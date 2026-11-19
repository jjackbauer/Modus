#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the factory gate suites and publishes separate measured counts (cases, failures, exact named coverage).

.DESCRIPTION
    CI entry point for v0.4 FAC-P1-08.4: a failed mandatory suite or a name-only placeholder named test makes the
    script exit non-zero. Missing named behaviour is published as a separate count; it is not silently folded into
    "tests passed", and the script does not claim PA-01/PA-02 acceptance.
#>
[CmdletBinding()]
param(
    [string]$Out = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = Join-Path $PSScriptRoot 'gate-report.json' }
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$suites = @(
    [ordered]@{ suite = 'factory-core';      project = 'WIP/Wip.Factory.Core.Tests/Wip.Factory.Core.Tests.csproj' },
    [ordered]@{ suite = 'factory-agents';    project = 'WIP/Wip.Factory.Agents.Tests/Wip.Factory.Agents.Tests.csproj' },
    [ordered]@{ suite = 'factory-host';      project = 'WIP/Wip.Factory.Host.Tests/Wip.Factory.Host.Tests.csproj' },
    [ordered]@{ suite = 'workspace-git';     project = 'WIP/Wip.Workspaces.Git.Tests/Wip.Workspaces.Git.Tests.csproj' },
    [ordered]@{ suite = 'architecture';      project = 'tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj' }
)

$measurements = @()
$anyFailed = $false

foreach ($suite in $suites) {
    $projectPath = Join-Path $repoRoot $suite.project
    $output = & dotnet test $projectPath -v q --nologo 2>&1
    $exit = $LASTEXITCODE
    $text = ($output | Out-String)

    $passed = [int]([regex]::Match($text, 'Passed:\s+(\d+)').Groups[1].Value)
    $failed = [int]([regex]::Match($text, 'Failed:\s+(\d+)').Groups[1].Value)

    if ($exit -ne 0 -or $failed -gt 0) { $anyFailed = $true }

    $measurements += [ordered]@{
        suite = $suite.suite
        project = $suite.project
        exitCode = $exit
        passedCases = $passed
        failedCases = $failed
    }
}

# Named coverage: regenerate from the compiled test assemblies at this revision.
$traceabilityPath = Join-Path $PSScriptRoot 'named-test-coverage.json'
$env:FACTORY_TRACEABILITY_OUT = $traceabilityPath
$traceOutput = & dotnet test (Join-Path $repoRoot 'WIP/Wip.Factory.Host.Tests/Wip.Factory.Host.Tests.csproj') `
    --filter 'FullyQualifiedName~Traceability_GivenMeasuredRepository' -v q --nologo 2>&1
$traceExit = $LASTEXITCODE
if ($traceExit -ne 0) { $anyFailed = $true }

$coverage = if (Test-Path $traceabilityPath) {
    Get-Content -Raw -Path $traceabilityPath | ConvertFrom-Json
} else {
    $null
}

$report = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    sourceRevision = (& git -C $repoRoot rev-parse HEAD).Trim()
    suites = $measurements
    totals = [ordered]@{
        passedCases = ($measurements | ForEach-Object { $_["passedCases"] } | Measure-Object -Sum).Sum
        failedCases = ($measurements | ForEach-Object { $_["failedCases"] } | Measure-Object -Sum).Sum
        suiteCount = $measurements.Count
    }
    namedCoverage = if ($coverage) { $coverage.counts } else { $null }
    namedCoverageDetail = 'named-test-coverage.json'
    pa01 = 'not-run'
    pa02 = 'not-run'
    acceptanceStatus = 'implementation-ready / acceptance-pending'
    gate = if ($anyFailed -or ($coverage -and $coverage.counts.nameOnly -gt 0)) { 'fail' } else { 'pass' }
}

[System.IO.File]::WriteAllText(
    $Out,
    ($report | ConvertTo-Json -Depth 8),
    (New-Object System.Text.UTF8Encoding $false))

Get-Content -Raw -Path $Out
if ($report.gate -ne 'pass') { exit 1 }
exit 0
