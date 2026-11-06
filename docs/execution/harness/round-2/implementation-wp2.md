# WP2 — Port `BonesPonderAgent` (single-seat + co-learning) onto the WP1 runtime adapter

- **Round**: 2 (goal WIP-IMPL-001 continuation)
- **Work package**: WP2 — prompts class, `ToolDefinition`s through `RuntimeToolInvoker`, budgets via `AgentLoopOptions`, memory via `IAgentMemory`/`ArtifactAgentMemory`, old-vs-new parity + controls
- **Date**: 2026-11-06
- **Repository HEAD**: `dad2a72` (`git rev-parse HEAD` output: `dad2a72c12e8ed41e92d633652c35172e8b532a1`)
- **Result**: PASS — focused Bone tests **13/13**, Release build of `Wip.Bones.Agents` **0 errors**, and the memory store fix adds **7/7** `ArtifactAgentMemoryTests` (see §11). The co-learning agent is untouched; no host DI, workflow registration, WP1 adapter, `docs/` (other than this file) or commit changes.
- **Not done (out of scope / deferred)**: Enhance (WP3), DeepSeek-provider retirement + host DI (WP4), permissions/Audit safety tests (WP5), legacy shim removal (WP6), full-suite run (orchestrator), commit.

## 1. Files

| File | Production/test | Lines | Status |
|---|---|---|---|
| `WIP/Wip.Bones.Agents/Runtime/BonesAgentLoopOptions.cs` | production | 28 | new |
| `WIP/Wip.Bones.Agents/Runtime/BonesAgentTools.cs` | production | 98 | new |
| `WIP/Wip.Bones.Agents/Ponder/BonesPonderPrompts.cs` | production | 77 | new |
| `WIP/Wip.Bones.Agents/Ponder/BonesPonderAgent.cs` | production | 496 (was 341) | modified |
| `WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs` | test | 1113 | new |
| `WIP/Wip.Runtime/Runtime/ArtifactAgentMemory.cs` | production | 154 (was 137) | modified (§11) |
| `WIP/Wip.Runtime.Tests/Memory/ArtifactAgentMemoryTests.cs` | test | 185 (was 148) | modified — one regression test added (§11) |

Observed `git status` (touched paths only; other paths in the shared tree belong to concurrent work):

```text
 M WIP/Wip.Bones.Agents/Ponder/BonesPonderAgent.cs
 M WIP/Wip.Runtime.Tests/Memory/ArtifactAgentMemoryTests.cs
 M WIP/Wip.Runtime/Runtime/ArtifactAgentMemory.cs
?? WIP/Wip.Bones.Agents/Ponder/BonesPonderPrompts.cs
?? WIP/Wip.Bones.Agents/Runtime/BonesAgentLoopOptions.cs
?? WIP/Wip.Bones.Agents/Runtime/BonesAgentTools.cs
?? WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs
?? docs/execution/harness/round-2/
```

`BonesCoLearningPonderAgent.cs`, `BonesLearningWorkflowRegistration.cs`, `BonesPonderPromptBuilder.cs`, every existing test assertion (the only test-file change is the added memory regression test in §11), the WP1 adapter and host DI have **no diff**.

## 2. Test-first: observed failing run (red)

The test file was written before any WP2 production type existed, then the focused command was run.

Command (PowerShell):

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesPonderRuntimeTests"
```

Observed output (first errors; compilation stops the test run; full log `.tmp/wp2-test-first.log`):

```text
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs(78,17): error CS0246: The type or namespace name 'BonesAgentLoopOptions' could not be found (are you missing a using directive or an assembly reference?) [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs(77,13): error CS1739: The best overload for 'BonesPonderAgent' does not have a parameter named 'memory' [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs(239,17): error CS0246: The type or namespace name 'BonesAgentLoopOptions' could not be found ... [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj]
...
... Wip.Bones.Tests.csproj : error CS0103: The name 'BonesAgentToolIds' does not exist in the current context ...
EXIT=1
```

This is the expected red for a new public API: the test file cannot compile until the runtime types and the 6-parameter constructor exist.

## 3. Build (green)

Command:

```powershell
wsl.exe -d Ubuntu -- bash -c 'bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh build WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj -c Release > /tmp/wp2-build.log 2>&1; echo EXIT=$?; tail -n 8 /tmp/wp2-build.log'
```

Observed output:

```text
EXIT=0
Build succeeded.

/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Sandbox.WorkerClient/Wip.Sandbox.WorkerClient.csproj : warning NU1510: PackageReference System.IO.Pipes.AccessControl will not be pruned. Consider removing this package from your dependencies, as it is likely unnecessary. [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Sandbox.WorkerClient/Wip.Sandbox.WorkerClient.csproj : warning NU1510: PackageReference System.IO.Pipes.AccessControl will not be pruned. Consider removing this package from your dependencies, as it is likely unnecessary.
    2 Warning(s)
    0 Error(s)

Time Elapsed 00:01:29.89
```

Both warnings are pre-existing `NU1510` in `Wip.Sandbox.WorkerClient` (outside WP2); no warning originates from a WP2 file.

## 4. Focused test run (green)

Command (same filter as the red run; explicit exit code captured):

```powershell
wsl.exe -d Ubuntu -- bash -c 'bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesPonderRuntimeTests" > /tmp/wp2-test-run4.log 2>&1; echo EXIT=$?; tail -n 6 /tmp/wp2-test-run4.log'
```

Observed output:

```text
EXIT=0
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 13 s - Wip.Bones.Tests.dll (net10.0)
```

13 = 13 `[Fact]` methods (no theories, no skips/filters inside the class):

| # | Test | Location |
|---|---|---|
| 1 | `ExecuteAsync_GivenSameInputThroughLegacyAndRuntimePaths_ExpectedDeterministicParity` | `BonesPonderRuntimeTests.cs:55` |
| 2 | `FindMismatches_GivenAlteredPromptOrResult_ExpectedDifferenceReported` | `:185` |
| 3 | `ExecuteAsync_GivenInvalidScriptThenCompileToolThenValidScript_ExpectedDiagnosticsInToolMessageAndStrategyPersisted` | `:233` |
| 4 | `ExecuteAsync_GivenModelAlwaysCallsCompileTool_ExpectedMaxStepsReachedWithoutStrategyArtifact` | `:299` |
| 5 | `ExecuteAsync_GivenModelAlwaysCallsUnregisteredTool_ExpectedToolFailureLimitReached` | `:338` |
| 6 | `ExecuteAsync_GivenMemorySnapshot_ExpectedMemoryMessageBeforeCurrentConversationAndExchangeAppended` | `:381` |
| 7 | `ExecuteAsync_GivenCoLearningAllSeatsAndEmptyLibrary_ExpectedFourSeatPromptsAndStrategiesWithFirstSeatResultReturned` | `:442` |
| 8 | `ExecuteAsync_GivenCompletedExecutionWithoutFinalMessage_ExpectedInvalidOperationException` | `:514` |
| 9 | `ToAgentLoopOptions_GivenDefaults_ExpectedRuntimeBudgetValues` | `:545` |
| 10 | `ToAgentLoopOptions_GivenCustomValues_ExpectedExactMapping` | `:562` |
| 11 | `BonesAgentToolDefinitionsAndDescriptors_GivenDefaults_ExpectedCompileCapabilityContract` | `:581` |
| 12 | `ConstructorSelection_GivenOnlyModelProviderRegistered_ExpectedLegacyProviderPath` | `:617` |
| 13 | `ConstructorSelection_GivenExecutorRegistered_ExpectedRuntimeExecutorPath` | `:642` |

### Iteration evidence (tests caught real problems; no assertion weakened)

An intermediate run after the production code existed produced **3 failures / 10 passed** (full log `.tmp/wp2-test-run1.log`):

```text
Failed ... ExecuteAsync_GivenSameInputThroughLegacyAndRuntimePaths_ExpectedDeterministicParity [10 s]
  Assert.Equal() Failure: Strings differ   (my literal expected the scoped file name; the local store's RelativePath is `.wip/sessions/{session}/artifacts/...`)
Failed ... ConstructorSelection_GivenExecutorRegistered_ExpectedRuntimeExecutorPath [54 ms]
  System.InvalidOperationException : Unable to activate type 'Wip.Bones.Agents.Ponder.BonesPonderAgent'. The following constructors are ambiguous: ...
Failed ... ExecuteAsync_GivenMemorySnapshot_ExpectedMemoryMessageBeforeCurrentConversationAndExchangeAppended [333 ms]
  Assert.Equal() Failure: Values differ  Expected: 3  Actual:   2
Failed!  - Failed:     3, Passed:    10, Skipped:     0, Total:    13
```

Resolutions (none weakens the parity guarantee):
- The artifact-path assertion now asserts the **exact** deterministic prefix `.wip/sessions/parity-session-wp2/artifacts/` plus the scoped file name (`:116`, `:120`); the old-vs-new equality assertion for `RelativePath` was already passing.
- The memory failure was a real production gap: `ArtifactAgentMemory` discovered memory only by `RelativePath.EndsWith("agent-memory.json")`, while `WipArtifactStoreLocal` normalizes the file name to `{fileName}-{artifactId}.json`, so the pre-seeded memory was never loaded (observed: first request had 2 messages instead of 3). The memory test now uses the **real `WipArtifactStoreLocal`** and the discovery predicate was fixed; see §11 for the fix, the new regression test and the observed reruns.
- The DI test now covers the two real registrations (provider-only and executor-only) instead of registering both; see §8 for the observed Microsoft DI rule.

## 5. What was implemented

### 5.1 `BonesAgentLoopOptions` (`Runtime/BonesAgentLoopOptions.cs`)

| Member | Location | Value/behaviour |
|---|---|---|
| `public sealed class BonesAgentLoopOptions` | `:10` | Bones-side budget options |
| `MaxSteps` | `:12` | default `3` |
| `MaxTotalTokens` | `:14` | `int?`, default `null` |
| `Timeout` | `:16` | `TimeSpan`, default 2 minutes |
| `MaxConsecutiveToolFailures` | `:18` | default `2` |
| `ToAgentLoopOptions()` | `:20-28` | maps all four values into `Wip.Abstractions.Agents.AgentLoopOptions` |

### 5.2 `BonesAgentTools` (`Runtime/BonesAgentTools.cs`)

| Member | Location | Behaviour |
|---|---|---|
| `BonesAgentToolIds.StrategyCompile` | `:11-13` | `new CapabilityId("tool.bones.strategy-compile")` |
| `BonesStrategyCompileToolRequest(string ScriptSource)` | `:16` | request record |
| `BonesStrategyCompileToolResult(bool Succeeded, string? FailureReason)` | `:18` | result record |
| `BonesStrategyCompileTool : ITool<...>` | `:25-44` | takes `BonesStrategyScriptHost`; `ExecuteAsync` (`:36-43`) calls `TryCompile(new BonesStrategyId("tool-strategy-compile"), request.ScriptSource)` and maps the compile result. It never calls a model. |
| `BonesAgentToolDefinitions.StrategyCompile` | `:49-74` | `ToolDefinition` named exactly `tool.bones.strategy-compile`; description contains "Compiles a Bones C# strategy script ... returns compiler diagnostics"; schema `{type:"object",properties:{scriptSource:{type:"string",description:...}},required:["scriptSource"],additionalProperties:false}` (`:52-73`) |
| `BonesAgentToolDescriptors.Create(...)` | `:76-97` | one `CapabilityDescriptor<BonesStrategyCompileToolRequest, BonesStrategyCompileToolResult>` with kind `Tool`, capability type `typeof(BonesStrategyCompileTool)`; optional `requiredPermissions`/`pluginOrigin` flow through (defaults: empty permissions, null plugin origin). `WipBuilder.AddTool` is not called anywhere. |

### 5.3 `BonesPonderPrompts` (`Ponder/BonesPonderPrompts.cs`)

| Member | Location | Behaviour |
|---|---|---|
| `Tools` | `:16-17` | the Ponder tool definitions (`[BonesAgentToolDefinitions.StrategyCompile]`) |
| `CreateExecutionRequest(...)` | `:19-56` | system prompt = the typed `"system"` message (`:30-31`, `:52`); non-system messages split into task = last (`:50`) and initial messages = the rest (`:44-46`); optional memory messages inserted after the system prompt and before the conversation (`:41-42`); tools = Ponder tools (`:53`); options = the passed loop options (`:55`) |
| `ReadCompletedMarkdown(...)` | `:58-67` | a completed execution with `FinalMessage == null` throws `InvalidOperationException("The agent runtime completed without a final assistant message.")` (`:62-64`) — never an empty answer |
| `ToChatMessage(...)` | `:69-76` | maps `user`/`assistant`; any other role throws `InvalidOperationException` (`:74-75`) |

The prompt **text** is not duplicated: the agent still builds messages with `BonesPonderPromptBuilder.BuildAuthoringRequest` and `AppendCompileRetryMessage` (the existing single source of truth), and `BonesPonderPrompts` only converts them to the runtime request. Existing `BonesPonderPromptBuilderTests` therefore still exercise the same strings.

### 5.4 `BonesPonderAgent` (`Ponder/BonesPonderAgent.cs`)

| Member | Location | Behaviour |
|---|---|---|
| fields | `:22-28` | nullable legacy `_modelProvider`; optional adapter, memory and loop options |
| legacy ctor (4 params, unchanged signature) | `:35-49` | delegates to the private ctor with `executor: null`; keeps calling the provider directly for existing tests/host wiring |
| production ctor (6 params) | `:57-73` | `(IArtifactStore, IAgentExecutor, BonesStrategyScriptHost? = null, BonesCompileRetryOptions? = null, IAgentMemory? = null, BonesAgentLoopOptions? = null)`; executor parameter has **no default** |
| private ctor + adapter composition | `:75-103` | `BonesAgentAdapter<PonderModelTurnRequest, PonderModelTurnResult>` over `BonesPonderPrompts.CreateExecutionRequest` (`:91-102`) |
| memory load (once, before the first model turn) | `:145` → `:265-274` | `IAgentMemory.LoadAsync`; empty when no memory |
| single model-call seam | `:158-164` → `:310-337` | dispatches to the adapter/executor when present, else the legacy provider; compile/retry/persist logic is not forked |
| memory append after a successful persisted result | `:223-227` → `:276-303` | appends final user prompt then final assistant markdown; redaction remains the memory implementation's job |
| preserved existing behaviour | `:115-264` | bootstrap skip (`:120-129`), observation loading (`:131-141`), compile-retry loop + retry prompts (`:153-260`), failure artifact, execution log, exception text, strategy persistence, `DefaultModelId` (`:20`) are unchanged |

Per-turn runtime records: `PonderModelTurnRequest` (`:466-470`) and `PonderModelTurnResult` (`:471`). `ChatMessage` memory messages are carried per turn (no shared mutable state), so concurrent executions of the singleton agent cannot leak memory between seats.

## 6. Parity comparison method and failure modes

Fixture and comparison (test `BonesPonderRuntimeTests.cs:55`, helpers `:674` and `:753`):

1. Two **separate temp repositories** (`PonderFixture`, `:753`) with the **same** `SessionId` (`parity-session-wp2`), same player (seat 2) and same observation markdown (`# Observation ... 6-4 to 5-4 ...`).
2. **Old path**: `new BonesPonderAgent(store, SingleAnswerModelProvider)` (legacy 4-param ctor) whose fake `IModelProvider` returns ``` ```csharp\n{ValidScript}\n``` ``` in one call.
3. **New path**: `new BonesPonderAgent(store, executor, host, retryOptions, memory: null, loopOptions)` (6-param ctor) with a **real `AgentExecutor`** over a fake `IChatModel`, a **real `RuntimeToolInvoker`** over `BonesAgentToolDescriptors.Create()`, and a real `BonesStrategyScriptHost` (`RuntimeTooling`, `:821`). Turn 1 returns a `ToolCall` to `tool.bones.strategy-compile` with the valid script; turn 2 returns the **same markdown** as the final answer. The fake model (`ToolRouteChatModel`, `:909`) refuses to proceed with `TOOL_DEFINITION_MISSING` if `ChatRequest.Tools` does not contain the compile tool, and returns `TOOL_ROUTE_FAILED` unless the first tool observation parsed as `succeeded: true`. The test also asserts the first-turn tool list and the tool observation (`:160-169`).
4. **Compared** (`PonderParitySnapshot.Create`/`FindMismatches`, `:674`): `StrategyId`, `StrategyDocument`, `AuthoredStrategy`, `ObservationsLoaded`; strategy artifact `ArtifactId`/`Kind`/`ProducerType`/`ProducerVersion`/`RelativePath`; the execution artifact **JSON content** read back from each store (all fields equal; observed exact string equality); and the **first-turn** prompt sequence (role + content) from `ModelProviderRequest.Payload.Messages` vs `ChatRequest.Messages`. Non-deterministic fields are deliberately excluded: the execution artifact GUID/`ProducedAtUtc`, tool-call artifacts (`RecordArtifacts = false`), and the store root path.
5. The comparison helper returns a list of human-readable mismatches; the parity test asserts **empty**. The same helper is proven sensitive by `FindMismatches_GivenAlteredPromptOrResult_ExpectedDifferenceReported` (`:185`).

**Failure modes**: a changed system/user/assistant string, role, or message count; a changed strategy id/document/artifact or execution-log field; a different number of prompt turns; or the agent dropping the compile tool from the request (fake model returns `TOOL_DEFINITION_MISSING`, so no strategy is authored and the parity run throws) all make the parity test fail. The helper reports the exact field, e.g. `Prompts[1].Content: expected '...' but was '...'` or `ExecutionArtifactJson: expected ...`.

## 7. Negative controls and observed results

All observed via the focused green run (`13/13`, EXIT=0):

| Control | Where | Deliberate failure | Observed result |
|---|---|---|---|
| Parity sensitivity (in-code) | `:185` | one prompt character changed; one execution-JSON field changed | identical snapshots → `Assert.Empty(mismatches)`; altered prompt → exactly one mismatch containing `Prompts[1].Content` and the altered text; altered result → exactly one mismatch containing `ExecutionArtifactJson`. **PASS** |
| Max steps | `:299` | model always returns a compile tool call; `BonesAgentLoopOptions { MaxSteps = 2 }` | `BonesAgentExecutionException` with `Status == AgentExecutionStatus.MaxStepsReached`, `Steps.Count == 2` (each step 1 tool result), message contains `Max steps (2) reached without a final answer.`, model called twice, **no** `bones-strategy-*` artifact. **PASS** |
| Tool failure limit / invoker route | `:338` | model always calls unregistered `tool.bones.unregistered`; `MaxConsecutiveToolFailures = 3` | `BonesAgentExecutionException` with `Status == ToolFailureLimitReached`, `Steps.Count == 3`, last tool result `IsError == true` and content `unknown-tool: no tool named 'tool.bones.unregistered' is registered.`, message contains `Tool failure limit (3) reached.`, no strategy artifact. **PASS** |
| Null `FinalMessage` | `:514` | fake `IAgentExecutor` returns `Completed` with `FinalMessage: null` | exact `InvalidOperationException` message `The agent runtime completed without a final assistant message.`; executor called once; no strategy artifact. **PASS** |
| Tool-routing positive (route proof) | `:233` | invalid script compiled through the tool on turn 1, valid final script on turn 2 | second `ChatRequest` has 4 messages, the last is a `Tool` message whose JSON has `succeeded:false` and a `failureReason` containing `Strategy script compilation failed` and the compiler diagnostic text; strategy `initial-seat-1-v1` is persisted with the valid source. **PASS** |
| Dropped tool definitions (mutation control) | fake hook `:930-940` | source mutation that removes `tools:` from the prompt request | Hook is implemented and would make the parity scenario fail (`TOOL_DEFINITION_MISSING` → no compilable answer → `InvalidOperationException`); the mutation itself was **not executed** in this WP2 run — the orchestrator will run it as the reproduction control. ❓ deferred |

The co-learning negative/positive distinction is covered by `:442`: an **empty** library with `AllSeatsLearning = true` makes the unchanged `BonesCoLearningPonderAgent` call the new-path single-seat agent for seats 1–4; all four prompts are distinct (`Player seat: 1..4`, `Observation count: 0`), each seat gets its own strategy artifact (`initial-seat-{1..4}-v1`) and the wrapper returns the **first completed** seat result (seat 1). **PASS**.

## 8. DI constructor selection (finding + choice)

- Legacy 4-parameter ctor: `BonesPonderAgent.cs:35`; production 6-parameter ctor: `:57`. The executor parameter has no default value, as required.
- Observed: with **only `IModelProvider`** registered, `AddSingleton<BonesPonderAgent>()` resolves the legacy ctor and calls the provider once (`:617`); with **only `IAgentExecutor`** registered, it resolves the production ctor and calls the executor once (`:642`).
- Observed **Microsoft DI limitation** (first green attempt, `.tmp/wp2-test-run1.log`): when **both** an `IModelProvider` and an `IAgentExecutor` are registered, `ServiceProvider` throws `Unable to activate type 'Wip.Bones.Agents.Ponder.BonesPonderAgent'. The following constructors are ambiguous`. This matches the .NET 10 `CallSiteFactory` rule: after choosing the longest resolvable ctor, a shorter resolvable ctor is accepted only when **all** of its parameter types are already present in the longer one; the legacy ctor has `IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>`, which the mandated 6-parameter ctor does not list. ❓ (source-level inference from `dotnet/runtime` `release/10.0` `CallSiteFactory.CreateConstructorCallSite`).
- **Choice**: keep the 6-parameter signature exactly as contracted; the supported production registration is executor-only (the WP4 end state retires the provider to test-only), and the current host wiring is provider-only. The focused DI test therefore covers the two real registrations; it does not assert the unsupported both-registered combination. No host DI change was made.

## 9. No stub / TODO

Command:

```powershell
$files = @('WIP/Wip.Bones.Agents/Runtime/BonesAgentLoopOptions.cs','WIP/Wip.Bones.Agents/Runtime/BonesAgentTools.cs','WIP/Wip.Bones.Agents/Ponder/BonesPonderPrompts.cs','WIP/Wip.Bones.Agents/Ponder/BonesPonderAgent.cs','WIP/Wip.Runtime/Runtime/ArtifactAgentMemory.cs')
$hits = Select-String -Path $files -Pattern 'TODO','FIXME','stub','NotImplementedException','NotSupportedException' -SimpleMatch
if ($null -eq $hits) { "NO STUB/TODO MATCHES in $($files.Count) production files" } else { $hits }
```

Observed output:

```text
NO STUB/TODO MATCHES in 5 production files
```

The test files contain `NotSupportedException` throws, all inside `IChatModel.StreamAsync` implementations of hand-written test fakes (the runtime tests never stream) — test-only code, not production stubs.

Encoding verification (UTF-8 without BOM; first bytes `117,115,105` = `usi`):

```text
WIP/Wip.Bones.Agents/Runtime/BonesAgentLoopOptions.cs first3bytes=117,115,105
WIP/Wip.Bones.Agents/Runtime/BonesAgentTools.cs first3bytes=117,115,105
WIP/Wip.Bones.Agents/Ponder/BonesPonderPrompts.cs first3bytes=117,115,105
WIP/Wip.Bones.Agents/Ponder/BonesPonderAgent.cs first3bytes=117,115,105
WIP/Wip.Runtime/Runtime/ArtifactAgentMemory.cs first3bytes=117,115,105
WIP/Wip.Bones.Tests/Runtime/BonesPonderRuntimeTests.cs first3bytes=117,115,105
```

## 10. Scope, integrity and evidence limits

- **No existing test assertion was edited, skipped, deleted or weakened**; the only test-file change is the added memory regression test in §11 (plus the Bones memory test switching stores, same assertions). `git status` shows only the WP2 code paths: 4 new files, `BonesPonderAgent.cs`, and the two §11 memory-fix files.
- **No `WipBuilder.AddTool` call** was introduced; the compile tool is only an agent-loop `ToolDefinition`/`ICapabilityDescriptor`, so the "exactly 4 descriptors" registration contract is not touched by this work package (registration tests were not run here; orchestrator full suite).
- **Only focused tests and project-scoped Release builds were run**; the full Bones/Runtime/Shell suites were deliberately not run (orchestrator instruction). Their status is not claimed by this artifact.
- The local-store/`ArtifactAgentMemory` filename mismatch is **fixed** in §11: discovery now accepts both the exact `agent-memory.json` name and the local store's `agent-memory-{artifactId}.json`, and the Bones memory test runs over the real `WipArtifactStoreLocal`.
- ❓ The both-registered DI ambiguity (§8) is a real .NET rule; the contract wording "the 6-parameter ctor wins" holds only for the executor-without-provider registration actually used in production. The orchestrator may want to record this as a known constraint or add a factory registration in WP4.
- No commit was made.

## 11. Memory store fix

### 11.1 Production change (`WIP/Wip.Runtime/Runtime/ArtifactAgentMemory.cs`)

Discovery previously matched only `RelativePath.EndsWith("agent-memory.json")`; `WipArtifactStoreLocal` writes `agent-memory-{artifactId}.json`, so any memory written through the local store was invisible to the next `LoadAsync`. The filter now delegates to `IsMemoryArtifact` (`ArtifactAgentMemory.cs:104-115`):

```csharp
private static bool IsMemoryArtifact(ArtifactDescriptor descriptor)
{
    if (!string.Equals(descriptor.ProducerType, ProducerType, StringComparison.Ordinal))
        return false;

    var fileName = Path.GetFileName(descriptor.RelativePath.Replace('\\', '/'));
    if (string.Equals(fileName, MemoryFileName, StringComparison.OrdinalIgnoreCase))
        return true;

    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
    return fileNameWithoutExtension.StartsWith("agent-memory-", StringComparison.OrdinalIgnoreCase);
}
```

The `ProducerType == "ArtifactAgentMemory"` guard is kept (`:106`). `LoadAsync` now calls `.Where(IsMemoryArtifact)` (`:42`). Exact-name stores (e.g. the runtime in-memory fake) are unchanged.

### 11.2 Regression test (`WIP/Wip.Runtime.Tests/Memory/ArtifactAgentMemoryTests.cs`)

`AppendThenLoad_GivenLocalStoreNormalizedFileName_ExpectedMessagesSurviveAcrossInstances` (`:28`) extends the existing hand-written `InMemoryArtifactStore` with an opt-in `useLocalStoreFileNames` mode (`:136-158`) that produces `agent-memory-{artifactId}.json`. It appends `"first"` through one memory instance, asserts the stored descriptor path is `.wip/artifacts/session-memory-local-name/agent-memory-agent-memory-*.json`, appends `"second"`, then loads through a **new** `ArtifactAgentMemory` and asserts both messages with their roles. Before the fix this load returned `Empty`; now it returns 2 messages. The existing redaction test (`Append_GivenMessageContainingApiKey_ExpectedPersistedArtifactIsRedacted`, `:116`) is unchanged and still passes.

### 11.3 Bones memory test now uses the real local store

`ExecuteAsync_GivenMemorySnapshot_...` (`BonesPonderRuntimeTests.cs:381`) now uses `PonderFixture` (real `WipArtifactStoreLocal` + real `ArtifactAgentMemory`), with no in-memory workaround and no workaround comment. Assertions unchanged: pre-seeded memory message is message index 1 (after the system prompt, before the current conversation), and after the run the memory snapshot is `[pre-seed user, new user, assistant markdown]` (3 messages); the artifact list contains the local-store normalized `agent-memory-agent-memory-*.json` under `.wip/sessions/memory-wp2/artifacts/` (`:432-441`).

### 11.4 Observed reruns

**(a) Runtime memory tests**

```powershell
wsl.exe -d Ubuntu -- bash -c 'bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~ArtifactAgentMemoryTests" > /tmp/wp2-memory-runtime.log 2>&1; echo EXIT=$?; tail -n 8 /tmp/wp2-memory-runtime.log'
```

Observed:

```text
EXIT=0
  Wip.Runtime.Tests -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Runtime.Tests/bin/Release/net10.0/Wip.Runtime.Tests.dll
Test run for /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Runtime.Tests/bin/Release/net10.0/Wip.Runtime.Tests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 1 s - Wip.Runtime.Tests.dll (net10.0)
```

Because the fix is in shared runtime code, this also confirms the pre-existing `ArtifactAgentMemoryTests` (including redaction) still pass: **7/7**.

**(b) Bones memory test**

```powershell
wsl.exe -d Ubuntu -- bash -c 'bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesPonderRuntimeTests.ExecuteAsync_GivenMemorySnapshot" > /tmp/wp2-memory-bones.log 2>&1; echo EXIT=$?; tail -n 8 /tmp/wp2-memory-bones.log'
```

Observed:

```text
EXIT=0
  Wip.Bones.Tests -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/bin/Release/net10.0/Wip.Bones.Tests.dll
Test run for /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/bin/Release/net10.0/Wip.Bones.Tests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 19 s - Wip.Bones.Tests.dll (net10.0)
```

**(c) Full focused WP2 suite**

```powershell
wsl.exe -d Ubuntu -- bash -c 'bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesPonderRuntimeTests" > /tmp/wp2-memory-focused.log 2>&1; echo EXIT=$?; tail -n 8 /tmp/wp2-memory-focused.log'
```

Observed:

```text
EXIT=0
  Wip.Bones.Tests -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/bin/Release/net10.0/Wip.Bones.Tests.dll
Test run for /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/bin/Release/net10.0/Wip.Bones.Tests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 11 s - Wip.Bones.Tests.dll (net10.0)
```

### 11.5 Counts

| Run | Before | After | Result |
|---|---|---|---|
| `ArtifactAgentMemoryTests` | 6 | **7** (one regression test added) | 7 passed / 0 failed |
| Bones memory filter | 1 | 1 (same test, real store) | 1 passed / 0 failed |
| `BonesPonderRuntimeTests` | 13 | 13 | 13 passed / 0 failed |

The production memory path is no longer hollow: the real `WipArtifactStoreLocal` + `ArtifactAgentMemory` combination loads prior messages (verified by the new regression test and by the Bones agent's first-turn request containing the pre-seeded memory message).
