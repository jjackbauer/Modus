# Goal — Bones Actual Champion

## One-sentence outcome
Produce and stage a statistically validated Bones champion: `c5-newend` beats v9 by at least +0.5 percentage points with a 95% lower bound > 0 on two independent 200,000-game fixed-field tests and reloads from a temp library.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | Fair selection | Candidate and v9 face the same fixed 64-strategy field (candidate excluded), 100 seeds × 2,000 games | `c5-newend` win rate > v9; 95% CI lower bound > 0. |
| D2 | Independent confirmation | Repeat D1 on 100 disjoint seeds | `c5-newend` win rate > v9; 95% CI lower bound > 0. |
| D3 | Combined evidence | Pool the two 200,000-game sets | Point estimate ≥ +0.5pp; combined 95% CI lower bound > 0. |
| D4 | Persist and reload | Write the winner to a temp library and reload | `BonesStrategyLibrary.LoadBestStrategy` returns `c5-newend` with the expected source. |
| D5 | Data safety | Hash `WIP/Wip.Bones.Host/bones-data/.bones/strategy-library/seat-*.json` | Four baseline hashes unchanged. |
| D6 | Determinism | Repeat the confirmation run | Same wins/games and same champion. |

## Hard constraints
- No DeepSeek or external network calls.
- The fixed field excludes the candidate and v9; the same field is used for both.
- Never write to `WIP/Wip.Bones.Host/bones-data`; stage under `.tmp/aggression-search/`.
- No production source changes; the search harness is temporary.
- Report the champion as staged, not live, until an explicit live promotion.
- Single-node builds; no `dotnet test` (testhost blocked).

## Stop criteria
- D1-D6 met with EVD rows, or
- Round cap 4 with concrete blockers, or
- Three consecutive rounds blocked by the same condition.

Round cap: 4.