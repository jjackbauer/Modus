# Equal-budget proposer ablation v1

**Question:** does any proposal process find a better four-seat policy than its equal-budget controls,
across more than one starting genome and more than one opponent task?

**Answer observed:** **no-benefit.** The feedback-driven Greedy arm verified zero tasks. The Random
control verified one task (`tight-three-tag`) but was `Unsafe` on the other three; Grid verified none
and was `Unsafe` on three; NoSearch verified none. One Random win alongside three anchor regressions
is not a proposal-process benefit, and it is not advertised as one.

## Design

- Tasks: 2 starting genomes (default 0.72/0.45/0.12/1.0; tight 0.90/0.60/0.00/1.0) x 2 compositions
  (`primary-mixed`, `three-tag`) = 4 tasks.
- Arms: `NoSearch` (start repeated), `Random` (uniform genome samples), `Grid` (+/-0.1 coordinate
  points), `Greedy` (two rounds of four +0.1 neighbours, moving to the best each round).
- Budget: 8 candidates per arm per task; selection 48 blocks x 20 hands; holdout 96 blocks x 20 hands;
  anchor holdout on the complementary composition. 737,280 focal-policy hands total (both the
  challenger and the incumbent run, so the simulator executes twice that).
- Gate: paired four-seat evaluator, margin 10 bb/100, confidence 0.95, 8 planned candidates
  (Bonferroni-adjusted), anchor regression tolerance 5 bb/100, holdout opponent pool unseen by the
  selection pool.
- Fixed seed `0x5EEDBEEF1234`; no clock, GUID or ambient randomness.

## Observed results (holdout mean / lower bound, verdict)

| Task | NoSearch | Random | Grid | Greedy |
|---|---|---|---|---|
| default-primary | Rejected (0) | Unsafe 479.25 / 367.67 | Unsafe 45.11 / 0.14 | Rejected 24.57 / 2.10 |
| default-three-tag | Rejected (0) | Unsafe 499.83 / 404.86 | Unsafe 10.46 / -7.42 | Unsafe 21.29 / 4.41 |
| tight-primary | Rejected (0) | Rejected 166.49 / -3.52 | Unsafe 26.95 / -6.27 | Rejected 8.56 / -3.46 |
| tight-three-tag | Rejected (0) | **Verified 146.41 / 108.43** | Inconclusive 9.92 / 8.27 | Inconclusive 14.25 / 6.65 |

Outcome: `no-benefit`. Report hash `751ca8bcdd3e90be5c6ef39e82f0dc29eca1dac1c2f4440c078bd2d2e7b81d92`
(the report file hashes to exactly this value).

## Interpretation

- The search space can produce large primary-task gains (Random: +479 and +500 bb/100 on the default
  start) but the anchor guard rejects them as regression: these candidates exploit the primary
  composition and collapse elsewhere. That is the gate working, not a learning result.
- The single verified result is a Random candidate on `tight-three-tag`. It is one task, produced by a
  non-adaptive control, while the same arm is unsafe on three tasks. It does not support "the
  proposal process outperforms equal-budget non-LLM controls over multiple tasks".
- Greedy, the feedback-driven process under test, verified none and was unsafe once. No learning
  competence claim is made; anchor 3 remains undemonstrated.

## Correction applied

The first run mechanically classified the run as `benefit` because any non-NoSearch arm had one
verified task. That rule was too weak. The submitted rule now requires the Greedy arm to have at
least two verified tasks, zero unsafe results, and strictly more verified tasks than each control.
The same deterministic results re-classify as `no-benefit`. The correction is covered by three
synthetic unit tests.

## Reproduction

```
dotnet run --project docs/goal-tholdem-enhancement/experiments/learning-ablation-v1/harness -c Release -- \
  docs/goal-tholdem-enhancement/experiments/learning-ablation-v1/results
```

The harness writes `results/report.json` and prints the per-task verdicts; it exits 0 for either
outcome. A null result is not a failure of the run.
