# Round 9 report — equity-aware policy and pot-relative sizing

**Status:** verified legality/determinism/sizing; D4 Partial. The policy is passive in its first
12-hand probe and no strength claim is made.

## Delivered

- `PotRelativeSizing`: target = own street commitment + call + fraction * (pot + call), half-away
  rounding, clamped to the legal min/max.
- `EquityPolicy`: sample count, opponent-count-relative `ValueEdge`/`RaiseEdge`, seeded bluff
  frequency, pot fraction. No inert fields.
- `EquityPolicyAgent`: counts live opponents from public seats, estimates multiway pot share, calls
  only when the whole interval clears pot odds, raises on a strong interval, value-bets/bluffs with
  pot-relative sizing, and legality-checks every action. The legacy `StrategyAgent` is untouched.

## Verified

| Check | Observed |
|---|---|
| Pot fractions .25/.5/.75/1 on a 200 pot | 50 / 100 / 150 / 200 |
| Clamping | min-raise and max-stack cases clamp correctly |
| Call + own commitment | pot 100, call 50, own 20, .5 -> 145 |
| 4-seat 12-hand match | 22 decisions, all legal: 1 Bet, 1 Raise, 1 Call, 9 Check, 10 Fold |
| Determinism | replay of every recorded turn produces the same action |
| Tests / suite / build | 9/9 new, **540/540**, 0 errors |

## Findings and limits

- The policy is **passive** in this probe: the conservative Uncertain -> fold rule and 150-sample
  intervals dominate. A 400+ sample budget and a less binary uncertainty rule are needed before it
  is a credible search starting point.
- Preflop situation branches (unopened / facing limp / facing three-bet / blind defense) are not
  implemented: the observation carries no normalized action history.
- The policy does not use `CallEvCalculator` yet: per-seat total commitments are not exposed by the
  observation.
- No EV or strength claim; the policy has only legality, determinism and sizing evidence.
- Anchor 3 remains the round-7 null.

## Next

OBS-003/004: expose per-seat total commitments and a normalized action history without changing the
two-seat canonical observation bytes; then preflop branches and real call-EV integration.
