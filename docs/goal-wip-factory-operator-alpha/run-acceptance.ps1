<#
.SYNOPSIS
    Runs one blind acceptance requirement through the factory and emits a §1.2 acceptance manifest.

.DESCRIPTION
    Deliberately never prints the requirement text, the contract body or the proposal. Only digests, states,
    process identity and per-gate verdicts are emitted, so an operator can keep the requirement withheld from
    whoever inspects the output.

.NOTES
    The factory binary is not modified by this script. It drives the public CLI only.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Requirement,
    [Parameter(Mandatory = $true)][string]$Gate,          # e.g. PA-01
    [Parameter(Mandatory = $true)][string]$OutRoot,
    [int]$ServeSeconds = 3
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot 'WIP/Wip.Factory.Host'

function Invoke-Factory {
    param([string[]]$Arguments)
    $output = & dotnet run --project $project --no-build -- @Arguments 2>&1
    [pscustomobject]@{ Output = ($output | Out-String); Exit = $LASTEXITCODE }
}

function Get-Events {
    param([string]$ControlRoot)
    $log = Join-Path $ControlRoot 'factory.log'
    if (-not (Test-Path $log)) { return @() }
    Get-Content $log | ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { return }
    } | Where-Object { $_.Events } | ForEach-Object { $_.Events }
}

$workRoot = Join-Path $OutRoot $Gate
$repository = Join-Path $workRoot 'target'
$control = Join-Path $workRoot 'control'
$manifestPath = Join-Path $OutRoot ($Gate + '.manifest.json')

New-Item -ItemType Directory -Force -Path $repository, $workRoot | Out-Null
& git -C $repository init -b main --quiet 2>&1 | Out-Null

$result = [ordered]@{
    gate                = $Gate
    hostCommit          = (& git -C $repoRoot rev-parse HEAD).Trim()
    startedAtUtc        = (Get-Date).ToUniversalTime().ToString('O')
    requirementDigest   = $null
    contractDigest      = $null
    reviewDigest        = $null
    workItemId          = $null
    states              = [ordered]@{}
    headSha             = $null
    mergedSha           = $null
    targetHeadSha       = $null
    scenarios           = $null
    served              = $null
    processId           = $null
    timingsSeconds      = [ordered]@{}
    verdict             = 'not-accepted'
    failureCategory     = $null
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# 1. Intake (never echoes the requirement).
$submit = Invoke-Factory @('factory', 'requirement', 'submit', '--file', $Requirement, '--repo', $repository, '--control-root', $control)
if ($submit.Exit -ne 0) {
    $result.failureCategory = 'intake-refused'
    $result.timingsSeconds.intake = [int]$sw.Elapsed.TotalSeconds
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    Get-Content $manifestPath
    return
}
$workItem = ([regex]::Match($submit.Output, 'WORK-[0-9a-f]+')).Value
$result.workItemId = $workItem
$result.requirementDigest = ([regex]::Match($submit.Output, 'sha256:[0-9a-f]+')).Value
$result.states.proposed = 'Proposed'

# 2. Run to the contract proposal, then read ONLY the digest from the durable log.
$null = Invoke-Factory @('factory', 'run', '--until-idle', '--repo', $repository, '--control-root', $control)
$events = Get-Events -ControlRoot $control
$proposed = $events | Where-Object { $_.Type -eq 'factory.contract.proposed.v1' } | Select-Object -Last 1
if (-not $proposed) {
    $result.failureCategory = 'no-contract-proposal'
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    Get-Content $manifestPath
    return
}
$result.contractDigest = $proposed.Data.proposalDigest

# 3. Approve the exact proposal digest (operator authority, no content shown).
$approve = Invoke-Factory @('factory', 'contract', 'approve', '--work-item', $workItem, '--digest', $result.contractDigest, '--repo', $repository, '--control-root', $control)
if ($approve.Exit -ne 0) {
    $result.failureCategory = 'contract-approval-refused'
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    Get-Content $manifestPath
    return
}

# 4. Implementation + review.
$impl = [System.Diagnostics.Stopwatch]::StartNew()
$null = Invoke-Factory @('factory', 'run', '--until-idle', '--repo', $repository, '--control-root', $control)
$impl.Stop()
$result.timingsSeconds.implementationAndReview = [int]$impl.Elapsed.TotalSeconds

$events = Get-Events -ControlRoot $control
$review = $events | Where-Object { $_.Type -eq 'factory.review.accepted.v1' } | Select-Object -Last 1
if (-not $review) {
    $failed = $events | Where-Object { $_.Type -eq 'factory.review.rejected.v1' -or $_.Data.failureCategory } | Select-Object -Last 1
    $result.failureCategory = if ($failed) { ("" + $failed.Data.rejectReason + $failed.Data.failureCategory).Trim() } else { 'review-not-reached' }
    $status = Invoke-Factory @('factory', 'status', '--repo', $repository, '--control-root', $control)
    $result.states.final = ([regex]::Match($status.Output, 'WORK-[0-9a-f]+\s+(\w+)')).Groups[1].Value
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    Get-Content $manifestPath
    return
}

$result.reviewDigest = $review.Data.reviewDigest
$result.contractDigest = $review.Data.contractDigest
$result.headSha = $review.Data.headSha
$result.scenarios = $review.Data.scenarios
$result.states.reviewed = 'ReadyForDelivery'

# 5. Separate merge authorisation, merge, post-merge verification.
$mergeApprove = Invoke-Factory @('factory', 'approve', '--work-item', $workItem, '--review-digest', $result.reviewDigest, '--repo', $repository, '--control-root', $control)
if ($mergeApprove.Exit -ne 0) {
    $result.failureCategory = 'merge-approval-refused'
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    Get-Content $manifestPath
    return
}

$merge = [System.Diagnostics.Stopwatch]::StartNew()
$null = Invoke-Factory @('factory', 'run', '--until-idle', '--repo', $repository, '--control-root', $control)
$merge.Stop()
$result.timingsSeconds.mergeAndPostMerge = [int]$merge.Elapsed.TotalSeconds

$events = Get-Events -ControlRoot $control
$released = $events | Where-Object { $_.Type -eq 'factory.release.completed.v1' } | Select-Object -Last 1
if (-not $released) {
    $result.failureCategory = 'not-released'
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
    Get-Content $manifestPath
    return
}

$result.mergedSha = $released.Data.mergedSha
$result.scenariosPostMerge = $released.Data.postMergeScenarios
$result.states.released = 'Released'
$result.targetHeadSha = (& git -C $repository rev-parse HEAD).Trim()

# 6. Serve: supervised identity proof.
$serve = Invoke-Factory @('factory', 'serve', '--work-item', $workItem, '--repo', $repository, '--control-root', $control, '--duration-seconds', $ServeSeconds)
$events = Get-Events -ControlRoot $control
$observed = $events | Where-Object { $_.Type -eq 'factory.release.observed.v1' } | Select-Object -Last 1
if ($observed) {
    $result.served = $observed.Data.servedUrl
    $result.processId = $observed.Data.processId
    $result.states.operational = 'OperationallyValidated'
}

# 7. Gate verdict — mechanical, not judgemental.
$result.timingsSeconds.total = [int]$sw.Elapsed.TotalSeconds
$result.verdict = if (
    $result.states.released -eq 'Released' -and
    $result.mergedSha -eq $result.targetHeadSha -and
    $result.scenarios -notmatch '=fail' -and
    $result.scenariosPostMerge -notmatch '=fail' -and
    $result.states.operational -eq 'OperationallyValidated'
) { 'pass' } else { 'fail' }

$result | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
Get-Content $manifestPath
