# Pre-registered experiment — positive-evidence signal floor

> Registered before any floor result is observed. The original DEV/HOLDOUT results from `docs/goal-tholdem-harness/` are already known; this document fixes the new arm and the fresh holdout before running it.

## Question

Does requiring positive evidence (`Rate >= floor`) for a selected signal preserve the refinement arm's exploration advantage while removing edits driven by a failure that never occurred?

## Floors and arms

- `baseline`: `HarnessRefinement = false`.
- `refinement` with `SignalRateFloor`:
  - `0.00` — the shipped behavior; the existing 60-row DEV result (`docs/goal-tholdem-harness` round 2) is reused as this arm, not rerun.
  - `0.05`
  - `0.10`
  - `0.20`

All runs: 5 iterations, 120 training hands, 24 evaluation seeds, 10 evaluation hands per match, default criteria.

## Seeds

- **DEV** (tuning; reused from the original experiment):
  `1099511627776 + i * 1000` for `i = 0..5`.
- **Fresh holdout** (never used in this session):
  `1099511827776 + i * 1000` for `i = 0..5`.

The holdout runs three arms on the same seeds, once: baseline, floor 0, and the chosen floor.
Floor 0 is included so the non-inferiority comparison is direct; it is not a second use of the
holdout.

The floor is chosen on DEV only. The holdout is run once, after the choice, and is never used to revise the floor.

## Metric

Decision divergence per iteration: the champion and challenger play the iteration's evaluation base seed for 120 hands; their `session.json` `handHashes` arrays are compared element-wise. This is the same metric and probe code as `docs/goal-tholdem-harness/round-1/experiment-design.md`.

Secondary: zero-effect evaluation rate (`|mean| < 0.05` and `|lcb| < 0.05`), verified promotions, mean bb/100.

## Selection rule (fixed now)

For each floor, compute the six paired per-seed median-divergence ratios against the baseline and against floor 0.

Choose the **smallest positive floor** that satisfies all of:

1. zero-effect rate <= the floor-0 zero-effect rate;
2. median divergence >= 0.9 × the floor-0 median divergence;
3. no promotion advantage is claimed (promotions are recorded, not optimized).

If no positive floor satisfies all three, choose **0**. The choice is recorded with its DEV numbers before the holdout.

## Holdout success rule

With the chosen floor (or 0):

- chosen arm vs baseline: median per-seed ratio >= 1.5 and paired 95% bootstrap lower bound > 1.0;
- zero-effect rate not higher than baseline;
- chosen arm vs floor 0 on the same holdout seeds: median ratio >= 0.9 (non-inferiority) and
  zero-effect rate not higher than floor 0.

If the chosen floor is 0, this is a fresh replication of the original claim under the same rule. If the holdout fails, the floor is rejected and the shipped policy remains 0.

## Commands

```powershell
# DEV floors (the probe is extended with --signal-floor and --split floor-holdout)
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split dev --arms refinement --signal-floor 0.05 --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/dev-floor005 --results .tmp/goal-floor/results-floor005.jsonl
# ... 0.10, 0.20

# Fresh holdout (chosen floor, once)
dotnet run --project .tmp/goal-refinement/Probe/Probe.csproj -c Release -- --split floor-holdout --arms baseline,refinement --signal-floor <chosen> --seeds 6 --iterations 5 --hands 120 --eval-seeds 24 --eval-hands 10 --root .tmp/goal-floor/holdout --results .tmp/goal-floor/results-holdout.jsonl
```

## Artifacts

- Raw rows: `.tmp/goal-floor/results-floor005.jsonl`, `...010`, `...020`, `...holdout.jsonl`; floor 0 is `.tmp/goal-refinement/experiment/results.jsonl`.
- DEV summary: `docs/goal-tholdem-floor/round-2/floor-selection.md` and `.json`.
- Holdout summary: `docs/goal-tholdem-floor/round-3/holdout-summary.md` and `.json`.
- Final decision: `docs/goal-tholdem-floor/final-report.md`.
