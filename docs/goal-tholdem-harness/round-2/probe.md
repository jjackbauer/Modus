# Round 2 — DEV harness-refinement divergence probe (raw rows)

Measured 2026-11-08 (results written 12:36:48 local). This document records the raw probe run only.
It does not aggregate, compare, or interpret the measurement.

## Pre-registration and scope

- Question, arms and starting point: `docs/goal-tholdem-harness/round-1/experiment-design.md:5-18`.
- DEV seeds: `1099511627776 + i * 1000` for `i = 0..5`
  (`docs/goal-tholdem-harness/round-1/experiment-design.md:21-27`).
- Primary metric definition (same evaluation seed, 120 hands, two scratch histories, element-wise
  `handHashes` comparison): `docs/goal-tholdem-harness/round-1/experiment-design.md:29-40`.
- This run covers the six DEV seeds only. The holdout seeds (`1099511727776 + i * 1000`,
  `docs/goal-tholdem-harness/round-1/experiment-design.md:24-25`) were **not** run.
- Settings: 5 iterations, `TrainingHands = 120`, `EvaluationSeeds = 24`,
  `EvaluationHandsPerMatch = 10`; baseline `HarnessRefinement = false`, refinement
  `HarnessRefinement = true` (`WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs:4-13`).

Probe source (not added to `Wip.slnx`):

- `.tmp/goal-refinement/Probe/Probe.csproj` — `net10.0`, references
  `WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj` and `WIP/Wip.Tholdem/Wip.Tholdem.csproj`.
- `.tmp/goal-refinement/Probe/Program.cs` — arg parsing, per-seed/arm training, two scratch
  `MatchSimulator.Run` calls, `session.json` `handHashes` comparison, JSONL row writer.

## Commands and observed output

### 1. Build

Command:

```powershell
dotnet build .tmp/goal-refinement/Probe/Probe.csproj -c Release --nologo -v m
```

Observed (tail; the `Probe` output line and the build summary):

```text
  Probe -> C:\Users\ricar\dev\Modus\.tmp\goal-refinement\Probe\bin\Release\net10.0\Probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:17.81
```

### 2. Probe run (exact command required by the task)

Command:

```powershell
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split dev --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-refinement/experiment/dev
```

Observed console output: three header lines, 60 `row=` lines, then the summary. The full 68-line
console output is saved verbatim at
`.tmp/goal-refinement/experiment/dev/logs/probe-run.log`. Header and summary:

```text
probe-root=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\dev
probe-results=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results.jsonl
probe-settings=split:dev seeds:6 iterations:5 hands:120 eval-seeds:24 eval-hands:10
...
rows-written=60
expected-rows=60
errors=0
elapsed=00:01:43.950
results-path=C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results.jsonl
PROBE_EXIT=0
```

The process exit code was 0 (`PROBE_EXIT=0`; the background job also reported `exit code: 0`). The
`PROBE_EXIT` line is printed by the wrapping shell, not by the probe, so it is not in the log.

### 3. Post-run verification

```powershell
$lines = [System.IO.File]::ReadAllLines('.tmp/goal-refinement/experiment/results.jsonl')
$lines.Count
```

Observed: `60`.

```powershell
Get-FileHash .tmp/goal-refinement/experiment/results.jsonl -Algorithm SHA256
```

Observed: `729a3a185967cd486ac1990fd5a4743a16f055413b8165b816cdfd2b892328b0`
(23,747 bytes).

Every line parses as JSON with exactly the 17 required fields, in the required order:
`split,arm,seed,seedIndex,iteration,championId,challengerId,meanBbPer100,lowerBoundBbPer100,promoted,verified,outcome,isInert,zeroEffect,divergenceHands,totalHands,divergenceRate`.

Row coverage (read back from the file):

- 60 rows: `baseline` 30, `refinement` 30.
- 5 rows for every arm/seed pair (12 pairs), all `totalHands = 120`.
- Seeds present: `1099511627776, 1099511628776, 1099511629776, 1099511630776, 1099511631776, 1099511632776`.
- 0 error lines; 0 rows with `isInert = true`.

## Raw rows

- Exact path: `C:\Users\ricar\dev\Modus\.tmp\goal-refinement\experiment\results.jsonl`
- The task fixes this path as `<root>/../results.jsonl`; with the required
  `--root .tmp/goal-refinement/experiment/dev` it resolves to the path above.
- 60 lines, UTF-8 without BOM, one JSON object per line (final newline present).
- The probe deletes any pre-existing results file at start, then appends and flushes each row, so
  the 60 lines above come from this single run (`Program.cs`, JSONL writer).

First three raw rows verbatim:

```json
{"split":"dev","arm":"baseline","seed":1099511627776,"seedIndex":0,"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-0ceaa8fd","meanBbPer100":0.625,"lowerBoundBbPer100":-0.4485975155157047,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":0,"totalHands":120,"divergenceRate":0}
{"split":"dev","arm":"baseline","seed":1099511627776,"seedIndex":0,"iteration":2,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-e1ba56f7","meanBbPer100":1.6666666666666667,"lowerBoundBbPer100":-0.8819055273719452,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":5,"totalHands":120,"divergenceRate":0.041666666666666664}
{"split":"dev","arm":"baseline","seed":1099511627776,"seedIndex":0,"iteration":3,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-ea96d95d","meanBbPer100":0,"lowerBoundBbPer100":-2.4321368897959945,"promoted":false,"verified":true,"outcome":"rejected","isInert":false,"zeroEffect":false,"divergenceHands":1,"totalHands":120,"divergenceRate":0.008333333333333333}
```

Raw per-arm data directories: `.tmp/goal-refinement/experiment/dev/<arm>/seed-<i>/data`, each with
13 files (1 `active-champion.json`, 1 `ledger.json`, 6 strategy JSON, 5 evaluation JSON). Verified
with `Get-ChildItem ... -Recurse -File` (13 files for all 12 directories).

Arm-configuration check: `refinement` data directories contain a `harness/trajectory` directory
(6 of 6 seeds, empty after capture); `baseline` data directories contain no `harness` directory
(0 of 6). Observed with `Get-ChildItem ... -Directory -Recurse -Filter harness`.

## Implementation notes tied to source

- The loop persists the starting v1 champion and every challenger before evaluation; an inert
  proposal still gets a challenger artifact but no evaluation report
  (`WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:166-192`).
- `TrainingLoop.ReadPersistedStrategy(dataDir, id)` reads `<data>/strategies/<id>.json` and checks the
  id against the content hash (`WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:275-295`).
- The probe derives the iteration's evaluation base seed exactly as the loop does:
  `SeedDerivation.Derive(seed, EvaluationSeedHandBase + iteration, 0, 0, EvaluationSeedStream)`
  (`WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:195-200`,
  `WIP/Wip.Tholdem.Agents/SeedDerivation.cs:21-35`).
- Probe matches pass `HistoryDirectory` and `AlternateButton: true`; the simulator writes
  `session.json` whose `handHashes` array is the per-hand SHA-256 of the canonical hand record
  (`WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:29-40`, `:86-90`, `:173-182`, `:216-219`,
  `:331-340`).
- `totalHands` is `min(championHashes.Count, challengerHashes.Count)`; a count other than 120 throws
  and is written as an error row.

## Problems and fixes

1. **Stale round-1 scratch probe at the same path.** `.tmp/goal-refinement/probe/Probe.csproj` and
   `Program.cs` already existed (round-1 support probe; csproj referenced only
   `Wip.Tholdem.Agents`). Windows paths are case-insensitive, so `Probe/` and `probe/` are the same
   directory. Both files were replaced with the round-2 versions above, then `dotnet build` succeeded
   with `0 Warning(s)` / `0 Error(s)` (section 1).
2. **Console log encoding.** `Tee-Object -FilePath ...probe-run.log` wrote the log as UTF-16LE with
   BOM (first bytes `FF FE`). It was re-encoded in place to UTF-8 without BOM:

   ```powershell
   $text = [System.IO.File]::ReadAllText('.tmp/goal-refinement/experiment/dev/logs/probe-run.log')
   [System.IO.File]::WriteAllText('.tmp/goal-refinement/experiment/dev/logs/probe-run.log', $text, (New-Object System.Text.UTF8Encoding $false))
   ```

   Observed after: first bytes `70 72 6F 62` (`prob`), 68 lines, 8,684 bytes. Text content unchanged.
3. **No compile or runtime errors occurred** in the run itself: build `0 Error(s)` (section 1),
   `errors=0` (section 2).

## Notes and limits

- The two scratch history directories used for a probe match are deleted in a `finally` block
  (`Program.cs`, `DeleteDirectory`). The containing `probe-history/` directory itself remains as an
  empty container: 12 such directories under `experiment/dev`, 0 files total
  (`Get-ChildItem ... -Filter probe-history -Recurse` plus per-directory file count).
- No file under `WIP/` was edited by this step; `dotnet build`/`dotnet run` only wrote normal
  `bin/`/`obj/` build outputs. `Wip.slnx` was not edited and the probe project was not added to it.
- Files written by this step: the two probe source files, `.tmp/goal-refinement/experiment/dev/**`
  (training data and log), the task-mandated `.tmp/goal-refinement/experiment/results.jsonl`, and
  this document.

## What could not be measured

- **Holdout split.** Not run by scope. The rows contain only the six DEV seeds listed above.
- **Inert-iteration row output.** No iteration in this run was inert, so no row exercised the
  `Report == null` branch (`meanBbPer100:null`, `lowerBoundBbPer100:null`, `zeroEffect:false`). The
  writer implements it, but these 60 rows do not demonstrate its output.
- **Poker strength / promotion-gate effect.** Not measured; the pre-registration defines decision
  divergence only and computes it outside the promotion gate
  (`docs/goal-tholdem-harness/round-1/experiment-design.md:5-9`, `:29-40`). No claim is made here
  about either arm being stronger.
- **Aggregate quantities.** Deliberately not computed: no medians, paired ratios, bootstrap interval
  or zero-effect rate is produced by this step; only the raw rows exist.
- **Frozen-gate hash equality.** Not checked by this probe; that is a separate success clause
  (`docs/goal-tholdem-harness/round-1/experiment-design.md:52`) and outside the probe's measurement.