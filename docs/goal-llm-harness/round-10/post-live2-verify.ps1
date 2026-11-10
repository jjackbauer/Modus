$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-10'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --nologo 2>&1
$build | Set-Content -Path (Join-Path $round 'post-live2-build.log')
($build | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
& $runx $dll *> (Join-Path $round 'post-live2-suite.log')
(Select-String -Path (Join-Path $round 'post-live2-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'post-live2-suite.log') -Pattern '^FAIL ').Line
& $runx $dll PonderEvidenceTests *> (Join-Path $round 'post-live2-focused.log')
(Select-String -Path (Join-Path $round 'post-live2-focused.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'post-live2-focused.log') -Pattern '^FAIL ').Line
Write-Output '=== done ==='