# Round 3 — HOLDOUT harness-refinement divergence probe (raw rows)

Measured 2026-11-08 (results-holdout written 12:55:27 local). The holdout split was run exactly once.
This document records the raw rows only; it does not aggregate, compare, or interpret the measurement.

## Pre-registration and scope

- Holdout seeds: `1099511727776 + i * 1000` for `i = 0..5`
  (`docs/goal-tholdem-harness/round-1/experiment-design.md:24-25`); the holdout is used once for the
  final claim (`:58`).
- Arms: the two pre-registered arms only, `baseline` (`HarnessRefinement = false`) and `refinement`
  (`HarnessRefinement = true`), with the same settings as the accepted DEV probe: 5 iterations,
  120 training hands, 24 evaluation seeds, 10 evaluation hands per match.
- The `legacy-large` control was **not** run on the holdout. The probe refuses a holdout run whose arm
  list is not exactly `baseline,refinement` (`Program.cs:88-93`).
- Attempts: **1** (this run). No technical failure occurred and no rerun was performed.

## Probe extension for the holdout split

- `HoldoutSeedBase = 1_099_511_727_776UL` (`Program.cs:13`); the seed base is selected from the
  split (`Program.cs:95`).
- `--split` accepts only `dev` or `holdout` (`Program.cs:36-43`), and the holdout split requires an
  explicit, distinct `--results` path so the DEV results file cannot be overwritten
  (`Program.cs:111-118`).
- The `split` value in each row is written from the argument (`Program.cs:306-320`); the error
  serializer does the same (`Program.cs:352-358`). The 17-field row schema is otherwise unchanged.

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

Time Elapsed 00:00:15.42
```

### 2. Holdout run (exact required command)

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split holdout --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-refinement/experiment/holdout --results .tmp/goal-refinement/experiment/results-holdout.jsonl
```

Observed console output: five header lines, 60 `row=` lines, then the summary. Full 70-line console
output saved at `.tmp/goal-refinement/experiment/holdout/logs/probe-holdout.log`. Header and summary:

```text
probe-root=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\holdout
probe-results=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results-holdout.jsonl
probe-arms=baseline,refinement
probe-seed-base=1099511727776
probe-settings=split:holdout seeds:6 iterations:5 hands:120 eval-seeds:24 eval-hands:10
...
rows-written=60
expected-rows=60
errors=0
elapsed=00:01:42.206
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results-holdout.jsonl
PROBE_EXIT=0
```

Process exit code: 0 (`PROBE_EXIT=0`; the background job also reported `exit code: 0`). The
`PROBE_EXIT` line is printed by the wrapping shell, not by the probe, so it is not in the log.

### 3. Post-run verification

```powershell
$lines = [System.IO.File]::ReadAllLines('.tmp/goal-refinement/experiment/results-holdout.jsonl')
$lines.Count
```

Observed: `60`.

```powershell
Get-FileHash .tmp/goal-refinement/experiment/results-holdout.jsonl -Algorithm SHA256
```

Observed: `d47057da15c661fd560245ae96b9714e7882b85ee806c9319f882ab0872e5ca5`
(23,917 bytes).

Row coverage read back from the file:

- 60 rows: `baseline` 30, `refinement` 30; 10 rows per seed (5 iterations x 2 arms).
- `"split":"holdout"` in every row; 0 `legacy-large` rows.
- Seeds present: `1099511727776, 1099511728776, 1099511729776, 1099511730776, 1099511731776, 1099511732776`.
- Every row has `totalHands = 120`; 0 error lines; 0 rows with `isInert = true`.
- Every row parses with exactly the 17 required fields in the required order
  (`split,arm,seed,seedIndex,iteration,championId,challengerId,meanBbPer100,lowerBoundBbPer100,promoted,verified,outcome,isInert,zeroEffect,divergenceHands,totalHands,divergenceRate`).

DEV and control artifacts untouched by this run:

```powershell
Get-FileHash .tmp/goal-refinement/experiment/results.jsonl -Algorithm SHA256
Get-FileHash .tmp/goal-refinement/experiment/results-control.jsonl -Algorithm SHA256
```

Observed before and after: `results.jsonl` SHA-256
`729a3a185967cd486ac1990fd5a4743a16f055413b8165b816cdfd2b892328b0` (23,747 bytes, mtime
`2026-11-08 12:36:48`); `results-control.jsonl` SHA-256
`6a7a7bd3c52cb588bf9d742b204571e2c81e575306b5fa4bf3a8158f38c153f9` (11,825 bytes, mtime
`2026-11-08 12:44:47`). A sorted path+content manifest of all 236 files under
`experiment/dev` and `experiment/dev-control` hashed to
`213f8b3746709dd9fe074e1a40cfcfaf4d8099ed4fdf7863d3ce59e49cb8543e` both before and after.

Holdout data directories: `.tmp/goal-refinement/experiment/holdout/<arm>/seed-<i>/data`, 13 files
each (1 `active-champion.json`, 1 `ledger.json`, 6 strategy JSON, 5 evaluation JSON). The
`refinement` data directories contain `harness/trajectory` (6 of 6 seeds); `baseline` contains none
(0 of 6). Twelve empty `probe-history/` parent directories remain (0 files).

## Three raw rows verbatim

```json
{"split":"holdout","arm":"baseline","seed":1099511727776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-0ceaa8fd","meanBbPer100":-20.208333333333332,"lowerBoundBbPer100":-59.39642315130803,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":1,"totalHands":120,"divergenceRate":0.008333333333333333}
{"split":"holdout","arm":"baseline","seed":1099511727776,"seedIndex":0,"iteration":2,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-e1ba56f7","meanBbPer100":0.9375,"lowerBoundBbPer100":-1.436949544411223,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":5,"totalHands":120,"divergenceRate":0.041666666666666664}
{"split":"holdout","arm":"baseline","seed":1099511727776,"seedIndex":0,"iteration":3,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-ea96d95d","meanBbPer100":0,"lowerBoundBbPer100":-0.5898798407405944,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":0,"totalHands":120,"divergenceRate":0}
```

## Problems and fixes

1. **Console log encoding.** `Tee-Object -FilePath ...probe-holdout.log` wrote the log as UTF-16LE
   with BOM (first bytes `FF FE`). It was re-encoded in place to UTF-8 without BOM:

   ```powershell
   $text = [System.IO.File]::ReadAllText('.tmp/goal-refinement/experiment/holdout/logs/probe-holdout.log')
   [System.IO.File]::WriteAllText('.tmp/goal-refinement/experiment/holdout/logs/probe-holdout.log', $text, (New-Object System.Text.UTF8Encoding $false))
   ```

   Observed after: first bytes `70 72 6F 62` (`prob`), 70 lines, 8,682 bytes. Text content unchanged.
2. **No compile or runtime errors occurred** and no failed attempt needed a rerun: build
   `0 Warning(s)` / `0 Error(s)` (section 1) and `errors=0` (section 2).

## What could not be measured

- **Aggregation, comparison or interpretation.** Deliberately not computed here: no medians, paired
  ratios, bootstrap interval, zero-effect rate or baseline/refinement comparison. Only the raw rows
  exist; the pre-registered aggregation is a separate step (`experiment-design.md:48-53`).
- **Poker strength / promotion-gate effect.** Not measured; the pre-registration defines decision
  divergence only and computes it outside the promotion gate (`experiment-design.md:5-9`, `:29-40`).
- **Inert-iteration row output.** No holdout iteration was inert (`isInert` is false in every row),
  so the `Report == null` branch produced no rows.
- **Frozen-gate hash equality.** Not checked by this probe; a separate success clause
  (`experiment-design.md:52`).
- **`legacy-large` on holdout.** Deliberately not run; no holdout control rows exist.
- **Repeatability of the holdout.** Not measured: the holdout was used once, with no rerun.