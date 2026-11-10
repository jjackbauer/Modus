# Final regression — orchestrator-run. Expected state: all green except the mandated Ponder tool-call gate.
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$round = Join-Path $root 'docs/goal-llm-harness/round-6'
$runx = Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll = Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'

Write-Output '=== build (Release) ==='
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --nologo 2>&1
$build | Set-Content -Path (Join-Path $round 'final-build.log')
($build | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }

Write-Output '=== full suite ==='
& $runx $dll *> (Join-Path $round 'final-suite.log')
(Select-String -Path (Join-Path $round 'final-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'final-suite.log') -Pattern '^FAIL ').Line

Write-Output '=== legacy CLI: two fresh directories ==='
dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj') -c Release --nologo 2>&1 | Set-Content -Path (Join-Path $round 'final-host-build.log')
$base = Join-Path $root '.tmp/goal-llm-harness/final-regression'
foreach ($name in @('legacy-a','legacy-b')) {
    $dir = Join-Path $base $name
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $out = dotnet run --project (Join-Path $root 'WIP/Wip.Tholdem.Host') -c Release --no-build -- train --seed 1099511627776 --iterations 2 --data-dir $dir 2>&1
    $out | Set-Content -Path (Join-Path $round ("final-legacy-" + $name + ".log"))
    Write-Output ("{0}: exit {1}; final line: {2}" -f $name, $LASTEXITCODE, ($out | Select-Object -Last 1))
}

$baseline = @{}
foreach ($line in Get-Content (Join-Path $root 'docs/goal-tholdem-harness/round-0/baseline-hashes.txt')) {
    if ($line -match '^\|\s*([^|]+?)\s*\|\s*([0-9a-f]{64})\s*\|\s*$') { $baseline[$Matches[1].Trim()] = $Matches[2] }
}
Write-Output ("baseline entries: " + $baseline.Count)
foreach ($name in @('legacy-a','legacy-b')) {
    $dir = Join-Path $base $name
    $actual = @{}
    Get-ChildItem $dir -Recurse -File -Filter *.json | ForEach-Object {
        $rel = $_.FullName.Substring($dir.Length + 1)
        $actual[$rel] = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
    }
    $mismatch = @()
    foreach ($key in $baseline.Keys) {
        if (-not $actual.ContainsKey($key)) { $mismatch += "missing:$key"; continue }
        if ($actual[$key] -ne $baseline[$key]) { $mismatch += "hash:$key" }
    }
    foreach ($key in $actual.Keys) { if (-not $baseline.ContainsKey($key)) { $mismatch += "extra:$key" } }
    Write-Output ("{0}: files={1} mismatches={2} harnessDir={3}" -f $name, $actual.Count, $mismatch.Count, (Test-Path (Join-Path $dir 'harness')))
    if ($mismatch.Count -gt 0) { $mismatch | ForEach-Object { Write-Output ("  " + $_) } }
}

Write-Output '=== five frozen component hashes ==='
$snapshot = @{}
foreach ($line in Get-Content (Join-Path $root 'docs/goal-llm-harness/round-0/frozen-baseline.txt')) {
    if ($line -match '^\|\s*`([^`]+)`\s*\|\s*`([0-9a-f]{64})`\s*\|\s*$') { $snapshot[$Matches[1]] = $Matches[2] }
}
$mismatch = 0
foreach ($path in $snapshot.Keys) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $root $path)).Hash.ToLowerInvariant()
    $state = if ($hash -eq $snapshot[$path]) { 'same' } else { 'CHANGED' }
    if ($state -eq 'CHANGED') { $mismatch++ }
    Write-Output ("{0}: {1}" -f $path, $state)
}
Write-Output ("frozen mismatches: " + $mismatch)

Write-Output '=== arm pointers ==='
foreach ($d in @('arm-llm','arm-deterministic')) {
    $p = ".tmp/goal-llm-harness/arms/$d/active-champion.json"
    $hash = (Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()
    Write-Output ("{0}: {1} {2}" -f $d, $hash, (Get-Content $p -Raw))
}

Write-Output '=== report sidecars ==='
foreach ($f in @('llm-arm-report.json','deterministic-arm-report.json')) {
    $h = (Get-FileHash -Algorithm SHA256 -Path "docs/goal-llm-harness/round-3/$f").Hash.ToLowerInvariant()
    $side = (Get-Content "docs/goal-llm-harness/round-3/$f.sha256").Trim()
    Write-Output "$f match=$($h -eq $side) sha=$h"
}

Write-Output '=== key-prefix scan (35-char operator shape) ==='
$pattern = 'sk-[A-Za-z0-9_-]{30,40}'
$roots = @('docs/goal-llm-harness', '.tmp/goal-llm-harness/arms', '.tmp/goal-llm-harness/ponder', 'WIP/Wip.Tholdem.ModelHarness')
$hits = 0
foreach ($scan in $roots) {
    $found = Get-ChildItem $scan -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } | Select-String -Pattern $pattern -ErrorAction SilentlyContinue
    foreach ($hit in $found) { $hits++; Write-Output ("HIT " + $hit.Path + ":" + $hit.LineNumber) }
}
Write-Output ("key-prefix hits: " + $hits)
Write-Output '=== done ==='