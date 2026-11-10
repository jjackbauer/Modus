# Deliverables — goal-llm-harness

> An `ART` row needs at least three supporting IDs and a `Verified by` that names the check. No artifact is registered until the orchestrator reproduced it.

## Registered artifacts

| ID | Artifact | Path | Type | Supporting evidence | Confidence | Verified by | Last updated |
|---|---|---|---|---|---|---|---|
| ART-001 | Model harness project (transcripts, parser, prompt builder, live policy, live configuration, selectable tool-prompt mode) | `WIP/Wip.Tholdem.ModelHarness/`, `WIP/Wip.Tholdem.Agents/Runtime/` | implementation | EVD-017, EVD-024, EVD-025, EVD-026, EVD-075 | High | Full suite 446/446; focused filters 50 + 2 mode tests; key scan 0 | 2026-11-09 |
| ART-002 | LLM refinement arm report and re-derived verdict | `docs/goal-llm-harness/round-3/llm-arm-report.json`, `llm-arm-verdict.json` | report | EVD-031, EVD-033, EVD-058 | High | Ran the arm and the verify mode; sidecar hash matches; pointer bytes unchanged | 2026-11-09 |
| ART-003 | Deterministic-refinement control report and verdict | `docs/goal-llm-harness/round-3/deterministic-arm-report.json`, `deterministic-arm-verdict.json` | report | EVD-034, EVD-035, EVD-058 | High | Ran the control and the verify mode; sidecar hash matches | 2026-11-09 |
| ART-004 | Live Ponder sessions and canonical transcripts, with durable copies | `.tmp/goal-llm-harness/ponder/live/` (pre-change), `live2/` (option A), `v-required/` (winner); copies under `docs/goal-llm-harness/round-4/live-artifacts/` | dataset | EVD-041, EVD-050, EVD-069, EVD-072, EVD-074, EVD-076 | High | Ran all six live matches; the winner made 54 tool calls; hash-valid transcripts; copies byte-identical | 2026-11-09 |
| ART-005 | Behavior-proof checklist with path/category validator | `docs/goal-llm-harness/behavior-proof.md`; `WIP/Wip.Tholdem.Tests/ModelHarness/BehaviorProofComplianceTests.cs` | document + test-suite | EVD-053, EVD-052 | High | Ran the focused filter 3/3, including synthetic rejection cases | 2026-11-09 |
| ART-006 | Final report and goal evidence | `docs/goal-llm-harness/final-report.md`, `evidence.md` (77 rows) | report | EVD-075, EVD-077 | High | Final regression: 446/446, legacy 7/7, five frozen hashes, 0 key hits | 2026-11-09 |
| ART-007 | D6 unblock proposal (scoped prompt/task options, pre-registration, threat review) | `docs/goal-llm-harness/ponder-tool-use-proposal.md` | document | EVD-043, EVD-044, EVD-062, EVD-065 | High | Read the proposal; every cited path/line resolves | 2026-11-09 |
| ART-008 | Authorized option A pre-registration and second-round report | `docs/goal-llm-harness/round-10/ponder-rerun-preregistration.md`, `implementation.md` | document | EVD-072, OBS-034, VAL-010 | High | Read both; seed verified unused; before/after hashes recorded; run log present | 2026-11-09 |
| ART-009 | Authorized parallel variants pre-registration, results and winner | `docs/goal-llm-harness/round-11/variant-preregistration.md`, `variants-results.md`, `v-*.log` | document + dataset | EVD-074, EVD-075, EVD-076 | High | Ran the four variants concurrently; verified the winner's 54 tool calls and 54 results; suite 446/446 | 2026-11-09 |

| ART-010 | Live 6-seat Ponder+Enhance loop exposure (4 rounds, 145 tool calls, 4 strategies, gate verdicts) | `docs/goal-llm-harness/ponder-loop/` (`results.md`, `strategies-report.*`, `evaluations/`, `refiner/`, `artifacts.sha256`) | report + dataset | EVD-078, EVD-079 | High | Ran the four live rounds; verified sessions/refiner/evaluations; hashed copies | 2026-11-09 |

## Not a deliverable

- The pre-registered **promotion rule** was not met: the LLM arm produced no verified promotion (best LCB 1.6233, best mean 50.03; the gate refused). That is an honest experimental result, not a failure of the harness.
- The **D6 tool-call gap was resolved** by the authorized parallel round: `both-required` made 54 live tool calls; D6 is Covered and the goal is complete.
- The scratch arm runner and Ponder host under `.tmp/goal-llm-harness/` are evidence plumbing, not product code.
