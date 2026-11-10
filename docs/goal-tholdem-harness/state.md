# Harness State — tholdem-continual-harness

- **Round**: 9 of 10 (complete; converged)
- **Goal**: Add an opt-in, data-only harness-refinement path to Tholdem that is byte-identical when disabled, leaves the frozen gate untouched, and measurably increases proposal exploration in a pre-registered experiment.
- **Definition of done**: **9/9 Covered**
- **Stop criteria**: met — zero Falsified delivery claims, zero P0 OQ, all dimensions Covered; P1 residuals recorded (OQ-001, OQ-004, OQ-007)

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001 to EVD-063 resolve to files, tests or reproduced commands |
| G2 Falsification | PASS | 0 Falsified delivery claims; HYP-003 is the recorded finding, OQ-008/RISK-006 resolved |
| G3 Coverage | PASS | D1–D9 Covered at High confidence |
| G4 Reproduction | PASS | 387/387 full suite, legacy 7/7, holdout aggregate recomputed, meta probe bytes identical |
| G5 Honesty | PASS | Residuals, the authorized test scope correction, the lineage deviation and the no-CI claim are recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract, baseline, goal files | Probe | 0 | 3 | 0 | — | complete |
| 1 | R-01/R-02/R-05 vertical slice | Implement + verification | 1 | 12 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 2 | DEV experiment + step-size control | Probe + control + verification | 2 | 7 | 1 (HYP-003) | G1✓ G2✓ G3~ G4✓ G5✓ | dev accepted |
| 3 | HOLDOUT one-shot | Probe + verification | 1 | 4 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | success rule met |
| 4 | R-03 memory / R-04 lineage | Implement + verification | 1 | 8 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 5 | R-06 bootstrap / R-08 safety | Implement + verification | 1 | 5 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 6 | R-09/R-10 skills | Implement + verification | 1 | 8 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 7 | R-11 ablation | Implement + verification | 1 | 6 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 8 | R-12 refiner evolution | Implement + verification | 1 | 6 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 9 | Meta-document path fix | Remediation + verification | 1 | 3 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | accepted; **converged** |

## Coverage

| Dimension | Status | Evidence |
|---|---|---|
| D1 Legacy equivalence | Covered | EVD-009, EVD-010, EVD-011, EVD-031, EVD-063 |
| D2 Trajectory capture and signatures | Covered | EVD-005, EVD-006, EVD-015, EVD-016 |
| D3 Memory and lineage | Covered | EVD-027, EVD-028, EVD-029, EVD-032 |
| D4 Policy and bootstrap | Covered | EVD-014, EVD-035, EVD-038 |
| D5 Guards and safety | Covered | EVD-036, EVD-046 |
| D6 Skills | Covered | EVD-040, EVD-042, EVD-043, EVD-044, EVD-046 |
| D7 Ablation | Covered | EVD-048, EVD-050, EVD-051, EVD-052 |
| D8 Refiner evolution | Covered | EVD-054, EVD-056, EVD-058 |
| D9 Significant experiment | Covered | EVD-023 to EVD-026, EVD-060 to EVD-062 |

## Final claim

The refinement arm more than quintuples decision divergence and removes the zero-effect evaluations observed in the shipped loop on untouched holdout seeds (median 6.53×, CI [4.53, 11.5], zero-effect 9/30 → 0/30), while the five frozen gate components, the legacy artifacts and the default meta bytes are unchanged. Most raw divergence is step size; the signature layer's distinct demonstrated benefit is the zero-effect rescue (7/7 on the exact dev iterations where the baseline and the large-step control both changed nothing). No poker-strength, CI-green, publish or deployment claim is made.

## Next objective

None. The goal is complete; the final report is `docs/goal-tholdem-harness/final-report.md`.
