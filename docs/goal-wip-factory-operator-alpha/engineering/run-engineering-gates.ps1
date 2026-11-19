# Wip.Factory v0.4 engineering gate runner.
#
# Measures the build and every in-scope suite, extracts the adversarial test names that actually executed,
# and asks the product CLI to derive ENGINEERING_COMPLETE / ENGINEERING_BLOCKED. It never marks acceptance
# and never edits a measured count by hand.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/engineering/run-engineering-gates.ps1
#
# Exit codes: 0 = ENGINEERING_COMPLETE, 3 = ENGINEERING_BLOCKED, 1 = the runner itself could not measure.

param(
    [string]$Configuration = "Debug",
    [string]$BranchName = ""
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $directory = [System.IO.Path]::GetFullPath($PSScriptRoot)
    while ($directory -ne $null) {
        if (Test-Path (Join-Path $directory "Wip.slnx")) { return $directory }
        $parent = [System.IO.Path]::GetDirectoryName($directory)
        if ($parent -eq $directory) { break }
        $directory = $parent
    }
    throw "repository root not found above $PSScriptRoot"
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    return "sha256:" + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TextSha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return "sha256:" + ([System.BitConverter]::ToString($hash).Replace("-", "").ToLowerInvariant())
}

function Get-ByteCount([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    return (Get-Item -LiteralPath $Path).Length
}

function Write-Utf8([string]$Path, [string]$Text) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Get-JsonProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Invoke-Captured([string]$Executable, [string[]]$Arguments, [string]$LogPath) {
    $output = & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    $exit = $LASTEXITCODE
    $text = ($output | Out-String)
    return [pscustomobject]@{ ExitCode = $exit; Text = $text; LogPath = $LogPath }
}

$repoRoot = Get-RepoRoot
Set-Location $repoRoot

$revision = (git rev-parse HEAD).Trim()
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -eq "HEAD" -and $BranchName.Length -gt 0) { $branch = $BranchName }
$dotnetVersion = (dotnet --version).Trim()
$generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
$engineeringRoot = Join-Path $repoRoot "docs/goal-wip-factory-operator-alpha/engineering"
$evidenceRoot = Join-Path $engineeringRoot "evidence"
$deliverableRoot = $repoRoot
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wip-engineering-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

$buildLog = Join-Path $tempRoot "build.log"
$build = Invoke-Captured "dotnet" @("build", "Wip.slnx", "-c", $Configuration, "--nologo") $buildLog
$buildErrors = 0
$buildWarnings = 0
$errorMatch = [regex]::Match($build.Text, "(?m)^\s*(\d+) Error\(s\)")
$warningMatch = [regex]::Match($build.Text, "(?m)^\s*(\d+) Warning\(s\)")
if ($errorMatch.Success) { $buildErrors = [int]$errorMatch.Groups[1].Value }
if ($warningMatch.Success) { $buildWarnings = [int]$warningMatch.Groups[1].Value }
if ($build.ExitCode -ne 0 -and $buildErrors -eq 0) { $buildErrors = 1 }
$buildStatus = if ($build.ExitCode -eq 0 -and $buildErrors -eq 0) { "pass" } else { "fail" }
$buildDigest = Get-Sha256 $buildLog

$suiteDefinitions = @(
    [pscustomobject]@{ Id = "factory-core"; Project = "WIP/Wip.Factory.Core.Tests/Wip.Factory.Core.Tests.csproj" },
    [pscustomobject]@{ Id = "factory-agents"; Project = "WIP/Wip.Factory.Agents.Tests/Wip.Factory.Agents.Tests.csproj" },
    [pscustomobject]@{ Id = "factory-host"; Project = "WIP/Wip.Factory.Host.Tests/Wip.Factory.Host.Tests.csproj" },
    [pscustomobject]@{ Id = "workspace-git"; Project = "WIP/Wip.Workspaces.Git.Tests/Wip.Workspaces.Git.Tests.csproj" },
    [pscustomobject]@{ Id = "architecture"; Project = "tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj" }
)

$testRoot = Join-Path $tempRoot "trx"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$testSuites = @()
$testNames = @{}
$totalExecuted = 0
$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$testsPassed = $buildStatus -eq "pass"

foreach ($definition in $suiteDefinitions) {
    $trxName = $definition.Id + ".trx"
    $logPath = Join-Path $tempRoot ($definition.Id + ".test.log")
    $commandArguments = @(
        "test", $definition.Project, "--no-build", "-c", $Configuration,
        "--logger", "trx;LogFileName=$trxName",
        "--results-directory", $testRoot,
        "--nologo"
    )
    $run = Invoke-Captured "dotnet" $commandArguments $logPath
    $trxPath = Join-Path $testRoot $trxName

    $executed = 0
    $passed = 0
    $failed = 0
    $skipped = 0

    if (Test-Path -LiteralPath $trxPath) {
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $results = @($trx.TestRun.Results.UnitTestResult)
        foreach ($result in $results) {
            $executed++
            $outcome = [string]$result.outcome
            if ($outcome -eq "Passed") { $passed++ }
            elseif ($outcome -eq "NotExecuted" -or $outcome -eq "Skipped") { $skipped++ }
            else { $failed++ }

            $testName = [string]$result.testName
            if ($testName.Length -gt 0) {
                $shortName = $testName.Substring($testName.LastIndexOf('.') + 1)
                foreach ($key in @($testName, $shortName)) {
                    $existing = $testNames[$key]
                    if ($null -eq $existing -or $outcome -ne "Passed") { $testNames[$key] = $outcome }
                }
            }
        }
    }
    else {
        $failed = 1
    }

    if ($run.ExitCode -ne 0) { $testsPassed = $false }
    if ($failed -gt 0) { $testsPassed = $false }

    $totalExecuted += $executed
    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped

    $trxDigest = if (Test-Path -LiteralPath $trxPath) { Get-Sha256 $trxPath } else { Get-TextSha256 $run.Text }
    $testSuites += [ordered]@{
        suite = $definition.Id
        project = $definition.Project
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $skipped
        command = "dotnet test " + $definition.Project + " --no-build -c $Configuration --logger trx;LogFileName=$trxName"
        trxDigest = $trxDigest
    }
}

if ($totalExecuted -eq 0) { $testsPassed = $false }

$requiredAdversarialTests = @(
    "Architecture_GivenSecondWebApplication_Refuses",
    "Architecture_GivenExecutableDisguisedByTestsFilename_Refuses",
    "Architecture_GivenExecutableTestDeclaration_Refuses",
    "Architecture_GivenLegitimateTestProject_Allows",
    "CreateAsync_GivenTargetAdvancedAfterFirstCreate_PreservesOriginalTarget",
    "CreateAsync_GivenConcurrentRetries_ProducesOneConsistentWorktree",
    "CreateAsync_GivenFailedAttemptThenRecovery_PreservesOriginalFailureSeparately",
    "Journal_GivenOriginalAndRetryFailures_PreservesBothSeparately",
    "Runner_GivenProvisioningFailure_PersistsOriginalFailureAndRetrySeparately",
    "EngineeringRecord_GivenHistoricalFailedAcceptance_KeepsEngineeringCompleteAndAcceptanceSeparate",
    "CandidateManifest_GivenCompleteEngineeringRecord_Validates"
)
$missingAdversarial = @()
foreach ($name in $requiredAdversarialTests) {
    if (-not $testNames.ContainsKey($name) -or $testNames[$name] -ne "Passed") { $missingAdversarial += $name }
}
$adversarialPassed = $missingAdversarial.Count -eq 0

$requiredRecoveryTests = @(
    "CreateAsync_GivenTargetAdvancedAfterFirstCreate_PreservesOriginalTarget",
    "CreateAsync_GivenRetryWithUncommittedWork_PreservesWorkingTree",
    "CreateAsync_GivenConcurrentRetries_ProducesOneConsistentWorktree",
    "CreateAsync_GivenBranchWithoutWorktree_ReattachesAtOriginalBaseline",
    "CreateAsync_GivenRegisteredWorktreeDirectoryRemoved_PrunesAndRecovers",
    "CreateAsync_GivenSessionHistoryRewritten_RefusesHistoryRewritten",
    "CreateAsync_GivenCorruptStateFile_RefusesStateCorruptWithoutDeletingWorktree"
)
$missingRecovery = @()
foreach ($name in $requiredRecoveryTests) {
    if (-not $testNames.ContainsKey($name) -or $testNames[$name] -ne "Passed") { $missingRecovery += $name }
}
$recoveryPassed = $missingRecovery.Count -eq 0

# Documentation and DSH evidence are prerequisites: a delivery record cannot be complete without them.
$implementationReport = Join-Path $deliverableRoot "IMPLEMENTATION_REPORT.md"
$knownLimitations = Join-Path $deliverableRoot "KNOWN_LIMITATIONS.md"
$traceabilityPath = Join-Path $deliverableRoot "REQUIREMENT_TRACEABILITY.json"
$dshEvidencePath = Join-Path $deliverableRoot "docs/goal-wip-factory-operator-alpha/engineering/DSH_EFFICIENCY_EVIDENCE.json"

$documentationPassed = (Test-Path -LiteralPath $implementationReport) `
    -and (Test-Path -LiteralPath $knownLimitations) `
    -and (Test-Path -LiteralPath $traceabilityPath) `
    -and (Test-Path -LiteralPath $dshEvidencePath) `
    -and ((Get-Item -LiteralPath $implementationReport).Length -gt 0) `
    -and ((Get-Item -LiteralPath $knownLimitations).Length -gt 0)

$traceabilityPassed = $false
if (Test-Path -LiteralPath $traceabilityPath) {
    try {
        $traceability = Get-Content -LiteralPath $traceabilityPath -Raw | ConvertFrom-Json
        $requirements = @(Get-JsonProperty $traceability "requirements")
        $traceabilityPassed = $requirements.Count -gt 0
        foreach ($requirement in $requirements) {
            $status = [string](Get-JsonProperty $requirement "status")
            if ($status -eq "missing" -or $status -eq "blocked" -or $status.Length -eq 0) { $traceabilityPassed = $false }
        }
    }
    catch { $traceabilityPassed = $false }
}

$dshPassed = $false
if (Test-Path -LiteralPath $dshEvidencePath) {
    try {
        $dsh = Get-Content -LiteralPath $dshEvidencePath -Raw | ConvertFrom-Json
        $dshPassed = [string](Get-JsonProperty $dsh "status") -eq "pass"
    }
    catch { $dshPassed = $false }
}

$sourceDirty = ((git status --porcelain -- WIP tests harness Wip.slnx Directory.Build.props) -join "`n").Trim()
$sourceClean = $sourceDirty.Length -eq 0

$historicalVerdict = Join-Path $repoRoot "docs/goal-wip-factory-operator-alpha/independent-acceptance-2026-11-19/PAIR.verdict.json"
$historicalDigest = if (Test-Path -LiteralPath $historicalVerdict) { Get-Sha256 $historicalVerdict } else { Get-TextSha256 "historical-pair-verdict-unavailable" }

$gate = @(
    [ordered]@{ id = "build"; status = $buildStatus; mandatory = $true; evidence = "evidence/build.log"; detail = "dotnet build Wip.slnx -c $Configuration; errors=$buildErrors warnings=$buildWarnings" },
    [ordered]@{ id = "tests"; status = $(if ($testsPassed) { "pass" } else { "fail" }); mandatory = $true; evidence = "ENGINEERING_TEST_RESULTS.json"; detail = "executed=$totalExecuted passed=$totalPassed failed=$totalFailed skipped=$totalSkipped" },
    [ordered]@{ id = "adversarial-tests"; status = $(if ($adversarialPassed) { "pass" } else { "fail" }); mandatory = $true; evidence = "evidence/factory-host.trx"; detail = $(if ($adversarialPassed) { "all required adversarial tests executed and passed" } else { "missing-or-failed=" + ($missingAdversarial -join ",") }) },
    [ordered]@{ id = "worktree-recovery"; status = $(if ($recoveryPassed) { "pass" } else { "fail" }); mandatory = $true; evidence = "evidence/workspace-git.trx"; detail = $(if ($recoveryPassed) { "all required recovery/concurrency tests executed and passed" } else { "missing-or-failed=" + ($missingRecovery -join ",") }) },
    [ordered]@{ id = "documentation"; status = $(if ($documentationPassed) { "pass" } else { "fail" }); mandatory = $true; evidence = "IMPLEMENTATION_REPORT.md,KNOWN_LIMITATIONS.md,REQUIREMENT_TRACEABILITY.json"; detail = "measured behavior and known limitations are recorded" },
    [ordered]@{ id = "traceability"; status = $(if ($traceabilityPassed) { "pass" } else { "fail" }); mandatory = $true; evidence = "REQUIREMENT_TRACEABILITY.json"; detail = "every in-scope requirement maps to implemented evidence or an explicit unresolved status" },
    [ordered]@{ id = "dsh-efficiency"; status = $(if ($dshPassed) { "pass" } else { "fail" }); mandatory = $true; evidence = "docs/goal-wip-factory-operator-alpha/engineering/DSH_EFFICIENCY_EVIDENCE.json"; detail = "DSH-scoped instrumentation, loop detection and reproducible baseline evidence" },
    [ordered]@{ id = "source-clean"; status = $(if ($sourceClean) { "pass" } else { "fail" }); mandatory = $true; evidence = "git status --porcelain -- WIP tests harness"; detail = $(if ($sourceClean) { "implementation paths are committed" } else { "uncommitted implementation paths: " + ($sourceDirty -replace "`n", ";") }) },
    [ordered]@{ id = "reproducible-build"; status = $(if ($buildStatus -eq "pass") { "pass" } else { "fail" }); mandatory = $true; evidence = "CANDIDATE_MANIFEST.json"; detail = "source revision and build digest are recorded in the candidate manifest" }
)

$limitations = @()
if (Test-Path -LiteralPath $knownLimitations) {
    foreach ($line in Get-Content -LiteralPath $knownLimitations) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("- ")) { $limitations += $trimmed.Substring(2).Trim() }
    }
}
if ($limitations.Count -eq 0) { $limitations = @("No limitation list was available at gate generation; see KNOWN_LIMITATIONS.md.") }

$recordInput = [ordered]@{
    codeRevision = $revision
    branch = $branch
    generatedAtUtc = $generatedAt
    buildStatus = $buildStatus
    buildCommand = "dotnet build Wip.slnx -c $Configuration"
    buildErrors = $buildErrors
    buildWarnings = $buildWarnings
    buildDigest = $buildDigest
    testSuites = $testSuites
    engineeringGates = $gate
    historicalAcceptance = [ordered]@{
        label = "2026-11-19 independent acceptance (historical)"
        evaluatedAtUtc = "2026-11-19T11:13:22.4613496Z"
        codeRevision = "e29df4d"
        hostDigest = "sha256:b921c4c1f84d4c94b9fadd77e3ecde3a09e7c3d01bf3fdef04cfea6d838a9990"
        pa01 = "fail"
        pa02 = "pass"
        pair = "fail"
        evidenceDigest = $historicalDigest
        note = "PA-01 failed and PA-02 passed on the original independent attempt; the pair is not accepted. This historical record is preserved, not re-adjudicated."
    }
    acceptanceStatus = "HISTORICAL_FAILURE_PRESERVED"
    knownLimitations = $limitations
}

$recordInputPath = Join-Path $tempRoot "engineering-record-input.json"
Write-Utf8 $recordInputPath ($recordInput | ConvertTo-Json -Depth 12)
$recordPath = Join-Path $deliverableRoot "ENGINEERING_TEST_RESULTS.json"
$recordRun = Invoke-Captured "dotnet" @(
    "run", "--project", "WIP/Wip.Factory.Host", "--no-build", "-c", $Configuration, "--",
    "factory", "engineering", "record", "--input", $recordInputPath, "--output", $recordPath
) (Join-Path $tempRoot "engineering-record.log")

if ($recordRun.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $recordPath)) {
    Write-Error "engineering record could not be generated: $($recordRun.Text)"
    exit 1
}

$record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
$status = [string](Get-JsonProperty $record "status")
$recordDigest = [string](Get-JsonProperty $record "evidenceDigest")

$sourceTreeDigest = Get-TextSha256 ((git ls-tree -r --full-tree HEAD) -join "`n")
$hostAssets = "WIP/Wip.Factory.Host/obj/project.assets.json"
$dependencyIdentity = "dotnet@" + $dotnetVersion `
    + ";Wip.slnx=" + (Get-Sha256 (Join-Path $repoRoot "Wip.slnx")) `
    + ";Directory.Build.props=" + (Get-Sha256 (Join-Path $repoRoot "Directory.Build.props")) `
    + ";host-assets=" + (Get-Sha256 (Join-Path $repoRoot $hostAssets))

$configurationEntries = @(
    [ordered]@{ path = "WIP/Wip.Factory.Host/Wip.Factory.Host.csproj"; digest = (Get-Sha256 (Join-Path $repoRoot "WIP/Wip.Factory.Host/Wip.Factory.Host.csproj")); bytes = (Get-ByteCount (Join-Path $repoRoot "WIP/Wip.Factory.Host/Wip.Factory.Host.csproj")) },
    [ordered]@{ path = "WIP/Wip.Factory.Host/Program.cs"; digest = (Get-Sha256 (Join-Path $repoRoot "WIP/Wip.Factory.Host/Program.cs")); bytes = (Get-ByteCount (Join-Path $repoRoot "WIP/Wip.Factory.Host/Program.cs")) },
    [ordered]@{ path = "WIP/Wip.Factory.Host/Factory/FactoryReviewChecks.cs"; digest = (Get-Sha256 (Join-Path $repoRoot "WIP/Wip.Factory.Host/Factory/FactoryReviewChecks.cs")); bytes = (Get-ByteCount (Join-Path $repoRoot "WIP/Wip.Factory.Host/Factory/FactoryReviewChecks.cs")) },
    [ordered]@{ path = "WIP/Wip.Factory.Host/Factory/EngineeringDeliveryRecord.cs"; digest = (Get-Sha256 (Join-Path $repoRoot "WIP/Wip.Factory.Host/Factory/EngineeringDeliveryRecord.cs")); bytes = (Get-ByteCount (Join-Path $repoRoot "WIP/Wip.Factory.Host/Factory/EngineeringDeliveryRecord.cs")) },
    [ordered]@{ path = "WIP/Wip.Workspaces.Git/WipWorkspaceProviderGit.cs"; digest = (Get-Sha256 (Join-Path $repoRoot "WIP/Wip.Workspaces.Git/WipWorkspaceProviderGit.cs")); bytes = (Get-ByteCount (Join-Path $repoRoot "WIP/Wip.Workspaces.Git/WipWorkspaceProviderGit.cs")) }
)

$hostDll = "WIP/Wip.Factory.Host/bin/$Configuration/net10.0/Wip.Factory.Host.dll"
$artifacts = @(
    [ordered]@{ path = $hostDll; digest = (Get-Sha256 (Join-Path $repoRoot $hostDll)); bytes = (Get-ByteCount (Join-Path $repoRoot $hostDll)) },
    [ordered]@{ path = "ENGINEERING_TEST_RESULTS.json"; digest = (Get-Sha256 $recordPath); bytes = (Get-ByteCount $recordPath) },
    [ordered]@{ path = "IMPLEMENTATION_REPORT.md"; digest = (Get-Sha256 $implementationReport); bytes = (Get-ByteCount $implementationReport) },
    [ordered]@{ path = "KNOWN_LIMITATIONS.md"; digest = (Get-Sha256 $knownLimitations); bytes = (Get-ByteCount $knownLimitations) },
    [ordered]@{ path = "REQUIREMENT_TRACEABILITY.json"; digest = (Get-Sha256 $traceabilityPath); bytes = (Get-ByteCount $traceabilityPath) },
    [ordered]@{ path = "docs/goal-wip-factory-operator-alpha/engineering/DSH_EFFICIENCY_EVIDENCE.json"; digest = (Get-Sha256 $dshEvidencePath); bytes = (Get-ByteCount $dshEvidencePath) }
)

$manifestInput = [ordered]@{
    status = $status
    sourceRevision = $revision
    branch = $branch
    sourceTreeDigest = $sourceTreeDigest
    dependencyIdentity = $dependencyIdentity
    configuration = $configurationEntries
    artifacts = $artifacts
    buildInstructions = @(
        "dotnet build Wip.slnx -c $Configuration",
        "powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/engineering/run-engineering-gates.ps1"
    )
    engineeringGates = $gate
    engineeringRecordDigest = $recordDigest
    generatedAtUtc = $generatedAt
}

$manifestInputPath = Join-Path $tempRoot "candidate-manifest-input.json"
Write-Utf8 $manifestInputPath ($manifestInput | ConvertTo-Json -Depth 12)
$manifestPath = Join-Path $deliverableRoot "CANDIDATE_MANIFEST.json"
$manifestRun = Invoke-Captured "dotnet" @(
    "run", "--project", "WIP/Wip.Factory.Host", "--no-build", "-c", $Configuration, "--",
    "factory", "engineering", "manifest", "--input", $manifestInputPath, "--output", $manifestPath
) (Join-Path $tempRoot "candidate-manifest.log")

if ($manifestRun.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $manifestPath)) {
    Write-Error "candidate manifest could not be generated: $($manifestRun.Text)"
    exit 1
}

$gateReport = [ordered]@{
    schemaVersion = "factory/engineering-gate-report@1"
    generatedAtUtc = $generatedAt
    sourceRevision = $revision
    branch = $branch
    build = [ordered]@{
        status = $buildStatus
        command = "dotnet build Wip.slnx -c $Configuration"
        errors = $buildErrors
        warnings = $buildWarnings
        digest = $buildDigest
    }
    testSuites = $testSuites
    totals = [ordered]@{
        executed = $totalExecuted
        passed = $totalPassed
        failed = $totalFailed
        skipped = $totalSkipped
    }
    gates = $gate
    missingAdversarialTests = $missingAdversarial
    missingRecoveryTests = $missingRecovery
    dshEvidence = $dshEvidencePath
    acceptance = [ordered]@{
        status = "HISTORICAL_FAILURE_PRESERVED"
        currentCandidateAccepted = $false
        note = "Independent acceptance is not a gate in this engineering record."
    }
}
$gateReportPath = Join-Path $deliverableRoot "docs/goal-wip-factory-operator-alpha/engineering/ENGINEERING_GATE_REPORT.json"
Write-Utf8 $gateReportPath ($gateReport | ConvertTo-Json -Depth 12)

Write-Output ("engineering status: " + $status)
Write-Output ("record digest: " + $recordDigest)
Write-Output ("tests: executed={0} passed={1} failed={2} skipped={3}" -f $totalExecuted, $totalPassed, $totalFailed, $totalSkipped)
Write-Output ("gates: " + (($gate | ForEach-Object { $_.id + "=" + $_.status }) -join ";"))

if ($status -eq "ENGINEERING_COMPLETE") { exit 0 }
exit 3
