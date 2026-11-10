# Harness State — tholdem-positive-evidence-floor

- **Round**: 3 of 6 (complete; converged)
- **Goal**: Evaluate a positive-evidence signal floor in the Tholdem refinement policy on DEV, then once on a fresh holdout; adopt a floor only if the pre-registered rule supports it, otherwise keep 0.
- **Definition of done**: **5/5 Covered**
- **Stop criteria**: met — zero Falsified delivery claims, zero P0 OQ; the floor is rejected as inert and the shipped 0 remains

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001 to EVD-013 resolve to raw rows, tests or reproduced commands |
| G2 Falsification | PASS | 0 Falsified delivery claims; the no-op result falsifies the hypothesized benefit of a positive floor on the tested space |
| G3 Coverage | PASS | F1–F5 Covered |
| G4 Reproduction | PASS | 394/394 full suite; 90 DEV rows and 90 holdout rows validated; selection and holdout aggregates reproduced |
| G5 Honesty | PASS | The floor is retained at 0; the inert result and the untested end-to-end zero-rate rescue are recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract and pre-registration | Probe | 0 | 1 | 0 | — | complete |
| 1 | F1 default-0 signal floor | Implement + verification | 1 | 4 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | accepted |
| 2 | DEV floor arms | Probe + verification | 1 | 4 | 0 | G1✓ G2✓ G3~ G4✓ G5✓ | floors inert; 0.05 mechanically chosen |
| 3 | Fresh holdout + decision | Probe + verification | 1 | 4 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | 0.05 inert; **retain 0** |

## Finding

The floor is behaviorally inert on the tested space:

- All three DEV positive floors were byte-identical to each other and field-identical to floor 0 on 30 refinement rows.
- Floor 0.05 was field-identical to floor 0 on the 30 fresh-holdout refinement rows.
- The 0.05 arm still passes the pre-registered arm-vs-baseline rule (median 4.91x, CI [3.92, 16], zero-effect 7/30 → 0/30), but that gain belongs to the existing signature arm, not the floor.
- Because the floor removed no observed edit, the no-op adoption rule retains **0**. The default-0 option stays in the product with its unit tests for a future adversarial zero-rate fixture.

## Next objective

None. The goal is complete; the final report is `docs/goal-tholdem-floor/final-report.md`.
