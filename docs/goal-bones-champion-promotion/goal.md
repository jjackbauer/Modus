# Goal — Bones Champion Promotion

## One-sentence outcome
Bones promotes a demonstrably stronger seat-1 strategy on the frozen 4,000-game benchmark: a deterministic champion-selection path replaces the deployed 22.78% strategy with a candidate above 25.9%, persists it to a temp library, and targeted tests prove the selection behavior.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | Build | `dotnet build` on `Wip.Bones.Agents`, `Wip.Bones.Benchmark`, and `Wip.Bones.Tests` | All three exit 0. |
| D2 | Selector tests | Run the new champion-selection tests | All pass; a 1/1 fluke cannot beat a 12/20 record; confirmation failure retains the incumbent. |
| D3 | End-to-end promotion | Run the champion-selection command on the historical 65-strategy bank with a temp data directory | Selected seat-1 strategy has a 4,000-game win rate >= 25.9% and at least 2.0 percentage points above the deployed 22.78%; temp `seat-1.json` contains it; a reload returns it. |
| D4 | Determinism | Run the same command twice with the same seeds | Identical selected strategy ID, games, wins, and win rate. |
| D5 | Data safety | Hash `WIP/Wip.Bones.Host/bones-data/.bones/strategy-library/seat-*.json` before and after all work | Hashes unchanged. |
| D6 | Regression | Run the observed-gap fixture test | `initial-seat-1-v2@004e6719` is selected over `initial-seat-1-v3@8bf48727`; the opposite result fails the test. |

## Hard constraints
- No DeepSeek or external network calls.
- Never write to `WIP/Wip.Bones.Host/bones-data`; all promotion outputs go under `.tmp/champion-goal/`.
- Do not weaken, skip, or delete existing tests/assertions.
- Keep production changes inside `WIP/Wip.Bones*`; keep harness artifacts inside `docs/goal-bones-champion-promotion/`.
- Use documented `dotnet build`/`dotnet test`/benchmark commands; no unbounded host runs.
- The goal is a vertical slice of `harness/requirements/Wip.Bones.ChampionPromotion.md` C1-C5 + C10 regression; host periodic wiring (C7) is a stretch, not a gate.

## Stop criteria
- D1-D6 are all Met with `EVD` rows, or
- Round cap 6 is reached with every unmet dimension carrying a concrete blocker, or
- The same blocking condition persists for three consecutive rounds (then `blocked` with the condition named).

Round cap: 6.