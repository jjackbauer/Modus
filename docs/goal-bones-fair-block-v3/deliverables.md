# Deliverables — bones-fair-block-v3

### ART-001: Goal contract (adopted and executed)

- **Path**: `docs/goal-bones-fair-block-v3/goal.md`
- **Type**: document
- **Supporting evidence**: EVD-001, EVD-006, EVD-013
- **Confidence**: High
- **Verified by**: Re-read after the session; the status header now records the outcome, and every dimension in its table maps to a row in `state.md`
- **Last updated**: 2026-11-01

### ART-002: Append-only journal

- **Path**: `docs/goal-bones-fair-block-v3/journal.md`
- **Type**: document
- **Supporting evidence**: EVD-003, EVD-008, EVD-009, EVD-011, EVD-012
- **Confidence**: High
- **Verified by**: Every numeric claim in OBS/VAL cross-checked against its `result-*.json` or console transcript; HYP statuses match the VAL verdicts
- **Last updated**: 2026-11-01

### ART-003: Verified-fact table

- **Path**: `docs/goal-bones-fair-block-v3/evidence.md`
- **Type**: document
- **Supporting evidence**: EVD-001…018 (self-registering)
- **Confidence**: High
- **Verified by**: Each Source opened or re-run in round 8; the two-proportion intervals recomputed from raw win counts
- **Last updated**: 2026-11-01

### ART-004: Round snapshot

- **Path**: `docs/goal-bones-fair-block-v3/state.md`
- **Type**: document
- **Supporting evidence**: EVD-004, EVD-005, EVD-008, EVD-010, EVD-011, EVD-012
- **Confidence**: High
- **Verified by**: The approach-verdict table was rebuilt from the four spec runs, not from memory
- **Last updated**: 2026-11-01

### ART-005: v3 harness

- **Path**: `.tmp/fair-block-v3/` (`Program.cs`, `Audit.cs`, `Variants.cs`, `policies/`, `specs/`)
- **Type**: benchmark
- **Supporting evidence**: EVD-001, EVD-002, EVD-018
- **Confidence**: High
- **Verified by**: `dotnet build … -c Release -m:1 -nodeReuse:false` → exit 0, 0 warnings, 0 errors. New capabilities exercised: `ceiling` (diagnostic loading with audit exemption), `--half` field split, optional `--specs`, and a `promote` guard that refuses non-fair sources
- **Last updated**: 2026-11-01

### ART-006: Fair Monte-Carlo planner candidates

- **Path**: `.tmp/fair-block-v3/policies/candidates/planner-mc.cs`, `planner-mc16.cs`, `planner-low16.cs`
- **Type**: implementation
- **Supporting evidence**: EVD-010, EVD-018
- **Confidence**: High
- **Verified by**: All three pass `verify` (`fair=YES`, one own-hand `GetHand`); 2,000-game throughput measured at ~2s wall; rates 20.49–20.88%
- **Last updated**: 2026-11-01

### ART-007: Diagnostic oracles (never promotable)

- **Path**: `.tmp/fair-block-v3/policies/diagnostic/oracle-greedy.cs`; `oracle-truth-dist` generated from `specs/ceiling.json`
- **Type**: benchmark
- **Supporting evidence**: EVD-004, EVD-005
- **Confidence**: High
- **Verified by**: The harness prints `[diagnostic] '<id>' is exempt from the fairness audit and is not promotable`; `promote` calls `FairnessAudit` and would throw on these sources
- **Last updated**: 2026-11-01

### ART-008: Measurement result documents

- **Path**: `.tmp/fair-block-v3/result-sweep-{910535,910635,910735,910835}.json`, `result-paired-910535.json`
- **Type**: benchmark
- **Supporting evidence**: EVD-003, EVD-009, EVD-013, EVD-014, EVD-015
- **Confidence**: High
- **Verified by**: Each file written by the run that produced the printed table and re-read to recompute the intervals
- **Last updated**: 2026-11-01
