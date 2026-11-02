# Harness State — bones-fair-block-v3

- **Round**: 8 of 8 (converged — bounded negative)
- **Goal**: Beat `v2-cur2` by ≥ +0.50pp with a fair strategy on a discovery-tuned selection set, an independent confirmation set and a 400k final, and report a fair information ceiling.
- **Definition of done**: 7/10 dimensions met (D1–D3 unmet by outcome, not by omission)
- **Stop criteria**: *bounded negative* reached — all six approaches carry a verdict, the field-transfer and ceiling gates are reported, and the surviving variant has a final 400,000-game CI against the champion.

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001…018 each resolve to a file, a SHA256, or a reproduced command output |
| G2 Falsification | PASS | HYP-001…006 all Falsified; zero outstanding. 0 Supported claims carried a `❓` |
| G3 Coverage | PASS | A1–A6 each measured; D7 ceiling and D8 transfer both reported |
| G4 Reproduction | PASS | `champ-clone` rebuilds `v2-cur2` byte-identically; `repro-next80` == `fair-block` bit-exactly; the champion's fresh-block numbers were each produced by a single command and re-read from the result JSON |
| G5 Honesty | PASS | One invalid A4 implementation detected and re-run (RISK-002); the session reports a negative, not a promoted noise-level variant; OQ-001…003 recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 1 | Stand up `.tmp/fair-block-v3/`, re-verify reproduction, measure the incumbent on the discovery block | Reproduction | 0 | 3 | 0 | D0 PASS | complete — champion 30.5430% on fresh seeds |
| 2 | Build the fair information ceiling and run it before the improvement work | Probe | 0 | 2 | 1 (HYP-005) | D7 PASS | complete — information worth ≤ 0 |
| 3 | Field-transfer split A/B against the champion | Probe | 0 | 2 | 1 (HYP-006) | D8 PASS | complete — edge holds on both halves |
| 4 | A1 residual search: one-at-a-time then 110 joint random perturbations | Adversarial | 0 | 2 | 1 (HYP-001) | discovery-only | complete — local optimum confirmed |
| 5 | A2 fair Monte-Carlo planner: build, measure throughput, compare | Adversarial | 0 | 1 | 1 (HYP-002) | throughput measured | complete — ~10pp worse |
| 6 | A3 exact tempo signal; A4 near-tie band | Adversarial | 0 | 2 | 2 (HYP-003, HYP-004) | discovery-only | complete — both rejected |
| 7 | Freeze; measure the champion on fresh selection / confirmation / final blocks | Reproduction | 0 | 3 | 0 | D1–D3 not applicable | complete — +1.3250 / +1.3210 / +1.1895pp |
| 8 | Persist, reload, fairness, safety, harness memory | Reproduction | 0 | 3 | 0 | D4–D6 PASS | complete |

## Coverage against the definition of done

| # | Dimension | Status | Evidence |
|---|---|---|---|
| D0 | Reproduction | **Met** | EVD-001, EVD-002 |
| D1 | Variant ≥ +0.50pp over `v2-cur2` on selection | **Not met** — best variant +0.0475pp | EVD-011 |
| D2 | Confirmation CI low > 0 vs `v2-cur2` | **Not met** — nothing to confirm | VAL-001…004 |
| D3 | Final CI low > 0 vs `v2-cur2` | **Not met** — nothing to confirm | VAL-001…004 |
| D4 | Persist + reload | **Met** (on the incumbent, for chain continuity) | EVD-016 |
| D5 | Fairness | **Met** | EVD-018 |
| D6 | Safety | **Met** | EVD-017 |
| D7 | Fair information ceiling with CI | **Met** | EVD-004, EVD-005 |
| D8 | Field transfer on a held-out half | **Met** | EVD-006, EVD-007 |
| D9 | Exhaustion of the six approaches | **Met** | table below |

## Approach verdicts

| # | Approach | Best variant | Result | Δ vs `v2-cur2` | Verdict |
|---|---|---|---|---|---|
| A1 | Residual search (one-at-a-time + 110 joint perturbations) | `hold=6` / `R50` | 24,492 / 24,511 of 80,000 | +0.055 / +0.079pp | **Falsified** — local optimum |
| A2 | Fair Monte-Carlo planner | `planner-mc16` | 20.8775% of 80,000 | **−9.68pp** | **Falsified** — greedy playout too weak; champion-strength playout out of budget |
| A3 | Exact expected-free-turns tempo signal | `freeturns=80` | 24,486 of 80,000 | +0.0475pp | **Falsified** — already encoded in the block term |
| A4 | Near-tie band mixing (δ = 8/20/40) | δ = 8 | 24,362 of 80,000 | −0.1075pp | **Falsified** — monotone degradation |
| A5 | Fair information ceiling | `oracle-truth-dist` | 30.2475% of 200,000 | **−0.2955pp** | **Negative gap** — information is worth ≤ 0 |
| A6 | Field transfer | `v2-cur2` halves A/B | +1.1840 / +1.3455pp | n/a | **Supported** — the edge is not field-specific |

## Champion

**Unchanged: `v2-cur2`.** 21,497 characters, SHA256 `0CB1F7DC390AD77092FFFF87881BB7E680DB5695C844B877A66DB8EC2A83E539`, staged at `.tmp/fair-block-v3/library` and reload-verified.

Fresh-block replication of its edge over `fair-block` (six independent blocks across two sessions):

| Block | Games | Champion | `fair-block` | Δ | 95% CI |
|---|---|---|---|---|---|
| Discovery 910535–910634 | 200k | 30.5430% | 29.4495% | +1.0935pp | [0.8095, 1.3775] |
| Field half A | 200k | 31.3330% | 30.1490% | +1.1840pp | [0.8980, 1.4700] |
| Field half B | 200k | 31.3580% | 30.0125% | +1.3455pp | [1.0597, 1.6313] |
| Selection 910635–910734 | 200k | 30.7520% | 29.4270% | +1.3250pp | [1.0408, 1.6092] |
| Confirmation 910735–910834 | 200k | 30.6340% | 29.3130% | +1.3210pp | [1.0371, 1.6049] |
| Final 910835–910934 | 400k | 30.7165% | 29.5270% | +1.1895pp | [0.9884, 1.3906] |

## Next objective

None — converged. The only open improvement question is OQ-003 (a champion-strength playout for the planner), which is a cost problem rather than a design problem.
