# Round 12 report — preflop situations and call-EV in the equity policy

**Status:** verified integration; D3/D4 remain Partial; no strength claim. Goal active.

## Delivered

- `CallEvObservation`: projects the public seat totals into `CallEvInput`, counts caller-eligible
  side-pot layers and applies the supplied uniform pot share to each (per-layer equity is a labelled
  later refinement).
- `PreflopSituationClassifier`: unopened / facing-limp / facing-open / facing-three-bet plus a
  blind-defense flag, from the public action history only.
- `EquityPolicyAgent`: calls now use `callEv.BreakEvenEquity` plus a preflop raise margin instead of
  a raw pot-odds scalar; a short all-in opponent no longer induces an over-call.

## Verified

| Check | Observed |
|---|---|
| Side-pot bridge | effective 80, layers 120/60/100, contestable 280, EV **-10.0**, break-even **0.285714** |
| No-call branch | effective 0, EV = equity × contestable pot |
| Classifier fixtures | Unopened 0/0; FacingLimp 0/1; FacingOpen 1/0 (blind defense button/BB true, UTG false); FacingThreeBet 2/0 |
| Real 12-hand probe | 10 postflop, 3 unopened, 9 facing limp; no open/three-bet occurred |
| Post-change actions | Fold 10, Check 9, Call 1, Bet 1, Raise 1 over 22 turns |
| Tests / suite / build | 7/7 new, **556/556**, 0 errors |

## Limits

- Per-layer equity is a single overall pot share repeated across layers; true per-layer equity is a
  later refinement.
- The real sample never exercised facing-open or facing-three-bet; those branches are synthetic-only.
- No evaluator comparison was run; no strength or EV claim.
- Anchor 3 remains the round-7 null.

## Next

D1: the four-seat CLI surface — persist/parse the plan and report, and add an additive `evaluate4`
command that runs the v2 evaluator/gate/verifier while the v1 commands stay unchanged.
