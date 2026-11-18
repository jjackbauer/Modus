# Deliverables — wip-factory-alpha

> An `ART` needs ≥3 supporting IDs and a `Verified by` that names a command I ran. Otherwise it is a TODO.

| ID | Artifact | Path | Type | Supporting evidence | Confidence | Verified by | Last updated |
|---|---|---|---|---|---|---|---|
| ART-001 | Functional-alpha requirement set (amended by A1, 105 items / 82 tests) | `harness/requirements/Wip.Factory.Alpha.md` | document | EVD-002, EVD-003, EVD-004, EVD-014 | High | Regex scan: 105 `- [ ]` items, 82 numbered backticked tests, `D-05` present; solution still builds with it in place | 2026-11-18 |
| ART-002 | Goal contract (D1–D9, corrected after audit, D4 baseline-relative) | `docs/goal-wip-factory-alpha/goal.md` | plan | EVD-001, EVD-005, EVD-006, EVD-011, EVD-015 | High | D1/D2 proven by EVD-011/EVD-020; D4 baseline pinned by EVD-015 and DEC-004 | 2026-11-18 |
| ART-003 | Independent intent audit (41 KEPT / 2 REPLACED / 1 REMOVED) | `docs/goal-wip-factory-alpha/audit-intent.md` | report | OBS-004, VAL-001, EVD-014 | High | Its M-08 claim re-verified in VAL-001 and closed by DEC-002 | 2026-11-18 |
| ART-004 | Factory project skeleton (12 implementation + 3 test projects) | `WIP/Wip.Factory.*` | implementation | EVD-011, EVD-012, EVD-013 | High | `dotnet build Wip.slnx` → `0 Error(s)`; `Wip.slnx` declares 66 entries | 2026-11-18 |
| ART-005 | Derived architecture gate + six factory layering rules | `tests/Wip.Architecture.Tests/` | test-suite | EVD-011, EVD-012, EVD-004 | High | 5/5 green; solution-vs-disk set equality; all six rule ids bite on synthetic edges | 2026-11-18 |
| ART-006 | Typed contract layer + work-state machine + dependency readiness | `WIP/Wip.Factory.Contracts/`, `WIP/Wip.Factory.Core/` | implementation | EVD-018, EVD-019, OBS-008 | High | Tests 1–4 pass (Core.Tests 4/4); state-machine test mutation-checked (EVD-019); build clean | 2026-11-18 |
| ART-007 | Plugin → `WipBuilder` contributor binding with fault isolation | `WIP/Wip.Factory.Modus/FactoryCapabilityContributor.cs` | implementation | EVD-021, EVD-022, EVD-023, EVD-024 | High | Tests 7–10 pass (Agents.Tests 4/4); throwing contributor isolated; duplicate id refused with first registration surviving | 2026-11-18 |
