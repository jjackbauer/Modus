$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-10'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
Write-Output '=== full suite ==='
& $runx $dll *> (Join-Path $round 'final-suite.log')
(Select-String -Path (Join-Path $round 'final-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'final-suite.log') -Pattern '^FAIL ').Line
Write-Output '=== behavior proof ==='
& $runx $dll BehaviorProofCompliance *> (Join-Path $round 'final-behavior-proof.log')
(Select-String -Path (Join-Path $round 'final-behavior-proof.log') -Pattern '^TOTAL ').Line
Write-Output '=== ponder evidence ==='
& $runx $dll PonderEvidenceTests *> (Join-Path $round 'final-ponder-evidence.log')
(Select-String -Path (Join-Path $round 'final-ponder-evidence.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'final-ponder-evidence.log') -Pattern '^FAIL ').Line
Write-Output '=== done ==='