# Proposal comparison pre-registration (LLM vs non-LLM controls)

**Registered before the run.**

- Tasks: the same four starting genomes as the LLM pilot (default, tight, loose, aggressive).
- Arms, 4 candidates per task: `llm` (the 16 stored proposals), `random` (deterministic LCG genome),
  `grid` (four fixed parameter points), `greedy` (four deterministic proposer steps), `nosearch`
  (the start genome four times).
- Equal candidate budget per task: 4. The LLM arm makes no additional model calls; the 16 stored
  proposals are reused.
- Evaluation: holdout 4 blocks x 2 hands per seat match, anchor composition `three-tag`, margin
  5 bb/100, confidence 0.95, one candidate; the same plan seed per (task, candidate index) across
  arms, so every arm faces identical cards and opponents.
- Outcome rule: `benefit` only if the LLM arm has a verified promotion on >=2 tasks, zero `Unsafe`
  verdicts, and strictly more verified promotions than **each** control arm. Otherwise `no-benefit`.
- No claim beyond this rule.
