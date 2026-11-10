$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$round=Join-Path $root 'docs/goal-llm-harness/round-2'
$runx=Join-Path $root '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$dll=Join-Path $root 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
Write-Output '=== rebuild ==='
$build = dotnet build (Join-Path $root 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj') -c Release --no-incremental --nologo 2>&1
$build | Set-Content -Path (Join-Path $round 'build.log')
($build | Select-String -Pattern 'Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)').Line | ForEach-Object { Write-Output $_ }
Write-Output '=== full suite ==='
& $runx $dll *> (Join-Path $round 'full-suite.log')
(Select-String -Path (Join-Path $round 'full-suite.log') -Pattern '^TOTAL ').Line
Write-Output '=== focused ==='
& $runx $dll LlmRefinementPolicy *> (Join-Path $round 'focused-policy.log')
(Select-String -Path (Join-Path $round 'focused-policy.log') -Pattern '^TOTAL ').Line
& $runx $dll LlmPromptBuilder *> (Join-Path $round 'focused-builder.log')
(Select-String -Path (Join-Path $round 'focused-builder.log') -Pattern '^TOTAL ').Line
Write-Output '=== forbidden tokens in ModelHarness ==='
$hits = Get-ChildItem (Join-Path $root 'WIP/Wip.Tholdem.ModelHarness') -File -Filter *.cs | Select-String -Pattern 'HttpClient|System.Diagnostics.Process|CSharpScript|Assembly.Load|CodeDom'
if ($hits) { $hits | ForEach-Object { $_.Path + ':' + $_.LineNumber + ' ' + $_.Line.Trim() } } else { Write-Output 'none' }
Write-Output '=== key-shaped hits in new sources/tests ==='
Get-ChildItem (Join-Path $root 'WIP/Wip.Tholdem.ModelHarness'),(Join-Path $root 'WIP/Wip.Tholdem.Tests/ModelHarness') -File -Filter *.cs | Select-String -Pattern 'sk-[A-Za-z0-9_-]{20,}' | ForEach-Object { $_.Path + ':' + $_.LineNumber + ' LEN=' + $_.Matches[0].Value.Length }
Write-Output '=== changed sources in last 3h ==='
Get-ChildItem (Join-Path $root 'WIP') -Recurse -File -Filter *.cs | Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-3) -and $_.FullName -notmatch '\\obj\\|\\bin\\' } | Sort-Object LastWriteTime | ForEach-Object { $_.LastWriteTime.ToString('HH:mm:ss') + '  ' + $_.FullName.Replace($root + '\','') }
Write-Output '=== done ==='