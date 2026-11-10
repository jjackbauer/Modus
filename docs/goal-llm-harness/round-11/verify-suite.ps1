$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-11'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
& $runx $dll *> (Join-Path $round 'full-suite.log')
(Select-String -Path (Join-Path $round 'full-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'full-suite.log') -Pattern '^FAIL ').Line
& $runx $dll TholdemToolPromptMode *> (Join-Path $round 'focused-modes.log')
(Select-String -Path (Join-Path $round 'focused-modes.log') -Pattern '^TOTAL ').Line
Write-Output '=== done ==='