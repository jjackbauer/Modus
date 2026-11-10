# Harness State — goal-llm-harness

- **Round**: 11 complete — **goal complete**
- **Goal**: A real DeepSeek model drives Tholdem refinement through the unchanged frozen gate with a canonical transcript, the existing `TholdemRuntimeAgent` plays a live 20-hand Ponder match with its four tools, and every deterministic artifact stays byte-identical.
- **Definition of done**: **9/9 Covered** (D6 resolved by the authorized parallel round)
- **Suite**: **446 tests — 446 passed, 0 failed, 0 skipped**
- **Stop criteria**: all met — D1–D9 Covered, 22/22 planned tests green, live LLM and Ponder runs recorded, deterministic/legacy/frozen unchanged, 0 falsified delivery claims, 0 P0 OQ, no key leak
- **D6 evidence**: the authorized `both-required` variant made **54 live tool calls** (equity-calculator 44, pot-odds 10) with 54 recorded results and 61 legal actions; `BothRequired` is the default tool-prompt mode

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | 77 EVD rows; every source resolves |
| G2 Falsification | PASS | 0 Falsified delivery claims (VAL-001…011); VAL-007/VAL-010 are corrected hypotheses, not delivery claims |
| G3 Coverage | PASS | 9/9 Covered |
| G4 Reproduction | PASS | full suite 446/446; legacy 7/7; five frozen hashes; pointers and sidecars; key scan 0 |
| G5 Honesty | PASS | the baseline nulls, the option-A null and the variant results are all recorded |

## Experiment outcome (pre-registered)

| Surface | Calls | Best LCB | Best mean | Promotions | Tool calls | Verdict |
|---|---:|---:|---:|---:|---:|---|
| `llm-refinement` | 10 live | 1.6233 | 50.03 | 0 | — | `lcb-below-margin` |
| `deterministic-refinement` | 0 | 2.3966 | 7.7 | 0 | — | `lcb-below-margin` |
| `ponder` pre-change | 33 live | — | — | — | **0** | 33 legal actions |
| `ponder` option A | 39 live | — | — | — | **0** | 39 legal actions |
| `ponder` task | 55 live | — | — | — | **0** | 55 legal actions |
| `ponder` both | 53 live | — | — | — | **0** | 53 legal actions |
| **`ponder` both-required (winner)** | **106 live** | — | — | — | **54** | **61 legal actions, 0 errors** |
| `ponder` reasoner-both | 57 live | — | — | — | **0** | served `deepseek-flash` |

## Final regression

- Build: 0 errors (12 pre-existing test warnings).
- Suite: `TOTAL 446 PASSED 446 FAILED 0 SKIPPED 0`.
- Legacy CLI: 7/7 hashes in two fresh directories, no `harness` directory.
- Five frozen components: unchanged from the Round-0 snapshot.
- Arm pointers: `ba242797…`, `tholdem-strategy-v1-c9d1a24b`, iteration 0, both arms.
- Report sidecars: both match.
- Key-prefix scan: 0 hits.
- Live artifacts mirrored with hashes, including the winner's session and 106 transcripts.

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract, pre-registration, baseline | Probe | 0 | 16 | 0 | — | complete |
| 1 | WP-1 seam + WP-2 transcript/replay | Implementer + verification | 1 | 7 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | accepted |
| 2 | WP-3 live refiner policy | Implementer + verification | 1 | 6 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | accepted |
| 3 | WP-4 configuration + WP-5 arms | Implementer + orchestrator live runs | 1 | 9 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | accepted (no promotion) |
| 4 | WP-6 live Ponder match | Implementer (host) + orchestrator (live run + probe) | 1 | 4 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | D6 Partial |
| 5 | Remaining planned tests | Implementer + verification | 1 | 8 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | accepted (1 red gate) |
| 6 | Behavior proof + final regression + report | Implementer + orchestrator | 1 | 8 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | goal not complete |
| 7 | Replay, tool-path and wall-clock evidence | Implementer + verification | 1 | 4 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | evidence accepted |
| 8 | Behavior-mix falsification and pinned test | Implementer + orchestrator verification | 1 | 2 | 1 (fold-only claim) | G1✓ G2✓ G3✗ G4✓ G5✓ | D6 unchanged |
| post-cap | Integrated final-evidence test | Implementer + orchestrator verification | 1 | 1 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | evidence pinned |
| 10 | Authorized option A + second live round | Orchestrator (authorized product change) | 0 | 1 | 1 (option A hypothesis) | G1✓ G2✓ G3✗ G4✓ G5✓ | 0 tool calls; D6 Partial |
| 11 | Authorized parallel variants + winner | Implementer (mode) + orchestrator (four live runs) | 1 | 4 | 0 | **G1✓ G2✓ G3✓ G4✓ G5✓** | **goal complete** |

## Next objective

None — the goal is complete. The pre-registered LLM arm result remains an honest no-promotion; the deterministic mode, legacy hashes and the five frozen components are unchanged.

## Post-completion exploration (2026-11-09)

- Four live **6-seat** Ponder+Enhance rounds at seed `1099512227776`: 145 tool calls, 0 errors, all served `deepseek-flash`.
- Candidates: 0.55/0.60/0.18/1.25, 0.60/0.55/0.18/1.25, 0.60/0.62/0.18/1.25, 0.62/0.66/0.18/1.15; gate LCBs −40.494/−45.935/−34.508/−78.977; all refused; champion unchanged at 0.70/0.70/0.12/1.0.
- Exposure: `docs/goal-llm-harness/ponder-loop/results.md`; evidence `EVD-078/079`; journal `OBS-036/VAL-012/DEC-021`.

### Second series (2026-11-09, after the first null)

- Batch B (genome measurement + gate-refusal memory, 4 live rounds): four more refused candidates; the 20-hand genome sample was noisy (champion sample −42.5 vs 24-seed +140.9); the model misread its own refusals as failed tightening.
- Gate-aware 3 + best-of-10: best gate LCB **2.568**; the model repeated `0.62/0.66/0.18/1.15` in 7/10 tries; all parser-valid after repair except the first three (long reasons, salvaged for scoring).
- Deterministic search: **0.95/0.20/0.10/1.0** clears the frozen gate at **24 seeds (LCB 21.164), 50 seeds (23.931) and 100 seeds (15.614)**; **0.90/0.30/0.10/1.0** clears at 13.077 (50); a local search from the LLM's best at raise 0.90 (**0.90/0.30/0.14/1.05**) clears at 19.057 (50). The old 0.80/0.40 fixture is seed-sensitive (−12.55 at 50).
- Conclusion: the LLM's direction (lower raise) is the bottleneck; the clearing region is higher raise/lower call. Recommended design: LLM proposes, local search refines, frozen gate decides.
- Exposure: `docs/goal-llm-harness/ponder-loop/experiments-2.md`; evidence `EVD-080/081`; journal `OBS-037/VAL-013/DEC-022`.

### Third series (2026-11-09): search-augmented loop — promotion

- 3 live 6-seat rounds at seed `1099512627776`; the LLM seed is refined by a 100-point grid on **disjoint** 50-seed data, and the frozen gate evaluates the winner on **untouched** 24 seeds.
- **Round 1 promoted `0.90/0.20/0.18/1.0`**: seed 0.60/0.55/0.18/1.25, search winner 0.90/0.20/0.18/1.0 (search LCB 43.362), gate mean **33.59375**, LCB **11.8854**, verified; pointer `tholdem-strategy-v2-6a108c2b`.
- Rounds 2–3 refused against the new champion; Ponder 74/85/61 turns, 34/43/28 tool calls, 0 errors.
- Robustness: two fresh base seeds × 24/50/100 seeds all promoted, LCB 27.7–36.2.
- The promotion is in the loop's scratch pointer (`.tmp/goal-llm-harness/loop/live-search/refiner-data/`); the product pointer is unchanged. Adoption is the user's call.
- Exposure: `docs/goal-llm-harness/ponder-loop/experiments-3.md`; evidence `EVD-082/083`; journal `OBS-038/VAL-014/DEC-023`.
