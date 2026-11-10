$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-11'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
Write-Output '=== build tests ==='
$b = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --nologo 2>&1
$b | Set-Content -Path (Join-Path $round 'winner-build.log')
($b | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== build ponder host ==='
$pb = dotnet build (Join-Path $root '.tmp/goal-llm-harness/ponder/Ponder.csproj') -c Release --nologo 2>&1
($pb | Select-String -Pattern 'Build succeeded|Build FAILED|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== full suite ==='
& $runx $dll *> (Join-Path $round 'winner-suite.log')
(Select-String -Path (Join-Path $round 'winner-suite.log') -Pattern '^TOTAL ').Line
(Select-String -Path (Join-Path $round 'winner-suite.log') -Pattern '^FAIL ').Line
Write-Output '=== focused pivot ==='
foreach ($f in @('PonderEvidenceTests','TholdemToolPromptMode','TholdemRuntimeAgentTests','PonderGuardTests','FinalEvidenceTests')) {
  & $runx $dll $f *> (Join-Path $round ("winner-focus-" + $f + ".log"))
  $line = (Select-String -Path (Join-Path $round ("winner-focus-" + $f + ".log")) -Pattern '^TOTAL ').Line
  Write-Output ("{0}: {1}" -f $f, $line)
  $fail = (Select-String -Path (Join-Path $round ("winner-focus-" + $f + ".log")) -Pattern '^FAIL ').Line
  if ($fail) { Write-Output ("  " + $fail) }
}
Write-Output '=== done ==='