# Harness State — bones-aggression-search

- **Round**: 4 of 4 (complete)
- **Goal**: Find a statistically supported more aggressive v9 variant, or prove the grid yields none.
- **Definition of done**: 6/6 dimensions covered with a negative capability verdict
- **Stop criteria**: D1-D6 met; final 96,000-game confirmation did not clear zero

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001..010 resolve the harness, selection, validation, persistence, and safety claims |
| G2 Falsification | PASS | HYP-001 Falsified; 0 unresolved falsified rows |
| G3 Coverage | PASS | All six dimensions covered; c5 labeled candidate, not promoted |
| G4 Reproduction | PASS | Smoke, selection, three validation sets, persistence, and hashes reproduced |
| G5 Honesty | PASS | Borderline result, failed 95% bar, and post-hoc limitations recorded (RISK-003, OQ-002) |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract goal and create harness memory | — | 0 | 0 | 0 | — | complete |
| 1 | Build aggression search harness and smoke-test | Implementer→orchestrator | 1 | 1 | 0 | G1✓ G2✓ G3– G4✓ G5✓ | complete |
| 2 | Run aggressive grid and control grid | Experiment | 0 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |
| 3 | Validate top variants on disjoint seeds | Experiment | 0 | 2 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |
| 4 | Final 96k confirmation and verdict | Experiment | 0 | 6 | 1 (HYP-001) | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |

## Coverage

| Dimension | Status | Evidence |
|---|---|---|
| D1 Search harness | Met | EVD-001 |
| D2 Selection grid | Met | EVD-002; 12 levels incl. aggressive and control |
| D3 Held-out validation | Met | EVD-003..007; 8k, 24k, 8k, 24k, 96k |
| D4 Capability claim | Met (negative) | EVD-007; final lower bound −0.001; no supported improvement |
| D5 Persist and reload | Met (candidate) | EVD-009; c5 in temp library, not live |
| D6 Determinism and safety | Met | EVD-008, EVD-010 |

## Final result

More aggression is monotonically worse. The best discovered player is a **calmer** variant, `control-c5` (pips 2, double 50, noFollow 140, end 6, chain 30, endgame 1). It is consistently +0.4 to +0.9 points over v9, but the pre-registered 96,000-game confirmation was 27,987/96,000 (29.153%) vs v9 27,598/96,000 (28.748%), diff +0.405pp, 95% CI [−0.001, 0.811]. That narrowly misses the 95% bar. The honest verdict: promising candidate, not a proven replacement; v9 remains champion.

## Next objective

If the user wants to continue: run a pre-registered ~200,000-game confirmation for c5, or a local search around c5 for a larger effect. Do not promote on the current evidence.