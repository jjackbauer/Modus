# Final report v2 — four-player benchmark delivered, learning claim null

**Status:** delivery objective complete; the conditional learning claim is **not** made because no
proposal process beat the equal-budget controls.

## 1. Delivered benchmark

- Four-seat evaluation: versioned `GameConfiguration`, `RunDetailed`, protected seed domains,
  strict plan JSON, frozen five-archetype pool with disjoint holdout, paired focal-seat evaluator,
  block statistics, Bonferroni gate, independent verifier.
- Four-seat training: `FourSeatTraining` + `train4`, promotion only through the v2 gate, isolated
  `four-seat/` pointer, no v1 pointer writes.
- Equity: typed estimates, N-opponent pot share, weighted ranges, confidence gate, side-pot call EV,
  preflop situations, equity-aware policy.
- Observations: totals + action history with canonical bytes preserved; measured VPIP with
  nullability and denominators.
- Engine: two-to-six seats preserved; legacy bytes unchanged.

## 2. The conditional claims

| Claim | Evidence | Status |
|---|---|---|
| Strategically improved challenger | control experiment: fold vs call-any +199.738 bb/100, LCB +176.917; anchor +144.651/+115.038; verifier true | condition enforced; the control pair is a harness validation, not a strategy claim |
| Regression guards | gate refused anchor regressions in random-search, equity-policy and diagnosis arms | delivered |
| WiP better at learning | six equal-budget experiments below | **not demonstrated** |

## 3. Learning experiments (all nulls)

| Experiment | Result |
|---|---|
| Diagnosis v1 (4 tasks x 4) | 0/16 both; diagnosis 7 Unsafe vs 4 |
| Diagnosis v2r (8 tasks x 6, pre-registered) | 0/48 both; diagnosis 20 Unsafe vs 7 |
| LLM 16-request pilot | 16/16 parsed; gate 0 verified, 8 rejected, 8 unsafe |
| LLM vs controls (same 16 candidates) | LLM 0/11; random 0/15; grid 0/15; greedy 0/7 |
| Family probe (144 genomes) | 1 verified (+150.625/+71.452); top mean Unsafe |
| Reachability (8 chained) | deterministic 1/8 (+66.09/+11.67); diagnosis 0/8 |
| Guarded vs controls (4 tasks x 8) | guarded 0/22; deterministic 0/15; random 0/32; grid 0/28 |

The family probe proves the policy family can represent a gate-passing improvement, so the
limitation is the proposal/search process, not the evaluation instrument. No tested proposal process
outperformed the controls; therefore WiP is **not** called better at learning and anchor 3 is null.

## 4. Release claims

- **R1** (correct four-player benchmark): substantially delivered; gaps: D1 legacy commands still
  heads-up, D2 versioned decision context, D3 legacy equity path, D5 diagnostics.
- **R2** (useful strategy search): not demonstrated (nulls above).
- **R3** (LLM value): not demonstrated; 16 live requests parsed, zero gate-verified.
- **R4** (harness learning): not demonstrated (nulls).

## 5. Verification

- Full suite **588/588**, 0 failed; solution build 0 errors; HarnessSync exit 0.
- All experiments persisted with pre-registration (where claimed), results and logs.
- `dotnet test` remains locally unavailable (VSTest testhost); CI is authoritative.

## 6. Open work (not required for the delivery claim)

D5 decision-time EV labels, D2 decision context, D9 controlled LLM attribution at larger budget,
D10 meta-learning, D12 UX. A richer policy family/action space is the most promising path to a
learning signal; repeating the same four-parameter mechanisms is not.
