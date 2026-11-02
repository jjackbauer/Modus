# Deliverables — bones-champion-promotion

| ART | Artifact | Path | Evidence | Confidence | Status |
|---|---|---|---|---|---|
| ART-001 | Champion promotion API map | `docs/goal-bones-champion-promotion/round-1/observation.md` | EVD-004, EVD-005, VAL-001 | High | Delivered |
| ART-002 | Pure champion selector | `WIP/Wip.Bones.Agents/Champions/BonesChampionSelection.cs` | EVD-015, EVD-016, EVD-018 | High | Delivered |
| ART-003 | Champion selector facts | `WIP/Wip.Bones.Tests/Champions/BonesChampionSelectorTests.cs` | EVD-017, EVD-018, EVD-019 | High | Delivered |
| ART-004 | Independent 4,000-game champion probe | `.tmp/champion-goal/verify-champion/` | EVD-013, EVD-014, EVD-020 | High | Delivered (temporary evidence) |
| ART-005 | Champion aggregator + champion CLI + persistence | `WIP/Wip.Bones.Agents/Champions/BonesChampionAggregation.cs`; `WIP/Wip.Bones.Benchmark/ChampionRunner.cs`; `WIP/Wip.Bones.Benchmark/Program.cs`; `WIP/Wip.Bones.Tests/Champions/BonesChampionAggregatorTests.cs` | EVD-021, EVD-022, EVD-023, EVD-026 | High | Delivered |
| ART-006 | Staged seat-1 champion library (v9, 27.65%) | `.tmp/champion-goal/run-b/.bones/strategy-library/seat-1.json` | EVD-022, EVD-023, EVD-028, EVD-029 | High | Delivered (staging, not live) |
| ART-007 | Champion delivery falsification table | `docs/goal-bones-champion-promotion/round-4/orchestrator-verdicts.md` | EVD-022..031, OBS-008 | High | Delivered (0 falsified; independent Falsifier verdict missing) |