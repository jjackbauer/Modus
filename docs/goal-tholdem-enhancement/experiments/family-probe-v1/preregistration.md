# Policy-family feasibility probe (pre-registration)

**Registered before the run.**

Question: does the four-threshold strategy family contain **any** genome that clears the gate against
the default genome on the holdout pool, or is the family itself the learning bottleneck?

- Grid: raiseThreshold {0.40, 0.60, 0.80, 0.95} x callThreshold {0.20, 0.40, 0.60, 0.80} x
  bluffFrequency {0.0, 0.2, 0.4} x sizingMultiplier {0.75, 1.25, 1.75} = **144 candidates**.
- Incumbent: default genome. Plan: selection pool, `primary-mixed`, holdout 8 blocks x 2 hands,
  margin 5 bb/100, confidence 0.95, one candidate; anchor `three-tag` on the same holdout domain.
- Every candidate is evaluated identically; a candidate is `verified` only on
  `VerifiedImprovement` from `PopulationPromotionGate`.
- Outcome rule: report the verified count and the best lower bound. **Any** verified candidate proves
  the family can represent a gate-passing improvement on this plan; **zero** is evidence (not proof)
  that this family/plan cannot, which shifts the next step to a richer policy family.
- No learning or strategic claim; this is a feasibility probe.
