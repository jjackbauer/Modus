# Harness State — bones-fair-block-v2

- **Round**: 6 of 8 (converged; stop criterion *champion confirmed* met)
- **Goal**: Beat `fair-block` by ≥0.5pp with a fair public-information strategy, confirmed on three disjoint seed sets, or deliver a bounded negative result.
- **Definition of done**: 6/6 dimensions met
- **Stop criteria**: champion confirmed on selection + confirmation + final 400k with lower bound > 0 — **met**

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001..019 each resolve to a file, a line range, or a reproduced command output |
| G2 Falsification | PASS | HYP-002/003/004/005 Falsified; 0 falsified rows outstanding. HYP-001 Supported. No claim carried a `❓` into a verdict |
| G3 Coverage | PASS | D1–D4 met by direct measurement; D5 met by fairness scan; D6 met — all six planned approaches have a 200,000-game verdict |
| G4 Reproduction | PASS | `repro-next80` == `fair-block` bit-exactly; `RunGameWon` == Arena on `fair-block`; the final 400,000-game run reproduced 123,068 twice; `c5-newend` re-measured at 29.066% against 29.0525% on record |
| G5 Honesty | PASS | OQ-001..003 recorded; RISK-001 (selection overfitting) recorded with the three independent estimates that bound it; the oracle diagnostics stayed diagnostics |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 1 | Build a reproducible harness under `.tmp/fair-block-v2/`, reproduce the incumbent on the new selection seeds, and quantify where the headroom is | Probe (orchestrator-built harness; subagent dispatch deferred until the harness could produce a number) | 0 | 6 | 0 | all PASS | complete — 168-pip fix, exact-count audit, round diagnostics |
| 2 | Test approach 1 (multi-opponent model) and approach 2 (exact public counting) on the selection set | Adversarial (generator vs incumbent, paired) | 0 | 2 | 1 (HYP-002) | all PASS | complete — rejected as a standalone lever at ≈+0.2pp |
| 3 | Explain why approaches 1–2 plateau and find a lever of the required size | Adversarial (paired coordinate sweep over 21 base weights) | 0 | 1 | 0 | all PASS | complete — base mis-weighting found; super-additive interaction identified |
| 4 | Freeze the best variant and run selection / confirmation / final | Reproduction | 0 | 3 | 0 | all PASS | complete — +1.2640 / +1.2365 / +1.2267pp |
| 5 | Test approaches 3, 4, 5, 6 on the selection set | Adversarial (200,000 games each) | 0 | 4 | 3 (HYP-003/004/005) | all PASS | complete — all rejected |
| 6 | Persist the champion, verify fairness, verify live-data safety, write the harness memory | Reproduction | 0 | 4 | 0 | all PASS | complete |

## Coverage

| Dimension | Status | Evidence |
|---|---|---|
| D1 Selection ≥ +0.5pp with CI low > 0 | Met | EVD-006 (+1.2640pp, [0.9795, 1.5485]) |
| D2 Confirmation CI low > 0 on disjoint seeds | Met | EVD-007 (+1.2365pp, [0.9521, 1.5209]) |
| D3 Final 400k head-to-head CI low > 0 | Met | EVD-008 (+1.2267pp, [1.0256, 1.4279]) |
| D4 Persist + reload | Met | EVD-010 (persisted=True, matchesExactly=True, SHA256 identical) |
| D5 Fairness (no hidden hands) | Met | EVD-011 (one `GetHand`, own seat only) |
| D6 Exhaustion of the six planned approaches | Met | EVD-005, EVD-013, EVD-014, EVD-015, EVD-016, EVD-017 |

## Approach verdicts (all at 200,000 selection games unless stated)

| # | Approach | Variant measured | Wins / 200,000 | Rate | Δ vs `fair-block` | Verdict |
|---|---|---|---|---|---|---|
| 1 | Multi-opponent blocking model | three-opponent graded term, weights 1.0/0.5/0.25 | 59,357 | 29.6785% | +0.1420pp [−0.1410, 0.4250] | Retained inside the champion, insufficient alone |
| 2 | Exact public tile-count probability | exact count with connector and equal-end collapse | see #1 (same arm) | — | — | Retained; the arithmetic fix alone is worth ≈0 |
| 3 | Seat-aware policy | block module scaled 70/130 … 150/50 | 61,382–61,561 | 30.69–30.78% | −0.02 to −0.11pp vs champion | Rejected |
| 4 | Endgame block/race planner | immediate-block gate; budgeted silent-chain search | 61,601 / 61,620 | 30.80 / 30.81% | 0.0000 / +0.0095pp | Rejected (gate is a strict no-op; chain is noise) |
| 5 | Public opponent-profile memory (voids) | pass-derived void-blended answer probabilities, 50% and 100% | 61,314 / 61,496 | 30.66 / 30.75% | −0.14 / −0.04pp | Rejected |
| 6 | Deterministic tie-break audit | last-legal-wins on score ties | 61,522 | 30.7610% | −0.0395pp | Rejected (not exploitable in this direction) |

## Champion

`v2-cur2` — 21,497 characters, fair, deterministic.

- Base `c5-newend` with six weight corrections, plus a three-opponent exact-count graded block term (weight 120, opponent weights 1.0 / 0.5 / 0.25).
- Source: `.tmp/fair-block-v2/policies/candidates/v2-cur2.cs`; staged library: `.tmp/fair-block-v2/library`.
- SHA256 `0CB1F7DC390AD77092FFFF87881BB7E680DB5695C844B877A66DB8EC2A83E539`.

## Remaining work

- OQ-001 (transfer to a different opponent field) is untested and needs a fresh benchmark bank.
- OQ-002 and OQ-003 are explanatory, not blocking.
