<#
.SYNOPSIS
    Runs one acceptance requirement through the unchanged factory and asks the evaluator to verify the evidence.

.DESCRIPTION
    Evaluator-owned expectations are mandatory: this script never approves a contract it has not been told to
    expect, and it never decides "pass" itself. It emits an evidence manifest and delegates the verdict to
    `factory acceptance verify`, whose nonzero exit code fails the run (v0.4 FAC-P0-04).

    Only digests, states, process identity and per-gate results are printed. The requirement text and the
    contract body are not echoed, so an evaluator can keep them withheld from implementer-visible logs.

.PARAMETER Expectations
    Evaluator-authored JSON: gate, expectedScenarioIds, approvedContractDigest, frozenHostDigest,
    requireLiveModel, evaluatorOwned, evaluatorId.

.NOTES
    The factory binary is not modified by this script. It drives the public CLI only.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Requirement,
    [Parameter(Mandatory = $true)][string]$Gate,
    [Parameter(Mandatory = $true)][string]$OutRoot,
    [Parameter(Mandatory = $true)][string]$Expectations,
    [int]$ServeSeconds = 3,
    [ValidateSet('live', 'mock', 'none')][string]$ModelEvidenceKind = 'live',
    [switch]$KnownInput
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot 'WIP/Wip.Factory.Host'

function Invoke-Factory {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet run --project $project --no-build -- @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    [pscustomobject]@{ Output = ($output | Out-String); Exit = $exitCode }
}

function Invoke-Git {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        return ((& git @Arguments 2>&1 | Out-String).Trim())
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Get-Events {
    param([string]$ControlRoot)
    $log = Join-Path $ControlRoot 'factory.log'
    if (-not (Test-Path $log)) { return @() }
    Get-Content $log | ForEach-Object {
        try { $_ | ConvertFrom-Json } catch { return }
    } | Where-Object { $_.Events } | ForEach-Object { $_.Events }
}

function Get-Sha256Hex {
    param([string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($bytes) } finally { $sha.Dispose() }
    return (-join ($hash | ForEach-Object { $_.ToString('x2') }))
}

function Write-JsonFile {
    param([string]$Path, $Value)
    $json = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding $false))
}

function ConvertFrom-ScenarioSummary {
    param([string]$Summary, [string]$Phase)
    if ([string]::IsNullOrWhiteSpace($Summary)) { return @() }
    $results = @()
    foreach ($entry in $Summary.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $parts = $entry.Split('=')
        if ($parts.Count -ne 2) {
            $results += [ordered]@{ id = ''; phase = $Phase; passed = $false; detail = 'unparseable' }
        } else {
            $results += [ordered]@{ id = $parts[0]; phase = $Phase; passed = ($parts[1] -eq 'pass'); detail = $null }
        }
    }
    return $results
}

# 0. Evaluator expectations are mandatory and authoritative.
if (-not (Test-Path $Expectations)) {
    [Console]::Error.WriteLine("acceptance-expectations-missing")
    exit 2
}
$expect = Get-Content -Raw -Path $Expectations | ConvertFrom-Json
if (-not $expect.evaluatorOwned -or [string]::IsNullOrWhiteSpace($expect.evaluatorId)) {
    [Console]::Error.WriteLine("acceptance-evaluator-missing")
    exit 2
}
if (-not $expect.expectedScenarioIds -or $expect.expectedScenarioIds.Count -eq 0) {
    [Console]::Error.WriteLine("acceptance-expectations-empty")
    exit 2
}
if ([string]::IsNullOrWhiteSpace($expect.approvedContractDigest)) {
    [Console]::Error.WriteLine("acceptance-contract-not-inspected")
    exit 2
}

$workRoot = Join-Path $OutRoot $Gate
$repository = Join-Path $workRoot 'target'
$control = Join-Path $workRoot 'control'
$manifestPath = Join-Path $OutRoot ($Gate + '.evidence.json')
$verdictPath = Join-Path $OutRoot ($Gate + '.verdict.json')

New-Item -ItemType Directory -Force -Path $repository, $workRoot | Out-Null
Invoke-Git @('-C', $repository, 'init', '-b', 'main', '--quiet') | Out-Null

# 1. Freeze the host binary and its effective configuration digest.
$hostAssembly = Join-Path $project 'bin/Debug/net10.0/Wip.Factory.Host.dll'
$hostDigest = if (Test-Path $hostAssembly) {
    'sha256:' + (Get-FileHash -Algorithm SHA256 -Path $hostAssembly).Hash.ToLowerInvariant()
} else {
    'sha256:unbuilt'
}
$configSource = (Get-Content -Raw (Join-Path $project 'Wip.Factory.Host.csproj')) + "`n" + (Get-Content -Raw (Join-Path $project 'Program.cs'))
$configDigest = 'sha256:' + (Get-Sha256Hex $configSource)

$evidence = [ordered]@{
    gate                   = $Gate
    hostDigest             = $hostDigest
    configDigest           = $configDigest
    requirementDigest      = $null
    contractDigest         = $null
    modelEvidenceKind      = $ModelEvidenceKind
    modelProvider          = 'deepseek'
    knownInput             = [bool]$KnownInput
    blind                  = (-not $KnownInput -and ($Gate -eq 'PA-01' -or $Gate -eq 'PA-02'))
    preMerge               = @()
    postMerge              = @()
    released               = $false
    mergedSha              = $null
    targetHeadSha          = $null
    operationallyValidated = $false
    recordedAtUtc          = (Get-Date).ToUniversalTime().ToString('O')
    commands               = @()
}

function Complete-Run {
    param([string]$Failure)
    if ($Failure) { $evidence['failureCategory'] = $Failure }
    Write-JsonFile -Path $manifestPath -Value $evidence
    $verify = Invoke-Factory @('factory', 'acceptance', 'verify', '--manifest', $manifestPath, '--expectations', $Expectations)
    Write-JsonFile -Path $verdictPath -Value ([ordered]@{
        gate = $Gate
        exit = $verify.Exit
        output = $verify.Output.Trim()
        verdict = $(if ($verify.Exit -eq 0) { 'pass' } else { 'fail' })
    })
    Write-Output (Get-Content -Raw -Path $verdictPath)
    exit $verify.Exit
}

# 2. Intake (never echoes the requirement).
$submit = Invoke-Factory @('factory', 'requirement', 'submit', '--file', $Requirement, '--repo', $repository, '--control-root', $control)
$evidence.commands += [ordered]@{ command = 'requirement submit'; exit = $submit.Exit }
if ($submit.Exit -ne 0) { Complete-Run 'intake-refused' }

$workItem = ([regex]::Match($submit.Output, 'WORK-[0-9a-f]+')).Value
$evidence.requirementDigest = ([regex]::Match($submit.Output, 'sha256:[0-9a-f]+')).Value

# 3. Run to the proposal, then inspect the digest against the evaluator's expected contract BEFORE approval.
$run = Invoke-Factory @('factory', 'run', '--until-idle', '--repo', $repository, '--control-root', $control)
$evidence.commands += [ordered]@{ command = 'run (proposal)'; exit = $run.Exit }
$events = Get-Events -ControlRoot $control
$proposed = $events | Where-Object { $_.Type -eq 'factory.contract.proposed.v1' } | Select-Object -Last 1
if (-not $proposed) { Complete-Run 'no-contract-proposal' }

$evidence.contractDigest = $proposed.Data.proposalDigest
if ($evidence.contractDigest -ne $expect.approvedContractDigest) {
    # A model-produced or otherwise different contract cannot be auto-approved by this harness.
    Complete-Run 'contract-not-inspected'
}

$approve = Invoke-Factory @('factory', 'contract', 'approve', '--work-item', $workItem, '--digest', $evidence.contractDigest, '--repo', $repository, '--control-root', $control)
$evidence.commands += [ordered]@{ command = 'contract approve'; exit = $approve.Exit }
if ($approve.Exit -ne 0) { Complete-Run 'contract-approval-refused' }

# 4. Implementation + review.
$impl = Invoke-Factory @('factory', 'run', '--until-idle', '--repo', $repository, '--control-root', $control)
$evidence.commands += [ordered]@{ command = 'run (implementation+review)'; exit = $impl.Exit }

$events = Get-Events -ControlRoot $control
$review = $events | Where-Object { $_.Type -eq 'factory.review.accepted.v1' } | Select-Object -Last 1
if (-not $review) {
    $failed = $events | Where-Object { $_.Type -eq 'factory.stage.failed.v1' } | Select-Object -Last 1
    Complete-Run $(if ($failed) { '' + $failed.Data.failureCategory } else { 'review-not-reached' })
}

$evidence.preMerge = ConvertFrom-ScenarioSummary -Summary $review.Data.scenarios -Phase 'pre-merge'
$reviewDigest = $review.Data.reviewDigest

# 5. Separate merge authorisation, merge, post-merge verification.
$mergeApprove = Invoke-Factory @('factory', 'approve', '--work-item', $workItem, '--review-digest', $reviewDigest, '--repo', $repository, '--control-root', $control)
$evidence.commands += [ordered]@{ command = 'approve (merge)'; exit = $mergeApprove.Exit }
if ($mergeApprove.Exit -ne 0) { Complete-Run 'merge-approval-refused' }

$merge = Invoke-Factory @('factory', 'run', '--until-idle', '--repo', $repository, '--control-root', $control)
$evidence.commands += [ordered]@{ command = 'run (merge+verify)'; exit = $merge.Exit }

$events = Get-Events -ControlRoot $control
$released = $events | Where-Object { $_.Type -eq 'factory.release.completed.v1' } | Select-Object -Last 1
if (-not $released) { Complete-Run 'not-released' }

$evidence.released = $true
$evidence.mergedSha = $released.Data.mergedSha
$evidence.postMerge = ConvertFrom-ScenarioSummary -Summary $released.Data.postMergeScenarios -Phase 'post-merge'
$evidence.targetHeadSha = Invoke-Git @('-C', $repository, 'rev-parse', 'HEAD')

# 6. Serve: supervised identity proof over the pinned release.
$serve = Invoke-Factory @('factory', 'serve', '--work-item', $workItem, '--repo', $repository, '--control-root', $control, '--duration-seconds', $ServeSeconds)
$evidence.commands += [ordered]@{ command = 'serve'; exit = $serve.Exit }

$events = Get-Events -ControlRoot $control
$observed = $events | Where-Object { $_.Type -eq 'factory.release.observed.v1' } | Select-Object -Last 1
if ($observed) {
    $evidence.operationallyValidated = $true
}

Complete-Run $null
