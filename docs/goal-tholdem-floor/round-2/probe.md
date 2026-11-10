# Round 2 — positive-evidence signal floor: DEV raw rows

Measured 2026-11-09 (results files written 09:32:29–09:35:29 local). This document records the raw
floor rows only. It does not aggregate, compare, or interpret the measurement.

## Scope and pre-registration

- Floors and arms: `docs/goal-tholdem-floor/round-1/experiment-design.md:9-18`. The three new DEV
  runs are `refinement` with `SignalRateFloor` 0.05, 0.10 and 0.20.
- Floor 0 is **not rerun**: its rows are reused from
  `.tmp/goal-refinement/experiment/results.jsonl` (`experiment-design.md:12-13`, `:75`).
- DEV seeds: `1099511627776 + i * 1000` (`experiment-design.md:22-23`); six seeds per run.
- Settings: 5 iterations, 120 training hands, 24 evaluation seeds, 10 evaluation hands per match.
- The fresh floor-holdout seeds are `1099511827776 + i * 1000`
  (`experiment-design.md:24-25`). The `floor-holdout` split was added to the probe but **not run**.
- Product wiring: `TrainingOptions.SignalRateFloor` (`WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs:20`)
  is passed to `SignatureDrivenRefinementPolicy(minimumSignalRate:)` at
  `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:195-200`; the policy validates and stores it at
  `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs:37-59` and filters at
  `:161` with `signal.Rate >= _minimumSignalRate` (inclusive).

## Probe extension

- `--signal-floor <double>`: parsed and validated as finite in `[0, 1]` at `Program.cs:38-54`; passed
  into `TrainingOptions` only for the `refinement` arm, and as `0.0` for any other arm
  (`Program.cs:206-208`); printed in the run header at `Program.cs:163`.
- `--split floor-holdout`: seed base `1_099_511_827_776UL` (`Program.cs:14`, decimal
  `1099511827776`), accepted split name (`Program.cs:56-64`), seed selection (`Program.cs:117-119`).
  Like `holdout`, it requires an explicit distinct `--results` path
  (`Program.cs:135-142`) and the default `baseline,refinement` arm pair (`Program.cs:109-115`).
  It was not executed.
- The 17-field row schema is unchanged. The floor value is not a row field; each floor is
  distinguished by its `--results` file and `--root` directory.

## Commands and observed output

### Build

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

Time Elapsed 00:00:17.39
```

### Floor 0.05

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split dev --arms refinement --signal-floor 0.05 --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/dev-floor005 --results .tmp/goal-floor/results-floor005.jsonl
```

Observed summary:

```text
rows-written=30
expected-rows=30
errors=0
elapsed=00:01:09.366
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-floor\results-floor005.jsonl
FLOOR005_EXIT=0
```

### Floor 0.10

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split dev --arms refinement --signal-floor 0.10 --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/dev-floor010 --results .tmp/goal-floor/results-floor010.jsonl
```

Observed summary:

```text
rows-written=30
expected-rows=30
errors=0
elapsed=00:01:08.655
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-floor\results-floor010.jsonl
FLOOR010_EXIT=0
```

### Floor 0.20

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split dev --arms refinement --signal-floor 0.20 --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/dev-floor020 --results .tmp/goal-floor/results-floor020.jsonl
```

Observed summary:

```text
rows-written=30
expected-rows=30
errors=0
elapsed=00:01:08.248
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-floor\results-floor020.jsonl
FLOOR020_EXIT=0
```

Exit code was 0 for all three runs. Full console output is saved at
`.tmp/goal-floor/dev-floor005/logs/probe-floor005.log`,
`.tmp/goal-floor/dev-floor010/logs/probe-floor010.log` and
`.tmp/goal-floor/dev-floor020/logs/probe-floor020.log` (41 lines each: six header lines, 30 row
lines, five summary lines; the teed `*_EXIT` lines were printed by the wrapping shell, not the probe).

## Raw files

| Floor | Results file | Rows | Bytes | SHA-256 |
|---|---|---:|---:|---|
| 0.05 | `.tmp/goal-floor/results-floor005.jsonl` | 30 | 12,030 | `2735ab51d5605553179d670674d61b65dbdca07d2a7b2ee691a277f0ac51b08d` |
| 0.10 | `.tmp/goal-floor/results-floor010.jsonl` | 30 | 12,030 | `2735ab51d5605553179d670674d61b65dbdca07d2a7b2ee691a277f0ac51b08d` |
| 0.20 | `.tmp/goal-floor/results-floor020.jsonl` | 30 | 12,030 | `2735ab51d5605553179d670674d61b65dbdca07d2a7b2ee691a277f0ac51b08d` |

The three files are byte-identical (same SHA-256 and length). The three rows below are from
`results-floor005.jsonl` and are representative of all three files, each with 30 rows.

## Three raw rows (representative; 30 rows per floor file)

```json
{"split":"dev","arm":"refinement","seed":1099511627776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-4f4ea1bf","meanBbPer100":27.604166666666668,"lowerBoundBbPer100":-13.54336573987472,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":15,"totalHands":120,"divergenceRate":0.125}
{"split":"dev","arm":"refinement","seed":1099511627776,"seedIndex":0,"iteration":2,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-d4c6f22d","meanBbPer100":9.166666666666664,"lowerBoundBbPer100":-49.590567159770984,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":16,"totalHands":120,"divergenceRate":0.13333333333333333}
{"split":"dev","arm":"refinement","seed":1099511627776,"seedIndex":0,"iteration":3,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-12f5b9e4","meanBbPer100":-55.416666666666664,"lowerBoundBbPer100":-121.42239337003494,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":5,"totalHands":120,"divergenceRate":0.041666666666666664}
```

## Verification

- Each file: 30 lines, 30 parsed objects, all `"split":"dev"` and `"arm":"refinement"`, five rows
  for each of the six DEV seeds, `totalHands = 120` in every row, 0 error lines, 0 inert rows.
- Each row parses with exactly the 17 required fields in the required order:
  `split,arm,seed,seedIndex,iteration,championId,challengerId,meanBbPer100,lowerBoundBbPer100,promoted,verified,outcome,isInert,zeroEffect,divergenceHands,totalHands,divergenceRate`.
- Floor 0 source unchanged before and after: `.tmp/goal-refinement/experiment/results.jsonl`
  SHA-256 `729a3a185967cd486ac1990fd5a4743a16f055413b8165b816cdfd2b892328b0`, 23,747 bytes,
  mtime `2026-11-08 12:36:48`.
- `.tmp/goal-refinement/experiment/results-control.jsonl` (SHA-256 `6a7a7bd3…`) and
  `results-holdout.jsonl` (SHA-256 `d47057da…`) were not touched.
- No floor-holdout data was produced: `.tmp/goal-floor/holdout` does not exist and no
  `.tmp/goal-floor/results-holdout.jsonl` was written.
- Data directories: `.tmp/goal-floor/dev-floor00{5,10,20}/refinement/seed-<i>/data`, six seeds each,
  14 files per seed: 1 `active-champion.json`, 1 `ledger.json`, 5 evaluation JSON, 1
  `harness/memory.json`, 6 strategy JSON. Each floor has six `harness` directories (one per seed).

## Problems and fixes

1. **Console log encoding.** `Tee-Object -FilePath ...` wrote each log as UTF-16LE with BOM (first
   bytes `FF FE`). Each was re-encoded in place to UTF-8 without BOM:

   ```powershell
   $text = [System.IO.File]::ReadAllText('<log path>')
   [System.IO.File]::WriteAllText('<log path>', $text, (New-Object System.Text.UTF8Encoding $false))
   ```

   Observed after: first bytes `70 72 6F 62` (`prob`), 41 lines each, 4,714 / 4,713 / 4,713 bytes.
   Text content unchanged.
2. **No compile or runtime errors occurred**: build `0 Warning(s)` / `0 Error(s)` and `errors=0` in
   all three runs.

## What could not be measured

- **Floor-holdout split.** Added to the probe but deliberately not run; no holdout rows exist.
- **Floor-0 rerun.** Not performed; the existing DEV rows are reused as the pre-registration requires.
- **Aggregation, comparison or interpretation.** Deliberately not computed here: no medians, ratios,
  zero-effect rates, bootstrap intervals or comparisons between floors. Only the raw rows exist.
- **Selected signal and its rate.** The probe does not persist the chosen failure signal or its rate,
  so whether a floor filtered a signal is not directly measurable from these rows (the rows are
  byte-identical across the three floors; see the hash table).
- **Poker strength / promotion-gate effect / frozen-gate hashes.** Not measured; outside this probe.
- **Inert-iteration row output.** No floor iteration was inert (`isInert` is false in every row), so
  the `Report == null` branch produced no rows.