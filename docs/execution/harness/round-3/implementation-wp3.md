# WP3 — Port `BonesEnhanceStrategyAgent` onto the WP1 runtime adapter (co-learning wrapper kept distinct)

- **Round**: 3 (goal WIP-IMPL-001 continuation)
- **Work package**: WP3 — prompts class, `ToolDefinition`s through `RuntimeToolInvoker`, budgets via `AgentLoopOptions`, memory via `IAgentMemory`/`ArtifactAgentMemory`, old-vs-new parity + negative controls
- **Date**: 2026-11-06
- **Repository HEAD**: `3e0087a` (`git rev-parse HEAD` output: `3e0087a140308280a5ad368bf8eaabf650452535`)
- **Result**: PASS — focused `BonesEnhanceRuntimeTests` **10/10**, all Enhance-related tests **49/49**, WP1 adapter **14/14**, WP2 Ponder runtime **13/13**, Release build of `Wip.Bones.Agents` **0 errors**. The unchanged co-learning wrapper is exercised over the new-path single-seat agent and returns the last completed seat (seat 4).
- **Orchestrator-reported (not rerun here, per parent instruction)**: the same files in the isolated clone give focused **10/10**, full Bones suite **433 total / 430 passed / 3 failed** (the three pre-existing waived behavior-proof meta-tests), and both mutation controls (runtime-only prompt change, dropped tool definitions) **fail the parity test as required**.
- **Not done (out of scope)**: provider replacement/host DI (WP4), permissions/Audit safety (WP5), legacy shim removal (WP6), full-suite run (orchestrator), commit.

## 1. Files

| File | Production/test | Lines | Status |
|---|---|---|---|
| `WIP/Wip.Bones.Agents/Enhance/BonesEnhancePrompts.cs` | production | 90 | new |
| `WIP/Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs` | production | 644 → 840 (+204/−8) | modified |
| `WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs` | test | 1113 | new |

Observed `git status --porcelain -- WIP/Wip.Bones.Agents WIP/Wip.Bones.Tests docs/execution/harness` (before this artifact was written):

```text
 M WIP/Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs
?? WIP/Wip.Bones.Agents/Enhance/BonesEnhancePrompts.cs
?? WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs
```

`BonesCoLearningEnhanceAgent.cs`, `BonesEnhancePromptBuilder.cs`, host DI, `BonesLearningWorkflowRegistration.cs`, the WP1 adapter/tools/loop options, the WP2 Ponder files and every existing test have **no diff**. The other entries of the full `git status` (`Wip.slnx`, `docs/plan/test-waivers.json`, `tests/Wip.Architecture.Tests/*`, `tools/run-wip-tests.ps1`, `WIP/Wip.Tholdem*`, `docs/goal-tholdem/`) belong to concurrent work and were not touched.

## 2. Test-first: observed failing run (red)

The test file was written before `BonesEnhancePrompts` or the 15-parameter constructor existed, then the contracted focused command was run (output redirected; same command, same filter):

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesEnhanceRuntimeTests"
```

Observed output (first and last errors of 10; full log `.tmp/wp3-test-first.log`, 58 lines, captured before any WP3 production change):

```text
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs(86,13): error CS1739: The best overload for 'BonesEnhanceStrategyAgent' does not have a parameter named 'memory' [...]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs(277,13): error CS1739: The best overload for 'BonesEnhanceStrategyAgent' does not have a parameter named 'memory' [...]
...
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs(562,13): error CS1739: The best overload for 'BonesEnhanceStrategyAgent' does not have a parameter named 'memory' [...]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs(582,13): error CS0103: The name 'BonesEnhancePrompts' does not exist in the current context [...]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs(612,23): error CS0103: The name 'BonesEnhancePrompts' does not exist in the current context [...]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs(651,13): error CS0103: The name 'BonesEnhancePrompts' does not exist in the current context [...]
```

10 `error CS` lines in total. This is the expected red for the new public API: the test file cannot compile until the prompts class and the 15-parameter constructor exist.

## 3. Build (green)

Command:

```powershell
wsl.exe -d Ubuntu -- bash -c 'if bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh build WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj -c Release > /tmp/wp3-build.log 2>&1; then echo GREEN > /tmp/wp3-build.status; else echo RED > /tmp/wp3-build.status; fi'
```

Observed (`/tmp/wp3-build.status` = `GREEN`; log `.tmp/wp3-build.log`):

```text
Build succeeded.

/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Sandbox.WorkerClient/Wip.Sandbox.WorkerClient.csproj : warning NU1510: PackageReference System.IO.Pipes.AccessControl will not be pruned. ...
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/Enhance/BonesCoLearningEnhanceAgent.cs(71,21): warning CS8602: Dereference of a possibly null reference. ...
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/Enhance/BonesCoLearningEnhanceAgent.cs(88,21): warning CS8602: Dereference of a possibly null reference. ...
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/Knowledge/BonesStrategyPool.cs(945,9): warning CA2264: ...
    5 Warning(s)
    0 Error(s)

Time Elapsed 00:01:30.06
```

All five warnings are pre-existing (`NU1510` in `Wip.Sandbox.WorkerClient`, `CS8602` in the untouched co-learning wrapper, `CA2264` in `BonesStrategyPool`); `Select-String` for `BonesEnhancePrompts.cs|BonesEnhanceStrategyAgent.cs|BonesEnhanceRuntimeTests.cs` over the build log returned **no matches**, i.e. no warning originates from a WP3 file.

## 4. Focused test run (green)

Command (identical filter to the red run; the shell bridge cannot be trusted with `$?`, see §11, so the outcome is a marker):

```powershell
wsl.exe -d Ubuntu -- bash -c 'if bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter FullyQualifiedName~BonesEnhanceRuntimeTests > /tmp/wp3-test-run2.log 2>&1; then echo GREEN > /tmp/wp3-test-run2.status; else echo RED > /tmp/wp3-test-run2.status; fi'
```

Observed (`/tmp/wp3-test-run2.status` = `GREEN`; log `.tmp/wp3-test-run2.log`):

```text
  Wip.Bones.Tests -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/bin/Release/net10.0/Wip.Bones.Tests.dll
Test run for /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/bin/Release/net10.0/Wip.Bones.Tests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 20 s - Wip.Bones.Tests.dll (net10.0)
```

10 = the 10 `[Fact]` methods of `BonesEnhanceRuntimeTests` (no theories, no skips):

| # | Test | Location |
|---|---|---|
| 1 | `ExecuteAsync_GivenSameInputThroughLegacyAndRuntimePaths_ExpectedDeterministicParity` | `BonesEnhanceRuntimeTests.cs:55` |
| 2 | `FindMismatches_GivenAlteredPromptOrResult_ExpectedDifferenceReported` | `:211` |
| 3 | `ExecuteAsync_GivenInvalidScriptThenCompileToolThenValidScript_ExpectedDiagnosticsInToolMessageAndStrategyPersisted` | `:263` |
| 4 | `ExecuteAsync_GivenModelAlwaysCallsCompileTool_ExpectedMaxStepsReachedWithoutStrategyArtifact` | `:325` |
| 5 | `ExecuteAsync_GivenModelAlwaysCallsUnregisteredTool_ExpectedToolFailureLimitReached` | `:368` |
| 6 | `ExecuteAsync_GivenMemorySnapshot_ExpectedMemoryMessageBeforeCurrentConversationAndExchangeAppended` | `:412` |
| 7 | `ExecuteAsync_GivenCoLearningAllSeats_ExpectedFourSeatPromptsAndStrategiesWithLastSeatResultReturned` | `:480` |
| 8 | `ExecuteAsync_GivenCompletedExecutionWithoutFinalMessage_ExpectedInvalidOperationException` | `:552` |
| 9 | `CreateExecutionRequest_GivenSystemUserAssistantMessagesAndMemory_ExpectedMappedRuntimeRequest` | `:592` |
| 10 | `CreateExecutionRequest_GivenUnknownRole_ExpectedInvalidOperationException` | `:642` |

### Iteration evidence (the test caught a real assumption; no assertion weakened)

The first run after implementation was **9 passed / 1 failed** (log `.tmp/wp3-test-run1.log`, 43 lines):

```text
Failed  Wip.Bones.Tests.Runtime.BonesEnhanceRuntimeTests.ExecuteAsync_GivenInvalidScriptThenCompileToolThenValidScript_ExpectedDiagnosticsInToolMessageAndStrategyPersisted [12 s]
  Error Message:
   Assert.Contains() Failure: Sub-string not found
String:    "Strategy script compilation failed: } exp"...
Not found: "error CS"
...
Failed!  - Failed:     1, Passed:     9, Skipped:     0, Total:    10, Duration: 18 s
```

Resolution: the in-process `BonesStrategyScriptHost` (`BonesStrategyScriptHost.cs:271-284`) surfaces `Diagnostic.GetMessage()` (e.g. `} expected`), not the `error CS` id. The test now asserts the **actual** deterministic diagnostic text (`} expected`) instead of the assumed Roslyn prefix (`BonesEnhanceRuntimeTests.cs:307`); production behavior was not changed and no assertion was dropped.

## 5. What was implemented

### 5.1 `BonesEnhancePrompts` (`Enhance/BonesEnhancePrompts.cs`)

| Member | Location | Behaviour |
|---|---|---|
| `internal static class BonesEnhancePrompts` | `:15` | internal, so the tests can pin the mapping contract (`InternalsVisibleTo` in `Wip.Bones.Agents.csproj:11`) |
| `Tools` | `:17-18` | exactly `[BonesAgentToolDefinitions.StrategyCompile]` |
| `CreateExecutionRequest(...)` | `:20-66` | validates roles first (`:33-38`), system prompt = the typed `"system"` message (`:40-41`, `:62`), non-system messages split into task = last (`:60`) and initial messages = the rest (`:55-56`); optional memory messages are inserted **after the system prompt and before the conversation** (`:49-53`); tools = the compile tool (`:63`); options = the passed `AgentLoopOptions` (`:65`) |
| `ReadCompletedMarkdown(...)` | `:68-76` | a completed execution with `FinalMessage == null` throws `InvalidOperationException("The agent runtime completed without a final assistant message.")` (`:71-74`) — never an empty answer |
| `ToChatMessage(...)` | `:82-90` | maps `user`/`assistant`; any other role throws `InvalidOperationException` (`:87-88`) |

The prompt **text** is not duplicated: the agent still builds messages with `BonesEnhancePromptBuilder.BuildEnhancementRequest` / `AppendCompileRetryMessage` (untouched, single source of truth); `BonesEnhancePrompts` only converts them to the runtime request.

### 5.2 `BonesEnhanceStrategyAgent` (`Enhance/BonesEnhanceStrategyAgent.cs`)

| Member | Location | Behaviour |
|---|---|---|
| fields | `:34-50` | nullable legacy `_modelProvider`; optional adapter, memory and loop options |
| legacy ctor (13 parameters, identical signature and order, all original defaults) | `:51-88` | delegates to the private ctor with `executor: null`; keeps calling the enhancement provider directly |
| production ctor (15 parameters) | `:91-124` | `(IArtifactStore, IAgentExecutor, BonesStrategyScriptHost?, BonesStrategyPromotionEvaluator?, BonesStrategyPromotionOptions?, BonesCompileRetryOptions?, BonesAggressiveIterationOptions?, BonesGameSimulationBudget?, BonesStrategyLibrary?, int?, IModelProvider<BonesPlayTurnRequest,BonesPlayTurnResult>?, string?, ILogger?, IAgentMemory?, BonesAgentLoopOptions?)`; `executor` has **no default** (`:93`), memory/loop options do (`:113-114`) |
| private ctor + adapter composition | `:127-177` | `BonesAgentAdapter<EnhanceModelTurnRequest, EnhanceModelTurnResult>` over `BonesEnhancePrompts.CreateExecutionRequest` -> `ReadCompletedMarkdown` (`:164-176`) |
| memory snapshot (loaded once, before the first model turn) | `:220` -> `:728-736` | `IAgentMemory.LoadAsync`; empty when no memory |
| memory included in every turn | `:288-294` -> `:773-806` | each attempt passes the same `memoryMessages` into the per-turn adapter request |
| memory append after a successful result | `:540-544` -> `:739-770` | appends the final non-system user prompt then the final assistant markdown once; redaction stays the memory implementation's job |
| single model-call seam | `:288-294` -> `:773-806` | adapter/executor when present, else the legacy provider with the same correlation id `{correlationId}-iter-{iteration}-attempt-{attempt}`; compile/retry/persist logic is not forked |
| preserved behaviour | see table below | prior strategy load, match histories, opponents, versioning, F2 iterations, F3 stagnation/temperature ramp, compile retries, failure artifact, promotion evaluation, promotion/rejection persistence, library upsert, execution log, result fields, logger calls |

Preserved-logic anchors (unchanged bodies, only the model call was replaced by the seam):

| Behaviour | Location |
|---|---|
| prior active strategy + document | `:196-198` |
| all match histories / opponent strategies | `:202-216` |
| F2 `EnhanceIterationsPerMatch` loop | `:222-223`, loop `:239` |
| enhancement prompt build | `:247-255` |
| F3 stagnation detection / temperature ramp / `BonesStagnationException` | `:321-362`, `:395-410`, `:620-634` |
| compile retry prompts and terminal compile failure | `:362-389`, failure artifact `:303`/`:376` -> `:692-726` |
| promotion evaluation (same evaluator/seed/options) | `:439-458` |
| promoted/rejected persistence + early exit | `:458-500` |
| promotion-decision artifact / execution log | `:506-518` -> `:636-655`, `:657-690` |
| library upsert | `:565-590` |
| logger calls (`LogWarning`/`LogError`) | `:334`, `:360`, `:409`, `:492`, `:586` |
| per-turn records | `:811-816` |

No `WipBuilder.AddTool` was added; the compile tool remains only an agent-loop `ToolDefinition`/`ICapabilityDescriptor`:

```text
NO WipBuilder.AddTool CALL in 2 production files
```

## 6. Parity comparison method and failure modes

Fixture and comparison (test `BonesEnhanceRuntimeTests.cs:55`, helpers `:688-824`, fixture `:826-931`):

1. Two **separate temp repositories** with the **same** `SessionId` (`parity-session-wp3`), same player (seat 2), same prior active script (`initial-seat-2-v1` = `FirstLegalMoveScriptSource`), same four seat strategies, same two match histories (`parity-match-a`, `parity-match-b`), same promotion options (`EvaluationMatchCount = 5`) and the same explicit `EvaluationSeed = 9001` (`:57-91`).
2. **Old path**: 13-parameter ctor + a hand-written `IModelProvider<BonesStrategyEnhancementRequest,BonesStrategyEnhancementResult>` returning ``` ```csharp\n{prior source}\n``` ``` (`:74-77`, fake `:1002-1029`).
3. **New path**: 15-parameter ctor + real `AgentExecutor` + real `RuntimeToolInvoker` over `BonesAgentToolDescriptors.Create()` (`RuntimeTooling`, `:933-955`) + fake `IChatModel` returning the **same markdown** on its single turn (`:79-85`).
4. **Compared** (`EnhanceParitySnapshot.Create`/`FindMismatches`, `:688-824`, asserted empty at `:207`): `PriorStrategyId`, `StrategyId`, `StrategyDocument` (markdown + player seat), strategy artifact descriptor id/kind/producer type/version/relative path, the persisted candidate strategy source/kind/promotion status read back from each store, `MatchHistoriesLoaded`, `PromotionOutcome`, `ActiveStrategyId`, `StagnationDetected`, `EnhanceIterationsCompleted`, the **promotion-decision artifact JSON** and the **execution artifact JSON** read back from each store, and the **first-turn prompt sequence** (role + content) from `ModelProviderRequest.Payload.Messages` vs `ChatRequest.Messages`.
5. Non-deterministic fields are deliberately excluded: GUID artifact ids, `ProducedAtUtc` timestamps and the store root (the compared `RelativePath` is repository-relative and deterministic).
6. Exact literals pinned in the same test: `initial-seat-2-v1` -> `initial-seat-2-v2`, `MatchHistoriesLoaded == 2`, `Rejected`, active id unchanged, `StagnationDetected == 0`, `EnhanceIterationsCompleted == 1`; execution JSON `PlayerSeat=2`, `MatchGameId=parity-match-b`, `ModelId=bones-strategy-enhance`, `CompileAttempts=1`, `PriorStage=play`, `PromotionOutcome=Rejected`; promotion JSON `EvaluationMatchCount=5`, `CandidateWins == IncumbentWins`, `ScoreDifferentialDelta == 0.0` (candidate source identical to the incumbent with paired seeds); runtime `ChatRequest.ModelId` and the single sent tool `tool.bones.strategy-compile`.

**Failure modes**: a changed system/user/assistant string, role or message count; a changed strategy id/document/artifact or execution-log field; a different number of prompt turns; or the agent dropping the compile tool from the request all make the parity test fail. The helper reports the exact field, e.g. `Prompts[1].Content: expected '...' but was '...'` or `ExecutionArtifactJson: expected ...`. The **orchestrator-reported** mutation controls (runtime-only prompt change; dropped tool definitions) fail the parity test as required — the prompt change breaks the `ChatRequest`/legacy equality and the snapshot comparison, while dropping the tool breaks the explicit `Assert.Single(runtimeRequest.Tools)`/tool-name assertion in the same test (inference about which assertion fires ❓; the mutation itself was run by the orchestrator, not here).

## 7. Negative controls and observed results

All observed in the green focused run (`10/10`, §4):

| Control | Where | Deliberate failure | Observed result |
|---|---|---|---|
| Parity sensitivity (in-code) | `:211` | one prompt character changed; one execution-JSON field changed | identical snapshots -> `Assert.Empty(mismatches)`; altered prompt -> exactly one mismatch containing `Prompts[1].Content` and the altered text; altered result -> exactly one mismatch containing `ExecutionArtifactJson`. **PASS** |
| Tool routing positive | `:263` | invalid script through the compile tool on turn 1, valid final markdown on turn 2 | second `ChatRequest` has 4 messages (`System`, `User`, `Assistant` with one `tool.bones.strategy-compile` call, `Tool` with the call id); the tool JSON has `succeeded:false` and a `failureReason` containing `Strategy script compilation failed` and `} expected`; strategy `initial-seat-1-v2` is persisted with the valid source and `Rejected` status. **PASS** |
| Max steps | `:325` | model always returns a compile tool call; `BonesAgentLoopOptions { MaxSteps = 2 }` | `BonesAgentExecutionException` with `Status == MaxStepsReached`, `Steps.Count == 2` (each step one tool result), message `Max steps (2) reached without a final answer.`, model called twice, **no** `bones-strategy-initial-seat-3-v2` artifact. **PASS** |
| Tool failure limit | `:368` | model always calls unregistered `tool.bones.unregistered`; `MaxConsecutiveToolFailures = 3` | `BonesAgentExecutionException` with `Status == ToolFailureLimitReached`, `Steps.Count == 3`, last tool result `IsError == true` and content `unknown-tool: no tool named 'tool.bones.unregistered' is registered.`, message contains `Tool failure limit (3) reached.`, no candidate artifact. **PASS** |
| Memory | `:412` | pre-seeded `ArtifactAgentMemory` over the fixture's real `WipArtifactStoreLocal` | first `ChatRequest` = `[System, pre-seeded User, current User prompt]` (memory **after** the system prompt, before the conversation); after the run the memory snapshot is `[pre-seed, new user with "# Strategy script enhancement request", assistant == the exact final markdown]`; the artifact list contains the `ArtifactAgentMemory`-produced `.wip/sessions/memory-wp3/artifacts/agent-memory-agent-memory-*.json`. **PASS** |
| Null `FinalMessage` | `:552` | fake `IAgentExecutor` returns `Completed` with `FinalMessage: null` | exact `InvalidOperationException` message `The agent runtime completed without a final assistant message.`; executor called once; no candidate artifact; the direct `BonesEnhancePrompts.ReadCompletedMarkdown` call throws the same message. **PASS** |
| Unknown role | `:642` | message role `tool` | exact `InvalidOperationException` message `Unsupported Bones enhance prompt role 'tool'.` **PASS** |
| Co-learning distinct | `:480` | unchanged `BonesCoLearningEnhanceAgent` over the new-path single-seat agent, real `StrategyPool`, `AllSeatsLearning = true` | 4 `ChatRequest`s, each with its own `Player seat: {1..4}` and `Prior strategy id: initial-seat-{1..4}-v1`; 4 persisted candidate artifacts (`initial-seat-{1..4}-v2`, `Rejected`, source == valid script); 4 pool entries (one per seat); the wrapper returns the **last completed** seat result (seat 4: prior `initial-seat-4-v1`, strategy `initial-seat-4-v2`, document player seat 4, active id unchanged). **PASS** |

## 8. Existing behaviour / no regression

Commands (same `if`-marker pattern; logs under `.tmp/`):

```powershell
wsl.exe -d Ubuntu -- bash -c 'if bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter FullyQualifiedName~BonesEnhance > /tmp/wp3-existing-enhance.log 2>&1; then echo GREEN > ...; else echo RED > ...; fi'
wsl.exe -d Ubuntu -- bash -c 'if bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter FullyQualifiedName~BonesPonderRuntimeTests > /tmp/wp3-ponder.log 2>&1; then echo GREEN > ...; else echo RED > ...; fi'
wsl.exe -d Ubuntu -- bash -c 'if bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter FullyQualifiedName~BonesAgentAdapterTests > /tmp/wp3-adapter.log 2>&1; then echo GREEN > ...; else echo RED > ...; fi'
```

Observed:

```text
# BonesEnhance filter: GREEN, log .tmp/wp3-existing-enhance.log
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49, Duration: 33 s - Wip.Bones.Tests.dll (net10.0)

# BonesPonderRuntimeTests: GREEN, log .tmp/wp3-ponder.log
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 15 s - Wip.Bones.Tests.dll (net10.0)

# BonesAgentAdapterTests: GREEN, log .tmp/wp3-adapter.log
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 536 ms - Wip.Bones.Tests.dll (net10.0)
```

`49 = 39 pre-existing Enhance tests (10 in `BonesEnhanceStrategyAgentTests` before `:918`, 13 in `BonesEnhancePromptBuilderTests`, 16 in `BonesEnhanceAggressiveFeaturesTests`) + the 10 new tests`. The 13-parameter constructor is still the only constructor resolvable without `IAgentExecutor`; the existing dispatch test (`BonesEnhanceStrategyAgentTests.cs:294-404`, DI with the provider only) passes, and no existing test file was edited.

## 9. No stub / TODO

Command:

```powershell
$files = @('WIP/Wip.Bones.Agents/Enhance/BonesEnhancePrompts.cs','WIP/Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs')
$hits = Select-String -Path $files -Pattern 'TODO','FIXME','stub','NotImplementedException','NotSupportedException' -SimpleMatch
if ($null -eq $hits) { "NO STUB/TODO MATCHES in $($files.Count) production files" } else { $hits }
```

Observed:

```text
NO STUB/TODO MATCHES in 2 production files
```

The test file contains `NotSupportedException` throws only inside the hand-written `IChatModel.StreamAsync` fakes (the runtime path never streams) — test-only code, not production stubs.

Encoding (UTF-8 without BOM; first bytes `117,115,105` = `usi`) for all three WP3 files:

```text
WIP/Wip.Bones.Agents/Enhance/BonesEnhancePrompts.cs first3bytes=117,115,105
WIP/Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs first3bytes=117,115,105
WIP/Wip.Bones.Tests/Runtime/BonesEnhanceRuntimeTests.cs first3bytes=117,115,105
```

## 10. Scope, integrity and evidence limits

- **No existing test assertion was edited, skipped, deleted or weakened.** Only the three WP3 files exist as a diff; `BonesCoLearningEnhanceAgent`, `BonesEnhancePromptBuilder`, host DI, workflow registration, WP1/WP2 files and `docs/goal-tholdem/` are untouched.
- **No `WipBuilder.AddTool` call** was introduced (§5.2).
- **Only focused/project-scoped runs were made here**: `BonesEnhanceRuntimeTests`, `BonesEnhance*`, `BonesPonderRuntimeTests`, `BonesAgentAdapterTests`, plus a project-scoped Release build. The full Bones suite status is **orchestrator-reported only** (§header), not claimed by this artifact.
- **The co-learning wrapper is not modified**: test 7 runs the unchanged `BonesCoLearningEnhanceAgent` over the new-path single-seat agent and pins the four-seat distinct behaviour.
- ❓ Shell-status caveat: the local `pwsh` -> WSL bridge expands `$?` before bash. Probe observed: `wsl.exe -d Ubuntu -- bash -c 'false; echo PROBE=$?' > file` wrote `PROBE=0` instead of `1`. All status markers after that discovery use `if ...; then echo GREEN; else echo RED; fi`; the pasted `GREEN` markers are those files. The red run in §2 is evidenced by its pasted compiler errors (captured before implementation), not by an exit code.
- ❓ The parity decision metrics (`CandidateWins == IncumbentWins`, `ScoreDifferentialDelta == 0.0`) follow from the candidate being byte-identical to the incumbent under paired seeds; the test asserts the observed literals, not the simulation internals.
- **No commit was made.**
