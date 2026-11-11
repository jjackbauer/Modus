# Proposal comparison v2 — signature-guarded step vs controls (pre-registration)

**Registered before the run.**

- Tasks: four starting genomes (default, tight, loose, aggressive).
- Arms, 8 chained proposals per task: `guarded` (40-hand capture -> signature-guarded 0.02 step),
  `deterministic` (0.02 step), `random` (fixed LCG), `grid` (8 spread genomes), `nosearch` (start).
- Evaluation per (task, candidate index) uses the same plan seed across arms: holdout 8 blocks x 2
  hands, anchor `three-tag`, margin 5, confidence 0.95.
- Outcome rule: `benefit` only if the guarded arm has a verified promotion on >=2 tasks, zero
  `Unsafe` verdicts, and strictly more verified promotions than **each** control arm. Otherwise
  `no-benefit`.
- This tests a safer proposal mechanism, not a new policy family; no claim beyond the rule.
