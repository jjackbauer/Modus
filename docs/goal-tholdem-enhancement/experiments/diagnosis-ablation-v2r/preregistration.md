# Diagnosis-driven vs deterministic equal-budget ablation v2r (pre-registration)

**Registered before the v2r run.** This is a clean pre-registered replication of the exploratory v2
design; v2 itself was specified in-session but its preregistration file was missing at run time, so
v2 is reported as exploratory.

- Tasks: default, tight (0.85/0.35/0.05/1.20), loose (0.55/0.65/0.25/0.80), aggressive
  (0.65/0.50/0.30/1.50), rock (0.90/0.20/0.02/1.00), station (0.50/0.80/0.40/0.70), lag
  (0.75/0.55/0.15/1.80), maniac (0.95/0.45/0.05/2.50).
- Arms: deterministic vs diagnosis (40-hand capture per iteration). 8 tasks x 2 arms x 6 iterations;
  selection 8x2, holdout 8x2, margin 5, seed 0x5EED2026.
- Outcome rule: `benefit` only if the diagnosis arm has a verified promotion on at least 4 of 8 tasks,
  zero `Unsafe` verdicts, and strictly more promotions in total than deterministic. Otherwise
  `no-benefit`.
- No claim beyond the rule.
