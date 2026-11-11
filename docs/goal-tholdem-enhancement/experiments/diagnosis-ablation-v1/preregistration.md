# Diagnosis-driven vs deterministic equal-budget ablation (pre-registration)

**Registered before the run.**

- Tasks: four starting genomes — default, tight (0.85/0.35/0.05/1.20), loose (0.55/0.65/0.25/0.80),
  aggressive (0.65/0.50/0.30/1.50).
- Arms: deterministic proposer vs diagnosis-driven proposer (capture 40 hands, then the signature
  policy). Each arm: 4 iterations, selection 8 blocks x 2 hands, holdout 8 blocks x 2 hands, margin
  5 bb/100, seed 0x5EED2026.
- Equal budget: same iterations, plans, seed and starting genome per task; the diagnosis arm spends
  one 40-hand capture per iteration in addition.
- Outcome rule: `benefit` only if the diagnosis arm has a verified promotion on at least 2 of the 4
  tasks, zero `Unsafe` verdicts, and strictly more promotions in total than the deterministic arm.
  Otherwise `no-benefit`.
- No claim beyond this rule; anchor 3 remains null unless a later pre-registered replication agrees.
