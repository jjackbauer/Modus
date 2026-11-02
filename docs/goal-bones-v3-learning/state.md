# Harness State — bones-v3-learning

- **Round**: 3 of 4 (loop stopped with one concrete blocker)
- **Goal**: Verify or correct every v3 safety/measurement claim; refuse unbounded runs; bound infinite-loop scripts; green or concretely block the remaining compliance gates; reproduce the benchmark.
- **Definition of done**: 5/6 met (D1, D2, D3, D5, D6); D4 blocked with named evidence
- **Stop criteria**: all six met, or three rounds with concrete OQ/RISK for each unmet dimension, or same blocker three rounds. Met: three rounds, D4 blocker named (OQ-004/OQ-005).

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001..EVD-017; every round claim resolves to a file/command |
| G2 Falsification | PASS | Round 1 falsified 3 claims; rounds 2-3 corrected claims supported 6/6 and 5/5, 0 falsified in final tables |
| G3 Coverage | **BLOCKED** | D4 (behavior-proof gates) blocked by stale host promotion/restart fixtures; not a code safety defect |
| G4 Reproduction | PASS | Arena, gate, unbounded refusal, direct timeout, host cap all rerun by orchestrator |
| G5 Honesty | PASS | Agent timeouts (RISK-004, RISK-006), flaky test (RISK-002), abandoned thread (RISK-005), stale tests (OQ-003/4/5) recorded |

## Coverage

| # | Dimension | Status | Evidence | Gap |
|---|---|---|---|---|
| D1 | Benchmark claims reproduce | Met | EVD-004, EVD-009, EVD-010 | — |
| D2 | Unbounded runs refused | Met | EVD-001, EVD-007, EVD-008 | — |
| D3 | Infinite-loop script bounded | Met (residual risk) | EVD-013..EVD-017 | Timed-out thread not reclaimed; process isolation is M2 |
| D4 | Behavior-proof gates green or concrete blocker | **Blocked** | final gates: script-strategies fails host promotion tests; reliability fails stale Markdown mocks | OQ-004, OQ-005 |
| D5 | No regression | Met | focused host 126/126; Bones 371/373 (2 known gates); builds 0 errors | Flaky sub-agent timeout test under load |
| D6 | Docs truthful | Met | v3 plan, host README, requirements addendum updated | D4 blocker documented |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 1 | Falsify v3 safety/measurement/reproducibility claims | Adversarial | 2 falsifiers + synthesizer | 6 | 3 (1,2,5) | G1✓ G2✗ G3✗ G4✓ G5✓ | re-dispatch |
| 2 | Fix unbounded refusal + split gate arena | Implementer + orchestrator | 1 implementer | 6 | 0 corrected | G1✓ G2✓ G3✗ G4✓ G5✓ | proceed |
| 3 | Bound direct script execution | Implementer + orchestrator | 1 implementer | 5 | 0 corrected | G1✓ G2✓ G3✗ G4✓ G5✓ | stop with D4 blocker |

## Next objective

D4 blocker: migrate `BonesLearningLoopPromotionHostTests` and the reliability/restart host mocks from Markdown/stale stage predicates to script responses, then rerun the two compliance gates. Process isolation (M2) remains the next safety increment after that.
