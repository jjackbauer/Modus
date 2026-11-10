# Round-1 verification — orchestrator-run, reproducible.
# Evidence for D1 (seam identity), D2 (transcript/replay), HG1/HG2/HG3 and HG5 prefix check.
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$round = Join-Path $root 'docs/goal-llm-harness/round-1'
$runx = Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll = Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'

function Show-Total($logPath, $label) {
    $total = Select-String -Path $logPath -Pattern '^TOTAL ' | Select-Object -Last 1
    if ($null -eq $total) { throw "No TOTAL line in $logPath" }
    Write-Output ("{0}: {1}" -f $label, $total.Line)
}

Write-Output '=== build tests (Release) ==='
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --nologo 2>&1
$build | Set-Content -Path (Join-Path $round 'build.log')
$warn = ($build | Select-String -Pattern 'Warning\(s\)').Line
Write-Output ("build: " + (($build | Select-String -Pattern 'Build succeeded').Line))
Write-Output ("build: " + $warn)

Write-Output '=== full suite ==='
& $runx $dll *> (Join-Path $round 'full-suite.log')
Show-Total (Join-Path $round 'full-suite.log') 'full'

Write-Output '=== model-harness focused suite ==='
& $runx $dll ModelHarness *> (Join-Path $round 'focused-suite.log')
Show-Total (Join-Path $round 'focused-suite.log') 'focused'

Write-Output '=== build host ==='
dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Host/Wip.Tholdem.Host.csproj') -c Release --nologo 2>&1 | Set-Content -Path (Join-Path $round 'host-build.log')

Write-Output '=== legacy CLI: two fresh directories ==='
$base = Join-Path $root '.tmp/goal-llm-harness/round-1'
foreach ($name in @('legacy-a','legacy-b')) {
    $dir = Join-Path $base $name
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $out = dotnet run --project (Join-Path $root 'WIP/Wip.Tholdem.Host') -c Release --no-build -- train --seed 1099511627776 --iterations 2 --data-dir $dir 2>&1
    $out | Set-Content -Path (Join-Path $round ("legacy-" + $name + ".log"))
    Write-Output ("{0}: exit {1}; final line: {2}" -f $name, $LASTEXITCODE, ($out | Select-Object -Last 1))
}

$baseline = @{}
foreach ($line in Get-Content (Join-Path $root 'docs/goal-tholdem-harness/round-0/baseline-hashes.txt')) {
    if ($line -match '^\|\s*([^|]+?)\s*\|\s*([0-9a-f]{64})\s*\|\s*$') { $baseline[$Matches[1].Trim()] = $Matches[2] }
}
Write-Output ("baseline entries: " + $baseline.Count)
if ($baseline.Count -ne 7) { throw "Expected 7 baseline entries" }

foreach ($name in @('legacy-a','legacy-b')) {
    $dir = Join-Path $base $name
    $actual = @{}
    Get-ChildItem $dir -Recurse -File -Filter *.json | ForEach-Object {
        $rel = $_.FullName.Substring($dir.Length + 1)
        $actual[$rel.Replace('/','\')] = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
    }
    $mismatch = @()
    foreach ($key in $baseline.Keys) {
        if (-not $actual.ContainsKey($key)) { $mismatch += "missing:$key"; continue }
        if ($actual[$key] -ne $baseline[$key]) { $mismatch += "hash:$key" }
    }
    foreach ($key in $actual.Keys) { if (-not $baseline.ContainsKey($key)) { $mismatch += "extra:$key" } }
    $harnessDir = Test-Path (Join-Path $dir 'harness')
    Write-Output ("{0}: files={1} mismatches={2} harnessDir={3}" -f $name, $actual.Count, $mismatch.Count, $harnessDir)
    if ($mismatch.Count -gt 0) { $mismatch | ForEach-Object { Write-Output ("  " + $_) } }
}

Write-Output '=== five frozen component hashes vs round-0 snapshot ==='
$snapshot = @{}
foreach ($line in Get-Content (Join-Path $root 'docs/goal-llm-harness/round-0/frozen-baseline.txt')) {
    if ($line -match '^\| `([^`]+)` \| `([0-9a-f]{64})` \|$') { $snapshot[$Matches[1]] = $Matches[2] }
}
Write-Output ("snapshot entries: " + $snapshot.Count)
$frozenMismatch = 0
foreach ($path in $snapshot.Keys) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $root $path)).Hash.ToLowerInvariant()
    $state = if ($hash -eq $snapshot[$path]) { 'same' } else { 'CHANGED' }
    if ($state -eq 'CHANGED') { $frozenMismatch++ }
    Write-Output ("{0}: {1}" -f $path, $state)
}
Write-Output ("frozen mismatches: " + $frozenMismatch)

Write-Output '=== key-prefix scan (prefix pattern only; never the key value) ==='
$pattern = 'sk-[A-Za-z0-9_-]{20,}'
$scanRoots = @('docs/goal-llm-harness', 'WIP/Wip.Tholdem.ModelHarness', '.tmp/goal-llm-harness')
$hits = 0
foreach ($scan in $scanRoots) {
    $full = Join-Path $root $scan
    if (-not (Test-Path $full)) { continue }
    $found = Get-ChildItem $full -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.md','.json','.jsonl','.log','.txt','.cs','.ps1') -and $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        Select-String -Pattern $pattern -ErrorAction SilentlyContinue
    foreach ($hit in $found) { $hits++; Write-Output ("HIT " + $hit.Path + ":" + $hit.LineNumber) }
}
Write-Output ("key-prefix hits: " + $hits)

Write-Output '=== done ==='
