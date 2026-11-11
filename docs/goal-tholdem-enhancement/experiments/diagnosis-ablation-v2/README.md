# Diagnosis-driven vs deterministic equal-budget ablation v2 (replication)

**Status:** exploratory run; outcome **no-benefit**. This run is **not** pre-registered: the
pre-registration file was missing at run time. See `../diagnosis-ablation-v2r/` for the clean
pre-registered replication.
- 8 tasks x 6 iterations x 2 arms; selection 8x2, holdout 8x2, margin 5, seed 0x5EED2026.
- Deterministic: **0/48** promotions, 7 Unsafe.
- Diagnosis: **0/48** promotions, 20 Unsafe.
- The rule required >=4 tasks with a verified promotion, zero Unsafe, and strictly more promotions;
  it fails all three.

Files: `preregistration.md`, `results/report.json`, `run.log`.

**Interpretation:** the diagnosis proposal process is not better than the deterministic one and is
less safe on this budget. Anchor 3 remains null.
