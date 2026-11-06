# Round 3 — Orchestrator verification of WP3 (Enhance on the runtime)

- **Round**: 3 (goal WIP-IMPL-001 continuation, D3)
- **WP3 deliverable**: `docs/execution/harness/round-3/implementation-wp3.md`
- **Starting HEAD**: `3e0087a` (WP2)
- **Verification environment**: isolated ext4 clone `/home/ricardo/verify` at `3e0087a` with the
  accepted WP3 files overlaid, .NET SDK 10.0.103, `LANG=en_US.UTF-8`, sandbox worker prebuilt.
- **Result**: PASS — focused 10/10, all Enhance-related tests 49/49, full Bones 433/430 with only the
  3 waived meta-tests failing, both parity controls fail as required.

## 1. Focused reproduction

`dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesEnhanceRuntimeTests"`

Observed: `Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10` (TRX `.tmp/wp3-trx/wp3-focused.trx`,
exit 0). The implementer separately ran the whole Enhance family: 49/49.

## 2. Parity controls (clone-only, restored afterwards)

| Control | Mutation of `BonesEnhancePrompts.cs` | Observed |
|---|---|---|
| A — changed prompt | runtime-only `systemPrompt: systemMessage.Content + " "` | parity test **failed 1/1** (TRX `.tmp/wp3-trx/wp3-control-A.trx`), exit 1 |
| B — dropped tool calls | `Tools { get; } = Array.Empty<ToolDefinition>()` | parity test **failed 1/1** (TRX `.tmp/wp3-trx/wp3-control-B.trx`), exit 1 |

Both files were restored (`systemPrompt: systemMessage.Content` observed again). The in-code
sensitivity test (`FindMismatches_GivenAlteredPromptOrResult_ExpectedDifferenceReported`) passes as
part of the 10.

## 3. Full Bones suite (accepted WP3 state)

Observed: **total 433, passed 430, failed 3**. The only failures are the three pre-existing
behavior-proof meta-tests already in the waiver register:

- `BonesLearningLoopReliabilityBehaviorProofComplianceGateTests...`
- `BonesBoardOrientationBehaviorProofComplianceGateTests...`
- `BehaviorProofComplianceGateTests.BehaviorProofCompliance_GivenScriptStrategiesRequirements...`

No unwaived failure. Count reconciliation: 396 declared baseline + 14 WP1 + 13 WP2 + 10 WP3 = 433.
TRX: `.tmp/wp3-trx/wp3-full.trx`. No Runtime code changed in WP3, so the round-2 Runtime result
(147/141/6 documented set) still stands.

## 4. Scope / integrity

- Existing tests were not edited, skipped or weakened; the diff is the new `BonesEnhancePrompts.cs`,
  the modified `BonesEnhanceStrategyAgent.cs` (legacy 13-param ctor preserved) and the new
  `BonesEnhanceRuntimeTests.cs`.
- The co-learning wrapper, prompt builder, host DI, workflow registration, WP1/WP2 files and all
  existing tests have no diff.
- The parity test compares exact domain results, the persisted candidate artifact, the promotion
  decision JSON and the execution JSON, plus the first-turn prompt sequence; the in-code sensitivity
  control proves the comparison detects a changed prompt or a changed execution field.

## 5. Residual notes

- The implementer's shell-exit caveat (PowerShell expanding `$?` in `bash -c`) did not affect this
  verification: all commands here run as bash script files, and the results were read from TRX
  counters/console summaries, not from `$?` markers.
- Count floor remains 396; the real count is now 433 (recorded, not hidden). The waiver file still
  carries the concurrent session's uncommitted Tholdem hunk, so it is not staged here.
