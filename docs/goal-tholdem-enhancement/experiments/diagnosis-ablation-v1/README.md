# Diagnosis-driven vs deterministic equal-budget ablation (v1)

**Status:** completed; outcome **no-benefit**. Not an anchor-3 success.

- Pre-registration: `preregistration.md`, SHA-256 `fa312c5df96e8d329da5235a54303f79059acb551446b645d274ba351fac09f5`.
- Four starting genomes (default, tight, loose, aggressive), 4 iterations per arm, selection 8x2,
  holdout 8x2, margin 5, seed 0x5EED2026; diagnosis arm captures 40 hands per iteration.
- Deterministic: **0/16** promotions, 4 Unsafe verdicts.
- Diagnosis: **0/16** promotions, 7 Unsafe verdicts.
- Outcome rule required >=2 tasks with a verified promotion, 0 Unsafe, and strictly more promotions;
  the diagnosis arm fails all three.

Files: `preregistration.md`, `results/report.json`, `run.log`.

**Interpretation:** the diagnosis-driven proposer is not better than the deterministic proposer on
this pre-registered budget, and it is less safe at the anchor. Anchor 3 remains null.
