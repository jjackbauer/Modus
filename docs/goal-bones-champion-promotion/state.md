# Harness State — bones-champion-promotion

- **Round**: 4 of 6 (complete)
- **Goal**: Promote a demonstrably stronger seat-1 strategy on the frozen 4,000-game benchmark.
- **Definition of done**: 6/6 dimensions met
- **Stop criteria**: D1-D6 met; round cap not reached

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001..031 resolve every claim |
| G2 Falsification | PASS | Orchestrator verdict table has 0 falsified rows; independent Falsifier run reproduced the result but its verdict artifact is missing (RISK-006) |
| G3 Coverage | PASS | All six dimensions met |
| G4 Reproduction | PASS | Builds, 8+2 facts, three CLI runs, real library reload, hashes, and source identity reproduced |
| G5 Honesty | PASS | VSTest blocker, subagent stalls, missing Falsifier artifact, and live-host gap recorded |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 0 | Contract goal and create harness memory | — | 0 | 3 | 0 | — | complete |
| 1 | Map exact APIs and minimal integration path | Probe | 1 | 8 | 0 | G1✓ G2✓ G3– G4✓ G5✓ | complete |
| 2 | Implement pure champion selector + eight facts | Implementer | 1 | 6 | 0 final | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |
| 3 | Add aggregator, champion CLI, and persistence | Implementer→orchestrator | 3 | 8 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |
| 4 | Independently falsify the champion delivery | Falsifier→orchestrator | 1+0 | 3 | 0 | G1✓ G2✓ G3✓ G4✓ G5✓ | complete |

## Coverage

| Dimension | Status | Evidence |
|---|---|---|
| D1 Build | Met | EVD-001, EVD-016, EVD-017, EVD-021 |
| D2 Selector tests | Met | EVD-018, EVD-019, EVD-026; 8 selector + 2 aggregator facts pass |
| D3 End-to-end promotion | Met | EVD-022, EVD-023, EVD-028, EVD-031; v9 27.65% vs v3 22.775%; temp library reloads v9 |
| D4 Determinism | Met | EVD-020, EVD-024; identical selected/wins/LCB across runs |
| D5 Data safety | Met | EVD-003, EVD-025; four hashes unchanged |
| D6 Regression | Met | EVD-019, EVD-026; observed-gap fact passes |

## Final result

The `champion` command selects `initial-seat-4-v9@00b1a655` with 1,106 wins / 4,000 games (27.650%, Wilson LCB 26.286%), 4.875 percentage points above the deployed `initial-seat-1-v3@8bf48727` (911/4,000, 22.775%, LCB 21.502%). It writes the selected source to a temp `seat-1.json`; a real `BonesStrategyLibrary.LoadBestStrategy` reloads it. The live `bones-data` library is untouched.

## Remaining work (not in this goal)

- Wire the selector into `BonesLearningLoopHost` startup/promotion paths (C6/C7).
- Add the archive/historical discovery and production benchmark service (C5/C2 full form).
- Add the ranking artifact/API (C9) and full compliance registration (C11).
- Replace the VSTest workaround with a runnable testhost in the normal environment.