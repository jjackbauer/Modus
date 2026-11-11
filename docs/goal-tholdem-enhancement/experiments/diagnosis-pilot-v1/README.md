# Diagnosis-driven vs deterministic four-seat pilot (v1)

**Status:** pilot only, **not** an equal-budget controlled experiment and not an anchor-3 result.

- Seed 1099511627776; 6 iterations; selection 8 blocks x 4 hands; holdout 8 blocks x 4 hands;
  margin 5 bb/100; selection pool; composition primary-mixed; default genome start.
- Deterministic arm: 6 iterations, **0 promotions**; holdout rejections include no-improvement
  (iteration 4, selection mean +9.06).
- Diagnosis arm (--diagnosis --diagnosis-hands 20): 6 iterations, **0 promotions**; iteration 6
  reached holdout with mean +45.94 but was **Unsafe / unsafe-anchor-regression**.
- Both runs left the champion unchanged and wrote no v1 pointer.

Logs: off.log, on.log.

**Interpretation:** the diagnosis path is wired and runs, but this short pilot produced no verified
promotion in either arm. It is not evidence for or against the diagnosis proposer; a controlled,
pre-registered comparison with more iterations and starting strategies is required.
