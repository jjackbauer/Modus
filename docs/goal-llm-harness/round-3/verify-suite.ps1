$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-3'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
Write-Output '=== build ==='
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --nologo 2>&1
$build | Set-Content -Path (Join-Path $round 'build.log')
($build | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== full ==='
& $runx $dll *> (Join-Path $round 'full-suite.log')
(Select-String -Path (Join-Path $round 'full-suite.log') -Pattern '^TOTAL ').Line
Write-Output '=== focused config ==='
& $runx $dll LiveModelConfiguration *> (Join-Path $round 'focused-config.log')
(Select-String -Path (Join-Path $round 'focused-config.log') -Pattern '^TOTAL ').Line
Write-Output '=== focused training llm ==='
& $runx $dll TrainingLoopLlmPolicy *> (Join-Path $round 'focused-training-llm.log')
(Select-String -Path (Join-Path $round 'focused-training-llm.log') -Pattern '^TOTAL ').Line
Write-Output '=== changed sources last 3h ==='
Get-ChildItem (Join-Path $root 'WIP') -Recurse -File -Filter *.cs | Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-3) -and $_.FullName -notmatch '\\obj\\|\\bin\\' } | Sort-Object LastWriteTime | ForEach-Object { $_.LastWriteTime.ToString('HH:mm:ss') + '  ' + $_.FullName.Replace($root + '\','') }
Write-Output '=== done ==='