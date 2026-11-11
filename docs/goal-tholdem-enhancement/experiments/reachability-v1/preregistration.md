# Target-reachability test (pre-registration)

**Registered before the run.** The family probe found one gate-passing genome: raise 0.95, call 0.20,
bluff 0.40, sizing 0.75 (+150.625 mean, +71.452 lower). This test asks whether the diagnosis proposer
can reach a verified proposal from the default genome at a small equal budget, and how controls do.

- Start: default genome. Fixed incumbent for evaluation: the default genome (static test).
- Arms, **8 chained proposals each**: `diagnosis` (40-hand four-seat capture -> signature policy),
  `deterministic` (0.02 step), `random` (fixed LCG), `grid` (8 spread genomes, target excluded),
  `nosearch` (default repeated).
- Evaluation per proposal index uses the same plan seed across arms: holdout 8 blocks x 2 hands,
  anchor `three-tag`, margin 5, confidence 0.95.
- Outcome rule: `reached` if any diagnosis proposal is `VerifiedImprovement`; report each arm count.
  This is a reachability pilot, not an attribution claim. The target is known from the prior probe, so
  it is not a blind discovery test.
