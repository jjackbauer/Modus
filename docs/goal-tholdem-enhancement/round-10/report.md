# Round 10 report — observation totals and action history; campaign cap reached

**Status:** verified additive observation data with canonical bytes preserved. The harness campaign
has reached its 10-round cap (S3). S1 is met; S2 is not. The persisted goal remains active.

## Delivered

- `PublicAction`: street, seat, kind, amount-to and pot-after for every public action.
- `SeatPublicState.TotalCommitted`: per-seat chips committed across all streets.
- `PlayerObservation.OwnTotalCommitted` and `ActionHistory`, filled at the decision boundary.
- `ToCanonicalJson` untouched: no new member is serialized, so no agent seed or hand hash changes.

## Verified

| Check | Observed |
|---|---|
| Totals | per-seat totals equal the engine state and sum to the pot |
| History | count and every field match the internal action log |
| Canonical schema | no `totalCommitted` / `ownTotalCommitted` / `actionHistory` in the JSON |
| Two-seat pin | exact 327-byte literal; SHA-256 `a67a4235d5f97ca5...` |
| Independent probe | 7/7 lines byte-identical to the pre-change baseline; hand hash `c953464b...` unchanged |
| Tests / suite / build | 4/4 new, **544/544**, 0 errors |

## Approved exception

`PlayerObservationTests` pins the complete public property list. It gained exactly `ActionHistory`
and `OwnTotalCommitted`; no other assertion changed. This is an intentional additive surface update
(DEC-005), not a weakened behavioral test, and the canonical golden is independently confirmed.

## Coverage at the cap

| Dimension | Status |
|---|---|
| D6 paired evaluator + gate | **Covered** |
| D7 frozen opponent population | **Covered** |
| D1 four-seat contract | Partial — library done; CLI still heads-up |
| D2 observation truthfulness | Partial — totals/history done; VPIP missingness and versioned context open |
| D3 equity | Partial — contracts/estimators/call EV done; legacy agent and runtime integration open |
| D4 policy | Partial — equity policy + sizing done; preflop situations/action traces open |
| D8 mutation/search | Partial — equal-budget runner and null result |
| D11 reproducibility/safety | Partial — hashes and v1 preservation; deny-tests/budgets open |
| D5/D9/D10/D12 | None |

Anchor 3 (learning competence) has a recorded **no-benefit**; it is not demonstrated and is not
described as such.

## Next campaign objective

Integrate `CallEvCalculator` and preflop situation branches into `EquityPolicyAgent` using the new
observation data; close OBS-005 (VPIP nullability); then add the four-seat CLI surface. A credible
anchor-3 retry needs that richer policy family and a diagnosis-driven proposer.
