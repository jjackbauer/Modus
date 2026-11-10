# Deliverables — tholdem-positive-evidence-floor

| ID | Artifact | Path | Type | Supporting evidence | Confidence | Verified by | Round |
|---|---|---|---|---|---|---|---|
| ART-001 | Floor experiment contract and pre-registration | `docs/goal-tholdem-floor/goal.md` | plan | EVD-001 | High | Read back; floors/seeds fixed before any floor result | 0 |
| ART-002 | Default-0 signal-rate floor implementation | `WIP/Wip.Tholdem.Agents/Learning/Refinement/SignatureDrivenRefinementPolicy.cs`, `TrainingOptions.cs`, `TrainingLoop.cs`; `WIP/Wip.Tholdem.Tests/Learning/HarnessSignalFloorTests.cs` | implementation | EVD-002, EVD-003, EVD-004, EVD-005 | High | 7/7 focused; 394/394 full; legacy 7/7; pinned challengers and frozen snapshot unchanged | 1 |
| ART-003 | DEV floor rows (0.05/0.10/0.20) | `.tmp/goal-floor/results-floor00{5,10,20}.jsonl` | dataset | EVD-006, EVD-007, EVD-009 | High | 30 rows each; 0 errors; identical hash `2735ab51…`; field-identical to floor 0 | 2 |
| ART-004 | DEV floor selection | `docs/goal-tholdem-floor/round-2/floor-selection.md`/`.json` | report | EVD-008, EVD-007 | High | Mechanical choice 0.05; positive floors inert on DEV | 2 |
| ART-005 | Fresh holdout rows and summary | `.tmp/goal-floor/results-holdout*.jsonl`; `docs/goal-tholdem-floor/round-3/holdout-summary.md`/`.json` | dataset | EVD-010, EVD-011, EVD-012 | High | 90 rows, 0 errors; 0.05 field-identical to floor 0; vs baseline 4.91x, CI [3.92, 16], zero-effect 7/30 to 0/30 | 3 |
| ART-006 | Floor experiment decision report | `docs/goal-tholdem-floor/final-report.md`, `state.md`, `evidence.md` | report | EVD-001 to EVD-013 | High | F1–F5 Covered; floor rejected as inert; shipped 0 retained | 3 |
