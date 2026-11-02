# Harness State — bones-fair-block

- **Round**: 3 of 4 (complete)
- **Goal**: Produce and stage a fair public-information blocker that beats c5-newend.
- **Definition of done**: 6/6 met
- **Stop criteria**: met

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001..008 resolve selection, confirmation, fairness, persistence, and safety |
| G2 Falsification | PASS | HYP-001 supported; oracle diagnostic recorded; no falsified rows |
| G3 Coverage | PASS | All six dimensions met |
| G4 Reproduction | PASS | Both 200k runs, reload, and repeat reproduced |
| G5 Honesty | PASS | Oracle labeled diagnostic; live data untouched; approximate model risk recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract goal and identify the public-information blocker | — | 0 | 0 | 0 | — | complete |
| 1 | Fair fixed-field selection: fair-block vs c5-newend | Experiment | 0 | 1 | 0 | all ✓ | complete |
| 2 | Independent 200k confirmation | Experiment | 0 | 2 | 0 | all ✓ | complete |
| 3 | Fairness, persist, reload, safety | Experiment | 0 | 5 | 0 | all ✓ | complete |

## Coverage

| Dimension | Status | Evidence |
|---|---|---|
| D1 Fair selection | Met | EVD-001 |
| D2 Independent confirmation | Met | EVD-002 |
| D3 Combined evidence | Met | EVD-003 |
| D4 Fairness | Met | EVD-004 |
| D5 Persist and reload | Met | EVD-005 |
| D6 Data safety | Met | EVD-006, EVD-007 |

## Final result

The new champion is `fair-block`:

- Base: `c5-newend` (calm weights + corrected new-end follow-on counter)
- Added: a public-information `NextMatchProbability` term that estimates whether the next player can answer either new end, using own hand, board length, public event log, and inferred next-hand size
- Fair selection: 59,107/200,000 = 29.553% vs c5 29.075% (+0.478pp, CI [0.196, 0.761])
- Independent confirmation: 58,862/200,000 = 29.431% vs c5 29.030% (+0.401pp, CI [0.119, 0.683])
- Combined: 117,969/400,000 = 29.4923% vs c5 29.0525% (+0.4398pp, CI [0.2403, 0.6392])
- Against v9 on the same 400k: +1.0265pp, CI [0.8277, 1.2253]

Staged at `.tmp/block-search/library-confirm`; live `bones-data` is unchanged.

## Next objective

If requested, stage-apply `fair-block` to live data with a backup; otherwise the goal is complete.