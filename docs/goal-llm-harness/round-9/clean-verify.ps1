$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$out=Join-Path $root 'docs/goal-llm-harness/round-9'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
Write-Output '=== clean rebuild (no incremental) ==='
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --no-incremental --nologo 2>&1
$build | Set-Content -Path (Join-Path $out 'clean-build.log')
($build | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== full suite ==='
& $runx $dll *> (Join-Path $out 'clean-full-suite.log')
(Select-String -Path (Join-Path $out 'clean-full-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $out 'clean-full-suite.log') -Pattern '^FAIL ').Line
Write-Output '=== key-prefix scan (35-char operator shape) ==='
$pattern = 'sk-[A-Za-z0-9_-]{30,40}'
$hits = 0
foreach ($scan in @('docs/goal-llm-harness','.tmp/goal-llm-harness/arms','.tmp/goal-llm-harness/ponder','WIP/Wip.Tholdem.ModelHarness')) {
  $found = Get-ChildItem $scan -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } | Select-String -Pattern $pattern -ErrorAction SilentlyContinue
  foreach ($hit in $found) { $hits++; Write-Output ("HIT " + $hit.Path + ':' + $hit.LineNumber) }
}
Write-Output ("key-prefix hits: " + $hits)
Write-Output '=== HarnessSync check ==='
dotnet run --project tools/HarnessSync -- --check 2>&1 | Select-Object -Last 3 | Out-String
Write-Output ("harness_sync_exit=" + $LASTEXITCODE)
Write-Output '=== done ==='