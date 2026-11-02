# Goal — Bones Fair Blocker

## One-sentence outcome
Produce and stage a fair public-information blocker (`fair-block`) that beats `c5-newend` by at least +0.3 percentage points with a 95% lower bound > 0 on two independent 200,000-game fair fixed-field tests and reloads from a temp library.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | Fair selection | `fair-block` vs `c5-newend` on a fixed 64-strategy field excluding candidates; 100 seeds × 2,000 games | `fair-block` > `c5-newend`; 95% CI lower bound > 0. |
| D2 | Independent confirmation | Repeat D1 on 100 disjoint seeds | Same direction; 95% CI lower bound > 0. |
| D3 | Combined evidence | Pool both 200,000-game sets | Point estimate ≥ +0.3pp; combined lower bound > 0. |
| D4 | Fairness | `fair-block` uses only own hand, board, public event log, seat order | No `state.Hands.GetHand(new ...)` access to opponents. |
| D5 | Persist and reload | Write winner to a temp library and reload | `LoadBestStrategy` returns `fair-block` with source length 16,398. |
| D6 | Data safety | Hash live `bones-data` library | Four baseline hashes unchanged. |

## Hard constraints
- No DeepSeek or external network calls.
- No hidden-hand access in `fair-block`; the oracle variant is diagnostic only, never a champion.
- Never write to `WIP/Wip.Bones.Host/bones-data`; stage under `.tmp/block-search/`.
- No production source changes; the experiment is temporary.
- Single-node builds; no `dotnet test` (testhost blocked).
- Report a negative result if the fair variant does not beat `c5-newend`.

## Stop criteria
- D1-D6 met, or
- Round cap 4 with concrete blockers, or
- Three consecutive rounds blocked by the same condition.

Round cap: 4.