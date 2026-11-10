# Pre-registered experiment — Tholdem harness refinement

> Registered before the first refinement result is observed. Changing this design after seeing results requires a new `DEC` entry and invalidates the holdout claim.

## Question

Does the opt-in harness-refinement arm produce proposals that change behaviour more often than the legacy ±0.02 proposer, without changing the promotion gate or the legacy artifacts?

This is an **exploration** question, not a poker-strength question. A larger decision divergence means the loop is testing policies that actually differ; it does not mean the policies are stronger.

## Arms

| Arm | Setting | Starting point |
|---|---|---|
| Baseline | `HarnessRefinement = false` | Fresh data directory, default v1 champion |
| Refinement | `HarnessRefinement = true` | Fresh data directory, default v1 champion |

Both arms use the same `TrainingOptions` otherwise: 5 iterations, `TrainingHands = 120`, `EvaluationSeeds = 24`, `EvaluationHandsPerMatch = 10`, default criteria (`margin = 5`, `minSeeds = 24`).

## Seeds

- **Dev seeds** (tuning only, results never used for the final claim):
  `1099511627776 + i * 1000` for `i = 0..5`.
- **Holdout seeds** (one final run):
  `1099511727776 + i * 1000` for `i = 0..5`.

All seeds are above the 2^40 guard. Each seed is paired across arms: the same seed runs baseline and refinement in separate fresh directories.

## Primary metric — decision divergence

For each completed iteration:

1. Read the champion and challenger strategy artifacts from the arm's data directory (`TrainingLoop.ReadPersistedStrategy`).
2. Derive the iteration's evaluation base seed exactly as the loop does:
   `SeedDerivation.Derive(jobSeed, TrainingLoop.EvaluationSeedHandBase + iteration, 0, 0, TrainingLoop.EvaluationSeedStream)`.
3. Run `MatchSimulator.Run` twice with the same seed and 120 hands into two scratch history directories: once with the champion agent in seat 0 against `TightAggressiveAgent`, once with the challenger in seat 0. Both runs use `AlternateButton = true`.
4. Read `session.json` from each scratch directory and compare the `handHashes` arrays element by element. A hand diverges when its canonical action-log hash differs.
5. `divergence(iteration) = divergingHands / 120`.

The metric is deterministic for a given seed, iteration and pair of genomes. It is computed outside the promotion gate and changes no pointer.

## Secondary metrics

- **Zero-effect evaluation rate**: fraction of iterations with `|meanBbPer100| < 0.05` and `|lowerBoundBbPer100| < 0.05`.
- **Verified promotions**: count of iterations with `verified == true && outcome == "promoted"`.
- **Mean bb/100**: arithmetic mean of per-iteration `meanBbPer100`.

## Aggregation and success rule

- For each holdout seed, compute the median divergence across its five iterations for each arm, then the ratio `refinementMedian / baselineMedian`. If the baseline median is zero, use the pooled mean instead and record the substitution.
- Report the six paired ratios, their median, and a paired bootstrap 95% confidence interval (10,000 resamples, fixed RNG seed 20261108, percentile method).
- **Success**: holdout median ratio >= 1.5, bootstrap lower bound > 1.0, refinement zero-effect rate <= baseline zero-effect rate, and the frozen-gate files are byte-identical to `round-1/frozen-baseline-hashes.txt`.
- **Failure**: any success clause unmet. The result is reported as an honest null. Do not re-run the holdout with a different metric to rescue it; a redesign must go back to dev seeds and pre-register a new holdout.

## Multiple-comparison and p-hacking controls

- Only the primary metric gates the claim; secondary metrics are descriptive.
- Dev seeds may be used to tune the policy; holdout seeds are used once for the final claim.
- The probe writes raw per-iteration rows before any aggregation; the aggregation script reads those rows and cannot edit them.
- The probe never writes `active-champion.json` or a meta pointer outside its own scratch data directories.

## Artifacts

- Raw rows: `.tmp/goal-refinement/experiment/<split>/<arm>/seed-<n>/...`
- Experiment summary: `docs/goal-tholdem-harness/round-<N>/experiment.json` and `experiment.md`
- Probe source: `.tmp/goal-refinement/Probe/` (not added to `Wip.slnx`)
