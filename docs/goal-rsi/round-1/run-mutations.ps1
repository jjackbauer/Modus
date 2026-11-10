param(
    [switch] $SkipBuildBefore = $false
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
Set-Location $repoRoot

$runner = Join-Path $repoRoot '.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe'
$testDll = Join-Path $repoRoot 'WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll'
$evidence = Join-Path $PSScriptRoot 'mutation-evidence.md'
$env:Logging__EventLog__LogLevel__Default = 'None'
$utf8 = New-Object System.Text.UTF8Encoding $false

$header = @'
# Mutation evidence - defense removed -> focused control fails -> restored by SHA-256

Each row is a deliberate one-line removal of one defense, followed by a Release build and the
focused control type. The observed failing test and the runner's `TOTAL` line are captured from the
run. The file is then restored byte-for-byte and its SHA-256 is compared with the pre-mutation value.
This is the same procedure used for the A1-A15 controls in `docs/tholdem/rsi-threat-model.md`.

| ID | Defense removed | Focused control | Observed | Restored |
|---|---|---|---|---|
'@
[System.IO.File]::WriteAllText($evidence, $header + [Environment]::NewLine, $utf8)

$mutations = @(
    @{
        Id = 'A2'
        Defense = 'seed-reuse admission (TrainingSeedRegistry call)'
        File = 'WIP/Wip.Tholdem.Web/Training/TrainingJobRegistry.cs'
        Old = '            if (!_seeds.TryReserve('
        New = '            if (false && !_seeds.TryReserve('
        Filter = 'TrainingJobApiTests'
    },
    @{
        Id = 'A3'
        Defense = 'verifier margin/seed checks (PromotionVerifier.Verify)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs'
        Old = @'
        var clearsMargin = report.LowerBoundBbPer100 > criteria.MarginBbPer100;
        var enoughSeeds = report.Seeds >= criteria.MinSeeds;
'@
        New = @'
        var clearsMargin = true;
        var enoughSeeds = true;
'@
        Filter = 'ThreatControlTests'
    },
    @{
        Id = 'A4'
        Defense = 'ledger hash-chain verification (LedgerChain.Verify)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/LedgerChain.cs'
        Old = @'
    public static LedgerIntegrity Verify(string? ledgerJson)
    {
        if (string.IsNullOrWhiteSpace(ledgerJson))
'@
        New = @'
    public static LedgerIntegrity Verify(string? ledgerJson)
    {
        return new LedgerIntegrity(true, true, 0, 0, 0, GenesisPreviousHash, LedgerIntegrity.VerifiedReason);

        if (string.IsNullOrWhiteSpace(ledgerJson))
'@
        Filter = 'ThreatControlTests'
    },
    @{
        Id = 'A5'
        Defense = 'champion-vs-itself refusal (MetaLoop sameChampionEverywhere)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs'
        Old = '            if (sameChampionEverywhere)'
        New = '            if (false && sameChampionEverywhere)'
        Filter = 'MetaLoopTests'
    },
    @{
        Id = 'A6/A15'
        Defense = 'fresh-holdout non-inferiority ratchet (MetaLoop BuildDecision)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs'
        Old = '        else if (fresh.LowerBoundBbPer100 < -options.Criteria.MarginBbPer100)'
        New = '        else if (false && fresh.LowerBoundBbPer100 < -options.Criteria.MarginBbPer100)'
        Filter = 'MetaLoopTests'
    },
    @{
        Id = 'A7'
        Defense = 'inert-proposal guard (TrainingIteration behavior check)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/TrainingIteration.cs'
        Old = '            if (candidate.BehaviorEquals(incumbent))'
        New = '            if (false && candidate.BehaviorEquals(incumbent))'
        Filter = 'ThreatControlTests'
    },
    @{
        Id = 'A8'
        Defense = 'server-side promote confirmation check (TrainingEndpoints)'
        File = 'WIP/Wip.Tholdem.Web/Training/TrainingEndpoints.cs'
        Old = '        if (request?.Confirm is not true)'
        New = '        if (request is null)'
        Filter = 'TrainingPromoteApiTests'
    },
    @{
        Id = 'A10'
        Defense = 'meta inner-iteration budget (MetaBudget.ConsumeEpisode)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs'
        Old = '            if (_episodes > _maxEpisodes)'
        New = '            if (false && _episodes > _maxEpisodes)'
        Filter = 'MetaLoopTests'
    },
    @{
        Id = 'A11'
        Defense = 'weak-seed iteration cap (TrainingJobValidator)'
        File = 'WIP/Wip.Tholdem.Web/Training/TrainingJobs.cs'
        Old = '        if (request.AllowWeakSeed && request.Iterations is > MaxWeakSeedIterations)'
        New = '        if (false && request.AllowWeakSeed && request.Iterations is > MaxWeakSeedIterations)'
        Filter = 'TrainingJobApiTests'
    },
    @{
        Id = 'A12'
        Defense = 'clock-free deterministic ledger entries (TrainingLoop.BuildLedgerEntry)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs'
        Old = @'
        builder.Append(",\"outcome\":");
        CanonicalJsonWriter.Text(builder, outcome);
        builder.Append('}');
'@
        New = @'
        builder.Append(",\"outcome\":");
        CanonicalJsonWriter.Text(builder, outcome);
        builder.Append(",\"runAt\":");
        CanonicalJsonWriter.Number(builder, DateTime.UtcNow.Ticks);
        builder.Append('}');
'@
        Filter = 'TrainingJobApiTests'
    },
    @{
        Id = 'A14'
        Defense = 'frozen-manifest comparison (FrozenComponents.Matches)'
        File = 'WIP/Wip.Tholdem.Agents/Learning/FrozenComponents.cs'
        Old = @'
        ArgumentNullException.ThrowIfNull(expected);

        if (!string.Equals(captured.Version, expected.Version, StringComparison.Ordinal))
'@
        New = @'
        ArgumentNullException.ThrowIfNull(expected);

        reason = MatchReason;
        return true;

        if (!string.Equals(captured.Version, expected.Version, StringComparison.Ordinal))
'@
        Filter = 'ThreatControlTests'
    }
)

# Preflight: every mutation must find its exact original text. A leftover mutation means the tree is
# dirty from an interrupted run; refuse rather than record misleading evidence.
foreach ($mutation in $mutations) {
    $path = Join-Path $repoRoot $mutation.File
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($path))
    if (-not $text.Contains($mutation.Old)) {
        throw "Dirty tree: mutation $($mutation.Id) pattern not found in $($mutation.File). Restore the file before rerunning."
    }
}

foreach ($mutation in $mutations) {
    $path = Join-Path $repoRoot $mutation.File
    $originalBytes = [System.IO.File]::ReadAllBytes($path)
    $originalHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $text = [System.Text.Encoding]::UTF8.GetString($originalBytes)
    $buildFailed = $false
    $buildLine = ''
    $testExit = 0
    $failLine = ''
    $totalLine = ''

    try {
        $mutated = $text.Replace($mutation.Old, $mutation.New)
        [System.IO.File]::WriteAllBytes($path, (New-Object System.Text.UTF8Encoding $false).GetBytes($mutated))

        $buildOutput = & dotnet build 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj' -c Release -v q --nologo 2>&1 | Out-String
        $buildExit = $LASTEXITCODE
        if ($buildExit -ne 0) {
            $buildFailed = $true
            $buildLine = ($buildOutput -split "`n" | Where-Object { $_ -match 'error ' } | Select-Object -First 1)
        }
        else {
            $testOutput = & $runner $testDll $mutation.Filter 2>&1 | Out-String
            $testExit = $LASTEXITCODE
            $failLine = ($testOutput -split "`n" | Where-Object { $_ -match '^FAIL ' } | Select-Object -First 1)
            $totalLine = ($testOutput -split "`n" | Where-Object { $_ -match '^TOTAL ' } | Select-Object -Last 1)
        }
    }
    finally {
        [System.IO.File]::WriteAllBytes($path, $originalBytes)
    }

    $restoredHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $restoredOk = [bool]($restoredHash -eq $originalHash)
    if (-not $restoredOk) {
        throw "Restore mismatch for $($mutation.Id): $restoredHash != $originalHash"
    }

    if ($buildFailed) {
        $row = "| $($mutation.Id) | $($mutation.Defense) | $($mutation.Filter) | BUILD FAILED: $($buildLine -replace '\|', '\|') | $($restoredHash.Substring(0,12))... (True) |"
        [System.IO.File]::AppendAllText($evidence, $row + [Environment]::NewLine, $utf8)
        Write-Output "BUILD-FAILED $($mutation.Id): $buildLine"
        continue
    }

    if (-not $failLine) { $failLine = '(no FAIL line captured)' }
    if (-not $totalLine) { $totalLine = '(no TOTAL line captured)' }
    $totalLine = $totalLine.Trim()
    $failLine = $failLine.Trim()
    if ($failLine.Length -gt 180) { $failLine = $failLine.Substring(0, 180) + '...' }
    $failLine = $failLine -replace '\|', '\|'

    $row = "| $($mutation.Id) | $($mutation.Defense) | $($mutation.Filter) | exit $testExit; $failLine; $totalLine | $($restoredHash.Substring(0,12))... (True) |"
    [System.IO.File]::AppendAllText($evidence, $row + [Environment]::NewLine, $utf8)
    Write-Output "MUTATION $($mutation.Id): exit=$testExit restored=$restoredOk"
    Write-Output "  $failLine"
    Write-Output "  $totalLine"
}
# Rebuild once with every file restored so the working tree's compiled output is not mutated.
& dotnet build 'WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj' -c Release -v q --nologo 2>&1 | Select-Object -Last 3
Write-Output "DONE evidence=$evidence"
