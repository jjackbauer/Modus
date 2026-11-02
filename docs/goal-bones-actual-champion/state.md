# Harness State — bones-actual-champion

- **Round**: 3 of 4 (complete)
- **Goal**: Produce and stage a statistically validated champion.
- **Definition of done**: 6/6 met
- **Stop criteria**: met

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001..008 resolve selection, confirmation, persistence, and safety |
| G2 Falsification | PASS | HYP-001 supported; no falsified rows |
| G3 Coverage | PASS | All six dimensions met |
| G4 Reproduction | PASS | Both fair runs and the reload reproduced |
| G5 Honesty | PASS | Live data untouched; champion labeled staged; aggressive direction reported as falsified |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract goal and fix the fair-field protocol | — | 0 | 0 | 0 | — | complete |
| 1 | Fair fixed-field selection over 5 candidates, 200k games each | Experiment | 0 | 1 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |
| 2 | Independent 200k fair confirmation | Experiment | 0 | 2 | 0 | all ✓ | complete |
| 3 | Persist, reload, safety, final verdict | Experiment | 0 | 5 | 0 | all ✓ | complete |

## Coverage

| Dimension | Status | Evidence |
|---|---|---|
| D1 Fair selection | Met | EVD-001 |
| D2 Independent confirmation | Met | EVD-002 |
| D3 Combined evidence | Met | EVD-003 |
| D4 Persist and reload | Met | EVD-004, EVD-005 |
| D5 Data safety | Met | EVD-006 |
| D6 Determinism | Met | EVD-007 |

## Final result

The actual champion is `c5-newend`:

- c5 weights: pips 2, double 50, noFollow 140, end 6, chain 30, endgame 1
- corrected `CountMatchingAfterPlay`: counts follow-ons against the **new** ends, not the stale ends
- Fair selection: 58,150/200,000 = 29.075% vs v9 28.512% (+0.563pp, CI [0.282, 0.844])
- Independent confirmation: 58,060/200,000 = 29.030% vs v9 28.419% (+0.611pp, CI [0.330, 0.891])
- Combined: 116,210/400,000 = 29.0525% vs v9 28.4658% (+0.5867pp, CI [0.388, 0.785])

It is staged at `.tmp/aggression-search/library-fair`; live `bones-data` is unchanged.

## Next objective

If requested, apply the staged champion to live `bones-data` with a backup; otherwise the goal is complete.