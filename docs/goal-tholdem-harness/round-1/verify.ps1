# Round-1 verification. Run from the repository root.
# This script is evidence tooling, not product code. It only reads and builds.

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$results = [System.Collections.Generic.List[string]]::new()
function Add-Result([string]$name, [bool]$ok, [string]$detail) {
    $tag = if ($ok) { 'PASS' } else { 'FAIL' }
    $results.Add("$tag | $name | $detail")
}

# 1. Build
dotnet build WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj -c Release --nologo -v q | Out-Null
$buildOk = $LASTEXITCODE -eq 0
Add-Result 'agents-build' $buildOk "exit=$LASTEXITCODE"

dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q | Out-Null
$testBuildOk = $LASTEXITCODE -eq 0
Add-Result 'tests-build' $testBuildOk "exit=$LASTEXITCODE"

$runner = Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$testDll = Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'

# 2. Focused refinement tests
$focused = & $runner $testDll 'HarnessRefinement' 2>&1
$focusedText = ($focused | Out-String).Trim()
$focusedOk = $focusedText -match 'FAILED 0' -and $focusedText -match 'PASSED ([1-9][0-9]*)'
Add-Result 'focused-refinement-tests' $focusedOk ($focusedText -replace "`r?`n", ' / ')

# 3. Full suite
$full = & $runner $testDll 2>&1
$fullText = ($full | Out-String).Trim()
$fullOk = $fullText -match 'FAILED 0' -and $fullText -match 'PASSED ([1-9][0-9]*)'
Add-Result 'full-suite' $fullOk ($fullText -replace "`r?`n", ' / ')

# 4. Frozen and adjacent source hashes
$frozenOk = $true
$frozenDetail = ''
$hashFile = Join-Path $root 'docs/goal-tholdem-harness/round-1/frozen-baseline-hashes.txt'
foreach ($line in Get-Content -LiteralPath $hashFile) {
    if ($line -match '^\| (WIP/[^|]+) \| ([0-9a-f]{64}) \|$') {
        $rel = $Matches[1].Trim()
        $expected = $Matches[2].Trim()
        $actual = (Get-FileHash (Join-Path $root $rel) -Algorithm SHA256).Hash.ToLower()
        if ($actual -ne $expected) {
            $frozenOk = $false
            $frozenDetail += "$rel changed; "
        }
    }
}
if ([string]::IsNullOrWhiteSpace($frozenDetail)) { $frozenDetail = 'all frozen/adjacent hashes match' }
Add-Result 'frozen-source-unchanged' $frozenOk $frozenDetail

# 5. Legacy CLI byte-equivalence against the recorded pre-change baseline
$legacyA = Join-Path $root '.tmp/goal-refinement/post-legacy-a'
$legacyB = Join-Path $root '.tmp/goal-refinement/post-legacy-b'
Remove-Item $legacyA, $legacyB -Recurse -Force -ErrorAction SilentlyContinue
& dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir $legacyA 2>&1 | Out-Null
$legacyExitA = $LASTEXITCODE
& dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir $legacyB 2>&1 | Out-Null
$legacyExitB = $LASTEXITCODE
$legacyOk = ($legacyExitA -eq 0 -and $legacyExitB -eq 0)
$legacyDetail = "exitA=$legacyExitA exitB=$legacyExitB; "
if ($legacyOk) {
    $baseline = @{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $root 'docs/goal-tholdem-harness/round-0/baseline-hashes.txt')) {
        if ($line -match '^\| ([^|]+\.json) \| ([0-9a-f]{64}) \|$') { $baseline[$Matches[1].Trim()] = $Matches[2].Trim() }
    }
    foreach ($dir in @($legacyA, $legacyB)) {
        $actual = @{}
        Get-ChildItem $dir -Recurse -File -Filter *.json | ForEach-Object {
            $rel = $_.FullName.Substring($dir.Length + 1).Replace('/', '\')
            $actual[$rel] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
        }
        if ($actual.Count -ne $baseline.Count) { $legacyOk = $false; $legacyDetail += "$dir count=$($actual.Count) expected=$($baseline.Count); " }
        foreach ($key in $baseline.Keys) {
            if (-not $actual.ContainsKey($key)) { $legacyOk = $false; $legacyDetail += "$dir missing $key; " }
            elseif ($actual[$key] -ne $baseline[$key]) { $legacyOk = $false; $legacyDetail += "$dir differs $key; " }
        }
    }
    if ($legacyDetail -notmatch 'missing|differs|count') { $legacyDetail += '7/7 hashes match in both directories' }
}
Add-Result 'legacy-byte-equivalence' $legacyOk $legacyDetail

# 6. Forbidden tokens in Learning (mirrors LearningSourceSafetyTests)
$forbidden = @('Process', 'HttpClient', 'git ', 'Assembly.Load', 'Compile', 'CSharpScript', 'CodeDom')
$scanOk = $true
$scanDetail = ''
foreach ($file in Get-ChildItem 'WIP/Wip.Tholdem.Agents/Learning' -Recurse -File -Filter *.cs) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($token in $forbidden) {
        if ($text.Contains($token)) { $scanOk = $false; $scanDetail += "$($file.Name):$token; " }
    }
}
if ([string]::IsNullOrWhiteSpace($scanDetail)) { $scanDetail = 'no forbidden token found' }
Add-Result 'learning-source-scan' $scanOk $scanDetail

# 7. Scope check: list changed/untracked Tholdem paths
$scope = & git status --porcelain=v1 -- WIP/Wip.Tholdem.Agents WIP/Wip.Tholdem.Tests 2>&1
$scopeText = ($scope | Out-String).Trim()
Add-Result 'scope-inventory' $true ($scopeText -replace "`r?`n", ' / ')

$report = @('# Round 1 verification','',"Run: $(Get-Date -Format o)",'','| Result | Check | Detail |','|---|---|---|') + ($results | ForEach-Object {
    $parts = $_ -split ' \| ', 3
    "| $($parts[0]) | $($parts[1]) | $($parts[2]) |"
})
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $root 'docs/goal-tholdem-harness/round-1/verification.md'), (($report -join "`r`n") + "`r`n"), $utf8)
$results
