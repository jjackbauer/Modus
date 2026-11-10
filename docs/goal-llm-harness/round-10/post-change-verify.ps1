$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-10'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
Write-Output '=== build tests after prompt change ==='
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --nologo 2>&1
$build | Set-Content -Path (Join-Path $round 'post-change-build.log')
($build | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== full suite after prompt change ==='
& $runx $dll *> (Join-Path $round 'post-change-suite.log')
(Select-String -Path (Join-Path $round 'post-change-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'post-change-suite.log') -Pattern '^FAIL ').Line
Write-Output '=== build ponder host ==='
$pb = dotnet build (Join-Path $root '.tmp/goal-llm-harness/ponder/Ponder.csproj') -c Release --nologo 2>&1
$pb | Set-Content -Path (Join-Path $round 'ponder-rebuild.log')
($pb | Select-String -Pattern 'Build succeeded|Build FAILED|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== done ==='