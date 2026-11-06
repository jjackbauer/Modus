# Round 2 — Orchestrator verification of WP2 (Ponder on the runtime)

- **Round**: 2 (goal WIP-IMPL-001 continuation, D2)
- **WP2 deliverable**: `docs/execution/harness/round-2/implementation-wp2.md`
- **Starting HEAD**: `dad2a72` (WP1)
- **Verification environment**: isolated ext4 clone `/home/ricardo/verify` at `dad2a72` with the
  accepted WP2 files overlaid (so concurrent Windows builds cannot lock this build), .NET SDK
  10.0.103, `LANG=en_US.UTF-8`, sandbox worker prebuilt in Release.
- **Result**: PASS — focused 13/13, full Bones 423/420 with only the 3 waived meta-tests failing,
  full Runtime 147/141 with only the 5 Windows-only + 1 waived failures, both parity controls fail
  as required.

## 1. Focused reproduction

`dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesPonderRuntimeTests"`

Observed: `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13, Duration: 11 s` (TRX
`.tmp/wp2-trx/wp2-focused.trx`, exit 0). This independently reproduces the implementer's 13/13.

## 2. Parity controls (clone-only, restored afterwards)

The parity test compares the legacy `IModelProvider` path against the real `AgentExecutor` +
`RuntimeToolInvoker` path for the same session/seat/observations: strategy id/document/artifact,
execution-artifact JSON, and the first-turn prompt sequence. Two mutations of `BonesPonderPrompts.cs`
were applied in the clone and reverted:

| Control | Mutation | Observed |
|---|---|---|
| A — changed prompt | runtime-only `systemPrompt: systemMessage.Content + " "` | parity test **failed 1/1** (`Failed! - Failed: 1, Passed: 0`, TRX `.tmp/wp2-trx/wp2-control-A.trx`), exit 1 |
| B — dropped tool calls | `Tools { get; } = Array.Empty<ToolDefinition>()` | parity test **failed 1/1** (`Failed! - Failed: 1, Passed: 0`, TRX `.tmp/wp2-trx/wp2-control-B.trx`), exit 1 |

After both controls the file was restored (`systemPrompt: systemMessage.Content` observed again).
The in-code sensitivity test (`FindMismatches_GivenAlteredPromptOrResult_ExpectedDifferenceReported`)
also passes as part of the 13.

## 3. Full Bones suite (accepted WP2 state)

Observed: **total 423, passed 420, failed 3**. The only failures are the three pre-existing
behavior-proof meta-tests that are already in `docs/plan/test-waivers.json`:

- `BonesLearningLoopReliabilityBehaviorProofComplianceGateTests...`
- `BonesBoardOrientationBehaviorProofComplianceGateTests...`
- `BehaviorProofComplianceGateTests.BehaviorProofCompliance_GivenScriptStrategiesRequirements...`

No unwaived failure. Count reconciliation: the declared baseline is 396; WP1 added 14 adapter tests
(410) and WP2 added 13 Ponder-runtime tests (423). The waiver total stays 396 as a floor; the real
discovered count is recorded here (it is not a failure and not raised to hide one).
TRX: `.tmp/wp2-trx/wp2-final-bones.trx`.

## 4. Full Runtime suite (memory fix)

Observed: **total 147, passed 141, failed 6**. The six failures are exactly the pre-existing set:
5 Windows-only `WipRuntimeValidationReportTests` that execute `dotnet.cmd`, plus the waived
`DeepSeekModelProviderTests.DeepSeekProvider_GivenCancellationRequested...`. 147 = the declared 146
+ the one new local-store memory regression test; the new test passes.
TRX: `.tmp/wp2-trx/wp2-final-runtime.trx`.

## 5. Memory defect found and fixed inside WP2

The implementer found that `ArtifactAgentMemory.LoadAsync` only matched an exact
`agent-memory.json` relative path, while `WipArtifactStoreLocal` writes
`agent-memory-{artifactId}.json`, so production memory never reloaded. The fix accepts both names
with the producer-type guard; the regression test and the Bones memory test now use the real local
store. The full Runtime and Bones runs above confirm no regression (the existing redaction test still
passes).

## 6. Scope / integrity

- Existing tests were not edited, skipped or weakened. The only existing-test file changed is
  `ArtifactAgentMemoryTests.cs` (one new `[Fact]` plus an opt-in store-name mode); the assertions of
  the existing six tests are unchanged.
- The co-learning agent, host DI, workflow registration, WP1 adapter and prompt builder have no diff.
- No commit was made by the implementers. Work-package files for the commit: the 3 new Runtime/Ponder
  production files, the modified Ponder agent, the new Ponder-runtime test, the memory fix + its test.

## 7. Residual findings

- **DI ambiguity**: when both `IModelProvider` and `IAgentExecutor` are registered, Microsoft DI
  throws an ambiguous-constructor error (the 6-param ctor does not list the provider type). WP4 must
  retire the provider registrations before/at the moment it registers the executor; the supported
  end state is executor-only. Recorded as RISK-007.
- **Counts**: the real Bones count is 423; the waiver floor is 396. If the concurrent session's
  waiver edit lands first, a later commit can raise the floor to 423 with this TRX as evidence.
