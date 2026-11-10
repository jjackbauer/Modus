# Round 2 — `legacy-large` DEV control (raw rows)

Measured 2026-11-08 (results-control written 12:44:47 local). This document records the raw control
run only. It does not aggregate, compare, or interpret the measurement.

## Control definition

- Third arm `legacy-large`: the **legacy** proposer (`HarnessRefinement: false`) with
  `ProposerGenome.MutationStep = 0.08` instead of the shipped default `0.02`
  (`WIP/Wip.Tholdem.Agents/Learning/ProposerGenome.cs:25-29`). It does not use
  `SignatureDrivenRefinementPolicy`.
- Exact call required by the follow-up and implemented in the probe:

  ```csharp
  TrainingLoop.Run(
      new TrainingOptions(seed, 5, 120, 24, 10, DataDirectory: dataDir, HarnessRefinement: false),
      proposer: new DeterministicStrategyProposer(new ProposerGenome(MutationStep: 0.08)))
  ```

  The probe's implementation is at `.tmp/goal-refinement/Probe/Program.cs:144-156`; the step
  constant is `Program.cs:15`. `DeterministicStrategyProposer` applies `Genome.MutationStep`
  (`WIP/Wip.Tholdem.Agents/Learning/StrategyProposer.cs:17-25`, `:38`), and `TrainingLoop.Run`
  resolves the supplied `proposer` at `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:88` and calls
  it on the legacy path at `:163` (the `HarnessRefinement` branch at `:154-160` is not taken).
- Seeds, iterations, hands, evaluation seeds and evaluation hands are identical to the accepted
  DEV probe. The run uses a separate root (`dev-control`) and a separate results file; the accepted
  `results.jsonl` was not rerun or modified.

## Probe extension

`Program.cs` gained optional `--arms` (comma-separated subset of `baseline`, `refinement`,
`legacy-large`) and `--results` (explicit output path). With neither flag the defaults are unchanged
(`baseline,refinement` and `<root>/../results.jsonl`). A guard refuses a partial arm set without
`--results`, so the round-2 dev results file cannot be overwritten accidentally
(`Program.cs:57-94`). The row schema is unchanged: the same 17 fields in the same order, with
`"arm":"legacy-large"`.

## Commands and observed output

### 1. Build

Command:

```powershell
dotnet build .tmp/goal-refinement/Probe/Probe.csproj -c Release --nologo -v m
```

Observed (tail):

```text
  Probe -> C:\Users\ricar\dev\Modus\.tmp\goal-refinement\Probe\bin\Release\net10.0\Probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:17.15
```

### 2. Control run

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split dev --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-refinement/experiment/dev-control --arms legacy-large --results .tmp/goal-refinement/experiment/results-control.jsonl
```

Observed console output: four header lines, 30 `row=` lines, then the summary. Full 39-line console
output saved at `.tmp/goal-refinement/experiment/dev-control/logs/probe-control.log`. Header and
summary:

```text
probe-root=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\dev-control
probe-results=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results-control.jsonl
probe-arms=legacy-large
probe-settings=split:dev seeds:6 iterations:5 hands:120 eval-seeds:24 eval-hands:10
...
rows-written=30
expected-rows=30
errors=0
elapsed=00:00:45.949
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results-control.jsonl
PROBE_EXIT=0
```

Process exit code: 0 (`PROBE_EXIT=0`; the background job also reported `exit code: 0`). The
`PROBE_EXIT` line is printed by the wrapping shell, not by the probe, so it is not in the log.

### 3. Post-run verification

```powershell
$lines = [System.IO.File]::ReadAllLines('.tmp/goal-refinement/experiment/results-control.jsonl')
$lines.Count
```

Observed: `30`.

```powershell
Get-FileHash .tmp/goal-refinement/experiment/results-control.jsonl -Algorithm SHA256
```

Observed: `6a7a7bd3c52cb588bf9d742b204571e2c81e575306b5fa4bf3a8158f38c153f9`
(11,825 bytes).

Row coverage read back from the file:

- 30 rows, all `"arm":"legacy-large"`; 5 rows for each of the six DEV seeds.
- Seeds present: `1099511627776, 1099511628776, 1099511629776, 1099511630776, 1099511631776, 1099511632776`.
- Every row has `totalHands = 120`; 0 error lines; 0 rows with `isInert = true`.
- Every row parses with exactly the 17 required fields in the required order.

Original accepted data untouched:

```powershell
Get-FileHash .tmp/goal-refinement/experiment/results.jsonl -Algorithm SHA256
Get-Item .tmp/goal-refinement/experiment/results.jsonl
```

Observed before and after this control run: SHA-256
`729a3a185967cd486ac1990fd5a4743a16f055413b8165b816cdfd2b892328b0`, 23,747 bytes,
last write `2026-11-08 12:36:48`.

Control data directories: `.tmp/goal-refinement/experiment/dev-control/legacy-large/seed-<i>/data`,
13 files each (1 `active-champion.json`, 1 `ledger.json`, 6 strategy JSON, 5 evaluation JSON).
No `harness` directory exists in any control data directory (0 of 6), consistent with
`HarnessRefinement: false`. Six empty `probe-history/` parent directories remain (0 files).

## Three raw rows verbatim

```json
{"split":"dev","arm":"legacy-large","seed":1099511627776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-e577cf9b","meanBbPer100":21.666666666666668,"lowerBoundBbPer100":-18.090826173918597,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":15,"totalHands":120,"divergenceRate":0.125}
{"split":"dev","arm":"legacy-large","seed":1099511627776,"seedIndex":0,"iteration":2,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-d4c6f22d","meanBbPer100":9.166666666666664,"lowerBoundBbPer100":-49.590567159770984,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":16,"totalHands":120,"divergenceRate":0.13333333333333333}
{"split":"dev","arm":"legacy-large","seed":1099511627776,"seedIndex":0,"iteration":3,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-12f5b9e4","meanBbPer100":-55.416666666666664,"lowerBoundBbPer100":-121.42239337003494,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":5,"totalHands":120,"divergenceRate":0.041666666666666664}
```

## Problems and fixes

1. **Console log encoding.** `Tee-Object -FilePath ...probe-control.log` wrote the log as UTF-16LE
   with BOM (first bytes `FF FE`). It was re-encoded in place to UTF-8 without BOM:

   ```powershell
   $text = [System.IO.File]::ReadAllText('.tmp/goal-refinement/experiment/dev-control/logs/probe-control.log')
   [System.IO.File]::WriteAllText('.tmp/goal-refinement/experiment/dev-control/logs/probe-control.log', $text, (New-Object System.Text.UTF8Encoding $false))
   ```

   Observed after: first bytes `70 72 6F 62` (`prob`), 39 lines, 4,603 bytes. Text content unchanged.
2. **No compile or runtime errors occurred**: build `0 Warning(s)` / `0 Error(s)` (section 1) and
   `errors=0` (section 2).

## What could not be measured

- **Holdout split.** Not run by scope. Rows contain only the six DEV seeds listed above.
- **Comparison with the accepted arms.** Not performed: this artifact contains the raw
  `legacy-large` rows only. No ratio, median, bootstrap or baseline/refinement comparison is computed.
- **Inert-iteration row output.** No control iteration was inert, so the `Report == null` branch
  (`meanBbPer100:null`, `lowerBoundBbPer100:null`) produced no rows.
- **Poker strength / promotion-gate effect and frozen-gate hashes.** Not measured; outside the
  probe's scope.