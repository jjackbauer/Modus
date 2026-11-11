# Diagnosis ablation v2r — pre-registered replication (no-benefit)

- Pre-registration: `preregistration.md`, SHA-256 `15d2945bdd6117041aed2a454e18db0d10f79c8af92bb43151febc067ed694e7`.
- 8 tasks x 6 iterations x 2 arms; selection 8x2, holdout 8x2, margin 5, seed 0x5EED2026.
- Deterministic: **0/48** promotions, 7 Unsafe.
- Diagnosis: **0/48** promotions, 20 Unsafe.
- Outcome `no-benefit` (rule required >=4 tasks with a verified promotion, zero Unsafe, and strictly
  more promotions).

Files: `preregistration.md`, `results/report.json`, `run.log`.

**Interpretation:** clean pre-registered replication; the diagnosis proposer is no better and less
safe. Anchor 3 remains null.
