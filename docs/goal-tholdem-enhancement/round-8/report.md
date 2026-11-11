# Round 8 report — contribution- and side-pot-aware call EV

**Status:** verified calculator; D3 still Partial because the legacy agent and runtime integration
still use the old unqualified comparison. Goal active.

## Delivered

`CallEvCalculator` builds pot layers from the caller-after-call contribution and every opponent
contribution (folded seats included as dead money), caps the effective call at the largest live
commitment and returns chips above it, applies caller-supplied per-layer equities only to layers the
caller can win, and reports effective call, contestable pot, call EV, fold EV = 0 and break-even
equity = (effective call + future cost) / contestable pot.

## Verified

| Check | Observed |
|---|---|
| Side-pot fixture (pot 170, own 20, seats 50 and 100 live, call 80) | effective 80, contestable 250, **EV +5.0**, break-even **0.32**; layers 150 @ [1,2] and 100 @ [2] |
| Heads-up | effective 60, contestable 160, EV +4.0, break-even 0.375 |
| Dead money | folded 30 included, layers 90 + 100, EV +2.5 |
| Excess return | own 80 vs live 100, call 50 -> effective 20, not 50 |
| Future cost | +10 shifts EV +5.0 -> -5.0, break-even 0.36 |
| Tests | 6/6 |
| Full suite | **531/531**, 0 failed |
| Build | 0 errors |

## Not done

- The calculator is not wired into `StrategyAgent`, the runtime equity/pot-odds tools, or the
  evaluator; it is a checked contract.
- The legacy `StrategyAgent` still compares `HandStrength.Estimate` to pot odds, so EQ-001 remains
  Partial until an equity-aware policy replaces that path.
- Range inference from public action history (EQ-004 provenance) remains unwired.
- Anchor 3 remains the recorded null from round 7; no learning claim.

## Next

D4: an equity-aware policy using `EquityEstimate`, `CallEvCalculator` and `EquityThresholdPolicy`,
with preflop situation branches, street branches and pot-relative sizing. Then a retry of the
equal-budget ablation with that richer policy family.
