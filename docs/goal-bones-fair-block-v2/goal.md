# Goal — bones-fair-block-v2

## Outcome (one sentence)

A fair, public-information Bones seat strategy that beats the current champion `fair-block` by at
least +0.5 percentage points with a 95% lower bound > 0 on a 200,000-game selection set, an
independent 200,000-game confirmation set, and a 400,000-game final head-to-head — or a bounded
negative result after the six planned approaches are exhausted.

## Incumbent and references

| Strategy | Location | Source length |
|---|---|---|
| `fair-block` (incumbent) | `.tmp/block-search/library-confirm/.bones/strategy-library/seat-1.json` | 16,398 |
| `c5-newend` (its base) | `.tmp/aggression-search/library-fair/.bones/strategy-library/seat-1.json` | 13,517 |
| v9 reference | `.tmp/bones-strategy-analysis/initial-seat-4-v9_00b1a655.cs` | — |
| field | `.tmp/bench/history.json` (65 scripts; v9 excluded → 64) + 5 deterministic archetypes | 69 entries |

Recorded prior scorecard (fair fixed field, candidate and v9 excluded, 400,000 games):
`fair-block` 117,969 (29.4923%), `c5-newend` 116,210 (29.0525%), v9 113,863 (28.4658%).

## Definition of done — falsifiable checks

| # | Dimension | Command | Expected observable |
|---|---|---|---|
| D1 | Selection | `dotnet run --project .tmp/fair-block-v2/FairBlockV2.csproj -c Release -- sweep --specs .tmp/fair-block-v2/specs/cur.json --seeds 100 --games 2000` (seeds 910235–910334) | candidate − fair-block ≥ +0.50pp **and** CI low > 0 |
| D2 | Confirmation | same spec, `--seedBase 910335` (100 disjoint seeds) | CI low > 0 |
| D3 | Final head-to-head | same spec, `--seedBase 910435 --games 4000` (400,000 games) | CI low > 0 |
| D4 | Persist + reload | `… -- promote --id <champion>` | `persisted=True`, `matchesExactly=True`, reloaded source SHA256 == measured source SHA256 |
| D5 | Fairness | `… -- verify` plus a grep for `GetHand`/`HandsByPlayer`/`BonesPlayerId` | only `state.Hands.GetHand(state.CurrentPlayer)`; no oracle access |
| D6 | Exhaustion | every planned approach implemented or rejected with a 200,000-game measurement | six rows in `state.md` with a verdict each |

All claims use the two-proportion interval
`diff ± 1.96·sqrt(p1(1−p1)/n1 + p2(1−p2)/n2)`. "Almost significant" is not a verdict.

## Hard constraints (gates)

1. No hidden-hand access in the champion: `state.Hands.GetHand(...)` may only be called with
   `state.CurrentPlayer`. Oracle variants may be measured as diagnostics and never promoted.
2. No writes to `WIP/Wip.Bones.Host/bones-data`. The experiment lives entirely under `.tmp/fair-block-v2/`.
3. No production `WIP` source edits; no network calls.
4. Builds use `-m:1 -nodeReuse:false`; `dotnet test` is not used (testhost blocked).
5. Do not weaken the field, the game count, or the interval.
6. Seed hygiene: nothing ≤ 910234. Selection 910235–910334, confirmation 910335–910434,
   final 910435–910534. A seed set may not serve two roles.

## Stop criteria

- **Champion**: D1–D5 all pass.
- **Exhaustion**: all six planned approaches have a 200,000-game verdict, and the best remaining
  variant has a reported final 400,000-game CI against `fair-block`.
- **Round cap**: 8 rounds.
