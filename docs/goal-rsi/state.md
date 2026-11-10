# Harness State — Wip.Tholdem mini-RSI

- **Round**: 1 (implementation)
- **Goal**: UI-triggered bounded learning loop plus mini-RSI over proposer policy and prompt template, frozen gate, A1–A15 controls.
- **Definition of done**: 7/7 covered, with the residuals stated below
- **Stop criteria**: not met; cap 8 rounds

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001…018 resolve to a command output, a file or a test |
| G2 Falsification | PASS | Native controls + 11-defense mutation campaign; two UI regressions were found by the pre-existing tests, fixed, and re-verified. No independent 3-agent falsification round was run in this single-round delivery (stated). |
| G3 Coverage | PASS | R1–R7 covered |
| G4 Reproduction | PASS | baseline 275/275 and final 317/317 reproduced; real UI run reproduced |
| G5 Honesty | PASS | A1/A9/A13 marked not fully preventable; default-proposer 0.000 runs, meta self-comparison and the non-automatic meta pointer are recorded; no CI claim |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract + baseline | Probe | 0 | 2 | 0 | — | baseline 275/275; scope fixed |
| 1 | Training API + mini-RSI + UI + controls | Implement + self-falsification | 1 delegated UI task + owner | 18 | 0 open (2 UI regressions fixed) | G1✓ G2✓ G3✓ G4✓ G5✓ | 317/317; 11/11 mutation controls red-then-green; standard + mini-RSI UI screenshots; integration gates |

## Next objective

None for this goal. Residuals: A1 proxy gap, A4/A14 unsigned trust anchors, A9 future prompt surface, A13 selective reporting, default proposer inertness, no CI claim. See the final statement in `docs/tholdem/rsi.md §9`.
