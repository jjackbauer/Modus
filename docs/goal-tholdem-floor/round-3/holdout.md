# Round 3 — positive-evidence signal floor: HOLDOUT raw rows

Measured 2026-11-09 (files written 09:43:18 and 09:45:12 local). The two commands below are the single
pre-registered holdout use. This document records raw rows only. It does not aggregate, compare, or
interpret the measurement; the one identity observation requested by the orchestrator is reported as
raw data in "Raw identity observation".

## Scope

- Pre-registration: fresh holdout seeds `1099511827776 + i * 1000`
  (`docs/goal-tholdem-floor/round-1/experiment-design.md:24-25`); the holdout runs baseline, floor 0
  and the chosen floor once (`:27-31`); the holdout success rule is at `:51-60` and is **not** applied
  here.
- The chosen floor was fixed at **0.05** by the orchestrator before this run; the probe did not choose
  it.
- Settings: 5 iterations, 120 training hands, 24 evaluation seeds, 10 evaluation hands per match.
- Probe: `--split floor-holdout` uses `FloorHoldoutSeedBase = 1_099_511_827_776UL`
  (`Program.cs:14`) and selects it at `Program.cs:127-129`; the split's arm-set guard allows only
  `baseline,refinement` or `refinement` (`Program.cs:116-125`). No product code was edited.
- Attempts: **one attempt per command, no technical failure, no rerun.**

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

Time Elapsed 00:00:17.80
```

### Command 1 — baseline + floor 0 (60 rows)

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split floor-holdout --arms baseline,refinement --signal-floor 0 --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/holdout-floor0 --results .tmp/goal-floor/results-holdout-floor0.jsonl
```

Observed summary:

```text
rows-written=60
expected-rows=60
errors=0
elapsed=00:02:00.614
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-floor\results-holdout-floor0.jsonl
HOLDOUT_FLOOR0_EXIT=0
```

Exit code 0, one attempt, no rerun. Full console output is saved at
`.tmp/goal-floor/holdout-floor0/logs/probe-holdout-floor0.log` (71 lines: six header lines, 60 row
lines, five summary lines).

### Command 2 — chosen floor 0.05 (30 rows)

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split floor-holdout --arms refinement --signal-floor 0.05 --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/holdout-chosen --results .tmp/goal-floor/results-holdout.jsonl
```

Observed summary:

```text
rows-written=30
expected-rows=30
errors=0
elapsed=00:01:13.965
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-floor\results-holdout.jsonl
HOLDOUT_CHOSEN_EXIT=0
```

Exit code 0, one attempt, no rerun. Full console output is saved at
`.tmp/goal-floor/holdout-chosen/logs/probe-holdout.log` (41 lines: six header lines, 30 row lines,
five summary lines). The `*_EXIT` lines were printed by the wrapping shell, not by the probe.

## Raw files

| File | Rows | Bytes | SHA-256 |
|---|---:|---:|---|
| `.tmp/goal-floor/results-holdout-floor0.jsonl` | 60 | 24,381 | `3dcd6373c593ccfba5c79526ab9ba3894e67faee234646b6ae842979f0e791c9` |
| `.tmp/goal-floor/results-holdout.jsonl` | 30 | 12,439 | `27b6d7f12db2a6ffa9df93bc1a569dfe315fbe2c0e71cb083abd421b672926a3` |

## Raw rows

### `results-holdout-floor0.jsonl` (60 rows: baseline 30, refinement 30)

```json
{"split":"floor-holdout","arm":"baseline","seed":1099511827776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-0ceaa8fd","meanBbPer100":1.4583333333333333,"lowerBoundBbPer100":-0.35780378212530217,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":0,"totalHands":120,"divergenceRate":0}
{"split":"floor-holdout","arm":"baseline","seed":1099511827776,"seedIndex":0,"iteration":2,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-e1ba56f7","meanBbPer100":1.25,"lowerBoundBbPer100":-0.6385369527095646,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":10,"totalHands":120,"divergenceRate":0.08333333333333333}
{"split":"floor-holdout","arm":"refinement","seed":1099511827776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-e577cf9b","meanBbPer100":0.8333333333333334,"lowerBoundBbPer100":-2.738566484053132,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":5,"totalHands":120,"divergenceRate":0.041666666666666664}
```

### `results-holdout.jsonl` (30 rows: refinement 30)

```json
{"split":"floor-holdout","arm":"refinement","seed":1099511827776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-e577cf9b","meanBbPer100":0.8333333333333334,"lowerBoundBbPer100":-2.738566484053132,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":5,"totalHands":120,"divergenceRate":0.041666666666666664}
{"split":"floor-holdout","arm":"refinement","seed":1099511827776,"seedIndex":0,"iteration":2,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-a829c1b8","meanBbPer100":-41.666666666666664,"lowerBoundBbPer100":-98.25415267298969,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":10,"totalHands":120,"divergenceRate":0.08333333333333333}
{"split":"floor-holdout","arm":"refinement","seed":1099511827776,"seedIndex":0,"iteration":3,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-86f42856","meanBbPer100":4.583333333333333,"lowerBoundBbPer100":-0.06762388873453595,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":28,"totalHands":120,"divergenceRate":0.23333333333333334}
```

## Raw identity observation

The 30 rows in `results-holdout.jsonl` are byte-identical, in order, to the 30
`"arm":"refinement"` rows in `results-holdout-floor0.jsonl` (30/30 compared with a case-sensitive
string comparison). This is reported as a raw observation; the probe draws no conclusion from it and
the decision is left to the orchestrator.

## Verification

- `results-holdout-floor0.jsonl`: 60 lines, 60 parsed objects, `baseline` 30 / `refinement` 30,
  `"split":"floor-holdout"` in every row.
- `results-holdout.jsonl`: 30 lines, 30 parsed objects, all `"arm":"refinement"`,
  `"split":"floor-holdout"`.
- Both files: six holdout seeds (`1099511827776, 1099511828776, 1099511829776, 1099511830776,
  1099511831776, 1099511832776`), `totalHands = 120` in every row, 0 error lines, 0 inert rows, no
  `legacy-large` rows, and exactly the 17 required fields in the required order
  (`split,arm,seed,seedIndex,iteration,championId,challengerId,meanBbPer100,lowerBoundBbPer100,promoted,verified,outcome,isInert,zeroEffect,divergenceHands,totalHands,divergenceRate`).
- DEV and earlier artifacts unchanged: `.tmp/goal-refinement/experiment/results.jsonl` SHA-256
  `729a3a18…`, `results-control.jsonl` `6a7a7bd3…`, `results-holdout.jsonl` `d47057da…`; DEV floor
  files `results-floor005/010/020.jsonl` all `2735ab51…` with unchanged mtimes.
- Holdout data directories: `.tmp/goal-floor/holdout-floor0/baseline/seed-<i>/data` (6 seeds, 13
  files each, 0 `harness` directories), `.tmp/goal-floor/holdout-floor0/refinement/seed-<i>/data`
  and `.tmp/goal-floor/holdout-chosen/refinement/seed-<i>/data` (6 seeds, 14 files each including
  `harness/memory.json`, 6 `harness` directories each).
- Console logs re-encoded from UTF-16LE-with-BOM to UTF-8 without BOM; 71 and 41 lines, 8,829 and
  4,819 bytes, first bytes `70 72 6F 62` (`prob`).

## Problems and fixes

1. **Console log encoding.** `Tee-Object -FilePath ...` wrote both logs as UTF-16LE with BOM (first
   bytes `FF FE`). They were re-encoded in place to UTF-8 without BOM:

   ```powershell
   $text = [System.IO.File]::ReadAllText('<log path>')
   [System.IO.File]::WriteAllText('<log path>', $text, (New-Object System.Text.UTF8Encoding $false))
   ```

   Observed after: first bytes `70 72 6F 62` (`prob`); text content unchanged.
2. **No compile or runtime errors occurred**, and no attempt failed: build `0 Warning(s)` /
   `0 Error(s)`, `errors=0` in both runs. Both commands were run once; no rows were rerun.

## What could not be measured

- **Aggregation, comparison or interpretation.** Deliberately not computed here: no medians, paired
  ratios, bootstrap intervals, zero-effect rates, non-inferiority test or success decision. The
  pre-registered holdout rule (`experiment-design.md:51-60`) is not applied by this step.
- **Selected signal and its rate.** The probe does not persist the chosen failure signal or its rate,
  so whether the 0.05 floor filtered a signal is not directly measurable from the rows.
- **Poker strength / promotion-gate effect / frozen-gate hashes.** Not measured; outside this probe.
- **Repeatability.** Not measured: the holdout was used once, with no rerun.
- **Inert-iteration row output.** No holdout iteration was inert (`isInert` is false in every row),
  so the `Report == null` branch produced no rows.