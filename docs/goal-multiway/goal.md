# Goal — Wip.Tholdem Multiway (2–6 seats, side pots)

- **One-sentence outcome**: `Wip.Tholdem` plays deterministic multiway no-limit Texas Hold'em for 2–6 seats with correct side pots, N-seat observations and histories, an N-seat simulator and viewer, while every previously recorded 2-seat hand still replays, and every claim is verified against a file, command or test.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| M1 | N-seat engine | Play 2–6 seat hands with seeded deals; check action order, blinds rotation, legality and chip conservation | Button/SB/BB rotate correctly; preflop order starts left of BB, postflop left of button; `sum(stacks)+pot == seats*startingStack` after every action for 2–6 seats |
| M2 | Side pots | Construct all-in hands with different stack levels; run an independent brute-force side-pot calculator over random and adversarial hands | Layer amounts and awardees match the calculator exactly; folded chips stay in pots; uncalled excess returns; odd chips go to the first eligible seat left of the button; pot conservation holds |
| M3 | Observation and history | Per-seat observation for 2–6 seats; canonical history v2; replay of new and old hands | No observation exposes the seed, deck or other seats' cards; v2 records replay to the same stacks and hash; every pre-existing v1 hand file still replays with exit 0 and the same hash |
| M4 | N-seat simulator | N agents at one table, N=2..6, same seed twice | Byte-identical session files; per-seat metrics with CIs; zero illegal actions; zero-sum chips across seats |
| M5 | Viewer and CLI | Live and recorded N-seat matches in the viewer; `tholdem play --seats` | N seat panels render with cards and stacks; replay of an N-seat session works; CLI validates 2..6; guard behaviour preserved |
| M6 | Compatibility | The existing 194-test heads-up suite and its recorded artifacts | All existing tests still pass with their expectations unchanged, or every changed expectation is justified as a deliberate multiway change and listed |
| M7 | Integration | `dotnet build Wip.slnx`, architecture, coverage, waiver | Build exit 0; project count and architecture assertion updated; coverage ok; waiver total real |
| M8 | Adversarial gate | Three independent falsifiers on side pots, conservation, hidden information and determinism | Zero falsified rows on the claims as stated |

## Hard constraints

- No release, publish, sign or deploy; no real-money, account or betting integration; offline by default.
- Determinism: same seed + same agents ⇒ byte-identical histories, for any seat count.
- No hidden information through any agent API or artifact; the weak-seed inference limitation stays documented and gated (no regression).
- The 2-seat case must remain behaviourally identical for the same seed and actions, so existing histories replay.
- Do not weaken, skip or delete an existing test to reach green; changed expectations must be listed and justified.
- No secrets in logs; never print environment keys.

## Environments and known limits (carried from the heads-up goal)

- Local `dotnet test` aborts with `Win32Exception (5)`; use `dotnet build` plus the supplementary runner `.tmp/tholdem-verify/RunXunit`.
- CI cannot be triggered or observed from this machine (no `gh`, no token); no CI green is claimed.
- `%TEMP%` is denied to workspace-launched executables; test scratch stays under the test output directory.
- The viewer must not run from `bin/Release` (build lock); run it from a copied output.

## Stop criteria

- M1–M8 Covered at High confidence, zero open falsified rows, zero P0 questions; cap **8 rounds**.
