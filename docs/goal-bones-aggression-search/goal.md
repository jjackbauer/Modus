# Goal — Bones Aggression Search

## One-sentence outcome
Search progressively more aggressive mutations of the v9 champion and either produce a variant whose disjoint-seed 8,000-game win rate beats v9 with a positive 95% lower bound, or prove the tested aggression grid yields no statistically supported improvement.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | Search harness | Build and run the deterministic aggression harness | At least 6 parameter levels generated from the v9 source; each compiles; the baseline level reproduces v9's 4,000-game 1,106/4,000. |
| D2 | Selection grid | Benchmark all levels on seeds 910001/910002, 2,000 games each | `selection-results.json` lists per-level wins/games/win rate and a ranked top 3. |
| D3 | Held-out validation | Benchmark top 3 + v9 on disjoint seeds 910003/910004/910005/910006, 2,000 games each (8,000/candidate) | `validation-results.json` lists wins/games, win-rate difference vs v9, and its 95% CI. |
| D4 | Capability claim | Compare the best variant to v9 | Claim "more capable" only if the validation difference's 95% lower bound is > 0; otherwise report the best observed variant and "no statistically supported improvement." |
| D5 | Persist and reload | If a variant wins, write it to a temp library | Temp `seat-1.json` contains the winning source; `BonesStrategyLibrary.LoadBestStrategy` returns its ID. If no variant wins, no live/threshold claim is made. |
| D6 | Determinism and safety | Repeat the winning validation run; hash `WIP/Wip.Bones.Host/bones-data` | Same selected variant and wins; four baseline hashes unchanged. |

## Hard constraints
- No DeepSeek or external network calls.
- Search and validation seed sets must be disjoint: selection 910001/910002; validation 910003-910006.
- Never write to `WIP/Wip.Bones.Host/bones-data`; all variants and libraries go under `.tmp/aggression-search/`.
- Do not change production `WIP` source for this experiment unless a tooling fix is proven necessary; the search harness lives under `.tmp/aggression-search/`.
- Single-node builds (`-m:1 -nodeReuse:false`); no `dotnet test` (testhost blocked).
- Use the existing `Arena`/`BonesStrategyScriptHost`; do not weaken the benchmark field or game count.
- Report a negative result honestly; do not promote a grid winner that fails held-out validation.

## Stop criteria
- D1-D6 met, including an explicit positive or negative capability verdict, or
- Round cap 4 with every open dimension carrying a concrete blocker, or
- The same blocking condition persists for three consecutive rounds.

Round cap: 4.