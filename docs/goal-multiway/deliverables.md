# Deliverables — multiway

| ID | Artifact | Path | Type | Supporting evidence | Confidence | Verified by | Round |
|---|---|---|---|---|---|---|---|
| ART-001 | General 2–6 seat engine with side pots and v1-compatible history | `WIP/Wip.Tholdem/Engine/`, `WIP/Wip.Tholdem/History/` | implementation | EVD-005, EVD-006, EVD-007 | High | 246/246 suite; my own 3-seat all-in hand produced layers 900/600/400 with conservation at every action; the 2-seat CLI manifest stayed byte-identical (`87f90413…`) | 1b |
| ART-002 | N-seat engine and history test suite (246 cases) | `WIP/Wip.Tholdem.Tests/` | test-suite | EVD-005, EVD-007 | High | Runner `TOTAL 246 PASSED 246 FAILED 0`; mutation on layer arithmetic failed 8 facts | 1b |
| ART-003 | N-seat simulator, metrics and runtime store | `WIP/Wip.Tholdem.Agents/Simulation/`, `WIP/Wip.Tholdem.Agents/Runtime/` | implementation | EVD-008, EVD-009 | High | My 4-seat probe: 31 byte-identical files across runs, identical manifest `2908dfa1…`, net sum 0; 257/257 suite; 2-seat oracle unchanged | 2 |
| ART-004 | N-seat viewer and CLI | `WIP/Wip.Tholdem.Web/`, `WIP/Wip.Tholdem.Host/` | implementation | EVD-010, EVD-011 | High | 4-seat CLI deterministic (`96ffb0e1…`) and replayable; HTTP live snapshot with four seats; 273/273 suite; 2-seat oracle unchanged | 3 |
| ART-005 | Four-seat live screenshot | `docs/goal-multiway/round-3/four-seat-live.png` | dataset | EVD-011 | High | Viewed the capture: four seat panels, per-seat cards and hand names, ALL-IN/dealer/winner badges, `Pot awarded 4000`, seat selector 4, no console errors | 3 |
| ART-006 | Self-contained, schema-aware history compatibility suite | `WIP/Wip.Tholdem.Tests/HistorySessionFixture.cs`, `HistorySessionCompatibilityTests.cs` | test-suite | EVD-012, EVD-013 | High | Generated three shapes (2-seat v1, 2-seat unequal v2, 4-seat v2), verified 18/18, accepted a CLI-produced 2-seat v2 hand, suite green with `artifacts/` absent | 5–6 |
| ART-007 | Multiway delivery (engine, simulator, viewer, CLI, docs) | `WIP/Wip.Tholdem*`, `docs/tholdem/` | implementation | EVD-005…015 | High | 275/275 suite; adversarial gate M1–M5 Supported 3/3; build exit 0; architecture 48; coverage ok; zero Tholdem waivers; 4-seat live and side-pot evidence | 1–6 |
| — | N-seat simulator, viewer, CLI, docs, adversarial evidence | — | — | — | — | pending | — |

## Notes

- ART-001's 2-seat form is byte-identical to the pre-multiway engine for the same seed and actions, and `HandRecord.ToCanonicalJson()` emits the exact v1 form for equal-stack 2-seat hands, so every recorded hash and replay in `artifacts/tholdem` still verifies.
