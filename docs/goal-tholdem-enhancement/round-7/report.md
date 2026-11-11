# Round 7 report — equal-budget proposer ablation (anchor 3)

**Status:** verified machinery plus a real four-task experiment; **outcome `no-benefit`**. Anchor 3
remains undemonstrated. Goal active.

## Delivered

- `SearchAblation`: a reusable equal-budget comparison of NoSearch, Random, Grid and Greedy proposal
  arms over multiple starting genomes and tasks, with selection, holdout, anchor, gate and canonical
  report (budget, candidates, results, outcome, hash).
- Corrected outcome rule: `benefit` only when Greedy has at least two verified tasks, zero unsafe
  results, and strictly more verified tasks than each control.
- A real experiment: 4 tasks, 8 candidates per arm, 737,280 focal hands, fixed seed, holdout opponent
  pool, Bonferroni-adjusted gate.

## Verified

| Check | Observed |
|---|---|
| Runner tests | 8/8 (5 contract + 3 outcome-classification) |
| Full suite | **525/525**, 0 failed |
| Build | 0 errors |
| Experiment outcome | **no-benefit** |
| Report hash | `751ca8bcdd3e90be5c6ef39e82f0dc29eca1dac1c2f4440c078bd2d2e7b81d92`; file SHA-256 equal |
| NoSearch | 4 Rejected, zero selection/holdout difference |
| Random | 1 Verified, 3 Unsafe |
| Grid | 0 Verified, 3 Unsafe, 1 Inconclusive |
| Greedy (proposal arm) | 0 Verified, 1 Unsafe, 2 Inconclusive, 1 Rejected |

The one verified cell (Random, `tight-three-tag`: +146.41 mean, +108.43 lower) is a single task from a
non-adaptive control whose other three tasks are unsafe. It is reported, not generalized.

## Correction record

The first run mechanically returned `benefit` because one non-control arm had one verified task. That
classification was rejected: the anchor requires a proposal process to outperform controls across
multiple tasks, and Random was unsafe on three of four. The rule was tightened and three synthetic
tests were added; the same deterministic data now classify as `no-benefit`.

## What this does not establish

- No learning-competence claim: Greedy did not beat the controls; anchor 3 is still open.
- No champion: no pointer moved, no strategy promoted.
- The search space is four genome parameters over the current weak heuristic policy. A meaningful
  learning test needs the policy expressiveness of Milestone C/D, not just these nudges.

## Next

EQ-005 side-pot call EV, then D4 policy semantics. The anchor-3 experiment must be repeated with a
richer policy family and a diagnosis-driven proposer before any learning claim.
