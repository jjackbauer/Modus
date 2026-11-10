# Round 1 — `--aggressive` search flag: implementation and evidence

Date: 2026-11-10 (America/Sao_Paulo)
Scope: `.tmp/goal-llm-harness/loop/LoopRecords.cs`, `Program.cs`, `RoundRunner.cs`.
No gate component, test, WIP product file, or Ponder host was touched.

## Change

- `LoopSettings` gains `bool AggressiveSearch = false` (positional default, so existing callers keep the
  non-aggressive shape). See [LoopRecords.cs](../../../.tmp/goal-llm-harness/loop/LoopRecords.cs).
- `--aggressive` is a boolean flag: the parser stores `"true"` and consumes **no** following value
  ([Program.cs](../../../.tmp/goal-llm-harness/loop/Program.cs)). It is shown in both usage lines.
- `SearchGenome` selects the grid from `settings.AggressiveSearch`
  ([RoundRunner.cs](../../../.tmp/goal-llm-harness/loop/RoundRunner.cs:324)):
  - aggressive: raises `{seed.raise, 0.70, 0.80, 0.85, 0.90, 0.95, 1.00}`, calls
    `{seed.call, 0.15, 0.20, 0.25, 0.30, 0.40}`, bluffs `{seed.bluff, 0.10, 0.14}`, sizings
    `{seed.sizing, 1.0, 1.1}`; genomes clamped; evaluated at `--search-seeds` x `--eval-hands` on the
    unchanged base `Derive(seed, 7000+round)`; highest LCB wins, tie-break higher mean.
  - non-aggressive: the previous 5x5x2x2 expression, byte for byte.
- `search.json` now carries `"aggressive": true|false` next to the existing `evaluated`
  ([RoundRunner.cs](../../../.tmp/goal-llm-harness/loop/RoundRunner.cs:372)).

Untouched: gate base seed `SeedDerivation.Derive(settings.Seed, MatchEvaluator.SeedHandBase + round, 0, 0, 0)`
with `MatchEvaluator.SeedHandBase = 9_000` ([RoundRunner.cs:131](../../../.tmp/goal-llm-harness/loop/RoundRunner.cs:131),
[MatchEvaluator.cs:13](../../../WIP/Wip.Tholdem.Agents/Learning/MatchEvaluator.cs:13)); `--eval-seeds` default 24
in [Program.cs](../../../.tmp/goal-llm-harness/loop/Program.cs).

## Build

Command (run from `C:\Users\ricar\dev\Modus`):

```
dotnet build .tmp\goal-llm-harness\loop\Loop.csproj -c Release
```

Observed output (exit 0):

```
  Determining projects to restore...
  All projects are up-to-date for restore.
  Wip.Abstractions -> C:\Users\ricar\dev\Modus\WIP\Wip.Abstractions\bin\Release\net10.0\Wip.Abstractions.dll
  Wip.Tholdem -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem\bin\Release\net10.0\Wip.Tholdem.dll
  Wip.Workspaces.Git -> C:\Users\ricar\dev\Modus\WIP\Wip.Workspaces.Git\bin\Release\net10.0\Wip.Workspaces.Git.dll
  Wip.Builder -> C:\Users\ricar\dev\Modus\WIP\Wip.Builder\bin\Release\net10.0\Wip.Builder.dll
  Wip.Artifacts.Local -> C:\Users\ricar\dev\Modus\WIP\Wip.Artifacts.Local\bin\Release\net10.0\Wip.Artifacts.Local.dll
  Wip.Agent.Basic -> C:\Users\ricar\dev\Modus\WIP\Wip.Agent.Basic\bin\Release\net10.0\Wip.Agent.Basic.dll
  Wip.Validation.DotNet -> C:\Users\ricar\dev\Modus\WIP\Wip.Validation.DotNet\bin\Release\net10.0\Wip.Validation.DotNet.dll
  Wip.Runtime -> C:\Users\ricar\dev\Modus\WIP\Wip.Runtime\bin\Release\net10.0\Wip.Runtime.dll
  Wip.Tholdem.Agents -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Agents\bin\Release\net10.0\Wip.Tholdem.Agents.dll
  Wip.Tholdem.ModelHarness -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.ModelHarness\bin\Release\net10.0\Wip.Tholdem.ModelHarness.dll
  Loop -> C:\Users\ricar\dev\Modus\.tmp\goal-llm-harness\loop\bin\Release\net10.0\Loop.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:01:03.21
```

Usage now advertises the flag (`dotnet .tmp\goal-llm-harness\loop\bin\Release\net10.0\Loop.dll`):

```
usage: Loop --live --secret-file <path> --seed <ulong> --rounds <n> --seats <n> --hands <n> --eval-seeds <n> --eval-hands <n> --out <dir> [--refiner-analysis <ponder|genome>] [--search-seeds <n>] [--aggressive]
       Loop --scripted --rounds <n> --seats <n> --hands <n> --eval-seeds <n> --eval-hands <n> --out <dir> [--refiner-analysis <ponder|genome>] [--search-seeds <n>] [--aggressive]
```

## Scripted smoke — aggressive

Command (run from `C:\Users\ricar\dev\Modus`; output directory deleted first so the run is fresh):

```
dotnet run --project .tmp\goal-llm-harness\loop\Loop.csproj -c Release -- --scripted --rounds 2 --seats 2 --hands 2 --eval-seeds 2 --eval-hands 2 --refiner-analysis genome --search-seeds 5 --aggressive --out .tmp\goal-llm-harness\loop\smoke-aggressive
```

Observed round lines (exit 0):

```
round=1 searchCandidates=120 searchBest=0.70/0.15/0.10/1.00 searchLcb=-11.706 searchMean=47.500
round=1 seats=2 hands=2 roundSeed=13032400259741297018 ponderTurns=5 toolCalls=0 toolNames=- legalActions=5 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=tholdem-strategy-v2-ab30fe48 rationale=llm-proposal gateSeeds=2 gateMean=25 gateLcb=-23.9991 verdict=lcb-below-margin verified=true promoted=false champion=tholdem-strategy-v1-c9d1a24b
round=2 searchCandidates=120 searchBest=0.70/0.40/0.10/1.00 searchLcb=-26.799 searchMean=7.500
round=2 seats=2 hands=2 roundSeed=2312279730489829546 ponderTurns=1 toolCalls=0 toolNames=- legalActions=1 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=tholdem-strategy-v2-0b4f27c6 rationale=llm-proposal gateSeeds=2 gateMean=0 gateLcb=-48.9991 verdict=lcb-below-margin verified=true promoted=false champion=tholdem-strategy-v1-c9d1a24b
modelKind=scripted
completedRounds=2
finalChampion=tholdem-strategy-v1-c9d1a24b
```

`smoke-aggressive/round-1/search.json` (read back):

```json
{"schema":"tholdem.loop.search.v1","round":1,"searchBaseSeed":1853858944220737094,"seeds":5,"hands":2,"aggressive":true,"evaluated":120,"best":{"raiseThreshold":0.7,"callThreshold":0.15,"bluffFrequency":0.1,"sizingMultiplier":1},"searchLcb":-11.705837329467677,"searchMean":47.5}
```

`smoke-aggressive/round-2/search.json` (read back):

```json
{"schema":"tholdem.loop.search.v1","round":2,"searchBaseSeed":7258714314292719605,"seeds":5,"hands":2,"aggressive":true,"evaluated":120,"best":{"raiseThreshold":0.7,"callThreshold":0.4,"bluffFrequency":0.1,"sizingMultiplier":1},"searchLcb":-26.799369729450945,"searchMean":7.5}
```

### Why 120, not 378

The scripted double's fixed proposal is `0.8/0.4/0.1/1.0`; every one of its four values is already an
anchor of the aggressive lists, so `Distinct` dedups each dimension: raises 7->6, calls 6->5,
bluffs 3->2, sizings 3->2, giving 6 x 5 x 2 x 2 = **120**. The grid *definition* is 7 x 6 x 3 x 3 =
**378 candidates (>= 300)** for a seed off the anchors. Same arithmetic, observed with the same
`Math.Round(...,4) + Distinct + OrderBy` rule over off-anchor seed `0.73/0.33/0.12/1.05`:

```
function D([double]$s, [double[]]$xs) { $all = @($s) + $xs; $all | ForEach-Object { [math]::Round($_,4) } | Sort-Object -Unique }
$ar=D 0.73 0.70,0.80,0.85,0.90,0.95,1.00
$dr=D 0.73 0.70,0.80,0.90,0.95
$ac=D 0.33 0.15,0.20,0.25,0.30,0.40
$dc=D 0.33 0.20,0.30,0.40,0.50
$ab=D 0.12 0.10,0.14
$db=D 0.12 0.10
$as=D 1.05 1.0,1.1
$ds=D 1.05 1.0
'aggressive raises=' + $ar.Count + ' calls=' + $ac.Count + ' bluffs=' + $ab.Count + ' sizings=' + $as.Count + ' grid=' + ($ar.Count*$ac.Count*$ab.Count*$as.Count)
'default    raises=' + $dr.Count + ' calls=' + $dc.Count + ' bluffs=' + $db.Count + ' sizings=' + $ds.Count + ' grid=' + ($dr.Count*$dc.Count*$db.Count*$ds.Count)
```

Observed output:

```
aggressive raises=7 calls=6 bluffs=3 sizings=3 grid=378
default    raises=5 calls=5 bluffs=2 sizings=2 grid=100
```

So the aggressive grid capacity is 378; for the on-anchor scripted seed the evaluated count is the
deduped 120, exactly the "exact count if the seed dedup reduces it" case.

## Scripted smoke — non-aggressive (default unchanged)

Command:

```
dotnet run --project .tmp\goal-llm-harness\loop\Loop.csproj -c Release -- --scripted --rounds 2 --seats 2 --hands 2 --eval-seeds 2 --eval-hands 2 --refiner-analysis genome --search-seeds 5 --out .tmp\goal-llm-harness\loop\smoke-nonaggressive
```

Observed round lines (exit 0):

```
round=1 searchCandidates=16 searchBest=0.70/0.50/0.10/1.00 searchLcb=-7.998 searchMean=12.500
round=1 seats=2 hands=2 roundSeed=13032400259741297018 ponderTurns=5 toolCalls=0 toolNames=- legalActions=5 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=tholdem-strategy-v2-952f8480 rationale=llm-proposal gateSeeds=2 gateMean=0 gateLcb=0 verdict=lcb-below-margin verified=true promoted=false champion=tholdem-strategy-v1-c9d1a24b
round=2 searchCandidates=16 searchBest=0.70/0.50/0.10/1.00 searchLcb=1.666 searchMean=20.000
round=2 seats=2 hands=2 roundSeed=2312279730489829546 ponderTurns=1 toolCalls=0 toolNames=- legalActions=1 errors=0 incumbent=tholdem-strategy-v1-c9d1a24b candidate=tholdem-strategy-v2-b0154e7b rationale=llm-proposal gateSeeds=2 gateMean=6.25 gateLcb=-30.4993 verdict=lcb-below-margin verified=true promoted=false champion=tholdem-strategy-v1-c9d1a24b
modelKind=scripted
completedRounds=2
finalChampion=tholdem-strategy-v1-c9d1a24b
```

`smoke-nonaggressive/round-1/search.json`:

```json
{"schema":"tholdem.loop.search.v1","round":1,"searchBaseSeed":1853858944220737094,"seeds":5,"hands":2,"aggressive":false,"evaluated":16,"best":{"raiseThreshold":0.7,"callThreshold":0.5,"bluffFrequency":0.1,"sizingMultiplier":1},"searchLcb":-7.997793991388924,"searchMean":12.5}
```

The pre-change run in `.tmp/goal-llm-harness/loop/smoke-search/round-1/search.json` also records
`"evaluated":16` for the same scripted seed (4 x 4 x 1 x 1), so the default grid is unchanged and
`"aggressive":false` is the only new key.

The default run also reproduces the pre-change gate exactly (same seeds, same values), read from
`smoke-search/strategies-report.json` vs `smoke-nonaggressive/strategies-report.json`:

```
old round 1 gateMean=0 gateLcb=0 decision=lcb-below-margin
new round 1 gateMean=0 gateLcb=0 decision=lcb-below-margin
old round 2 gateMean=6.25 gateLcb=-30.49932471012601 decision=lcb-below-margin
new round 2 gateMean=6.25 gateLcb=-30.49932471012601 decision=lcb-below-margin
```

## Live batch command for the orchestrator

The pre-registered batch script already uses the flag; one run is:

```
dotnet .tmp\goal-llm-harness\loop\bin\Release\net10.0\Loop.dll --live --secret-file .secrets\deepseek.env --seed 1099513000001 --rounds 4 --seats 6 --hands 20 --eval-seeds 24 --eval-hands 20 --refiner-analysis genome --search-seeds 50 --aggressive --out .tmp\goal-llm-harness\batch\run-01
```

All ten runs (seeds `1099513000001`..`1099513000010` pre-registered):

```
pwsh -NoProfile -File docs\goal-ponder-batch\run-batch.ps1 -StartRun 1 -EndRun 10
```

`--aggressive` is omitted from no command above; the gate command line is unchanged
(`--eval-seeds 24 --eval-hands 20`).

## Not done / limits

- The 10 live runs were **not** executed here (that is the orchestrator's deliverable); only the
  scripted smoke above was run. No live-model claim is made.
- The aggressive evaluated count for an arbitrary live proposal can be below 300 when the proposal
  lands on anchor values (dedup); the full grid capacity is 378 as shown.
