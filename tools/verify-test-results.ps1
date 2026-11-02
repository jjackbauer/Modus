param(
    [string] $ResultsDirectory = 'artifacts/ci-test-results',
    [string] $WaiverPath = 'docs/plan/test-waivers.json'
)

$ErrorActionPreference = 'Stop'

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw 'Not inside a git repository.'
}

Set-Location $repoRoot

if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    Write-Error "Results directory not found: $ResultsDirectory"
    exit 1
}

$waiver = Get-Content -Raw -LiteralPath $WaiverPath | ConvertFrom-Json
$expectedTotals = $waiver.expectedTotals
$knownAborted = @($waiver.knownAborted | ForEach-Object { $_.project })
$waivedFailures = @{}
foreach ($failure in @($waiver.failures)) {
    $waivedFailures[($failure.project + '|' + $failure.test)] = $failure
}

$namespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' | Sort-Object Name)
$seenProjects = @{}
$errors = New-Object System.Collections.Generic.List[string]

foreach ($trx in $trxFiles) {
    $project = $trx.BaseName
    $seenProjects[$project] = $true

    [xml] $document = Get-Content -Raw -LiteralPath $trx.FullName
    $ns = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
    $ns.AddNamespace('t', $namespace)
    $counters = $document.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
    if ($null -eq $counters) {
        $errors.Add("${project}: missing ResultSummary/Counters")
        continue
    }

    $total = [int] $counters.total
    $failed = [int] $counters.failed

    if (-not $expectedTotals.PSObject.Properties.Name.Contains($project)) {
        $errors.Add("${project}: unexpected test project (not in waiver register)")
        continue
    }

    $expected = [int] $expectedTotals.$project
    if ($total -le 0) {
        $errors.Add("${project}: zero tests discovered")
    }
    elseif ($total -lt $expected -and $knownAborted -notcontains $project) {
        $errors.Add("${project}: only $total tests discovered, expected $expected")
    }
    elseif ($total -lt $expected -and $knownAborted -contains $project) {
        Write-Output "WAIVED-ABORT ${project}: $total/$expected tests (known abort)"
    }

    $failedTests = @($document.SelectNodes('//t:UnitTestResult[@outcome="Failed"]', $ns))
    foreach ($test in $failedTests) {
        $key = $project + '|' + $test.testName
        if (-not $waivedFailures.ContainsKey($key)) {
            $errors.Add("${project}: unwaived failure: $($test.testName)")
        }
    }
}

foreach ($property in $expectedTotals.PSObject.Properties) {
    if (-not $seenProjects.ContainsKey($property.Name)) {
        $errors.Add("$($property.Name): missing TRX (project removed or did not run)")
    }
}

if ($errors.Count -gt 0) {
    Write-Output ('test_gate=FAIL errors=' + $errors.Count)
    $errors | ForEach-Object { Write-Output ('ERROR ' + $_) }
    exit 1
}

Write-Output ('test_gate=ok projects=' + $seenProjects.Count + ' waived_failures=' + $waivedFailures.Count)
exit 0
