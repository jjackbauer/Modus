# WP6 — Retire the legacy Bones workflow shim

- **Round**: 6 (goal WIP-IMPL-001 continuation)
- **Work package**: WP6 — add the declarative-only builder registration, make `BonesLearningWorkflow` implement only `IWorkflowDefinition`, delete the stub `ExecuteAsync`, switch the Bones registration, reconcile the legacy-registration test helper, and run the full Bones + Runtime + Shell + Builder + architecture suites plus the Host/Web builds.
- **Date**: 2026-11-06
- **Repository HEAD**: `7158280` (`test(bones): prove the runtime agent permission and safety posture [WP5]`) plus this work package (uncommitted).
- **Result**: **Contracted suites PASS** — Builder 34/34 (32 pre-existing + 2 new), Shell 34/34, architecture 4/4, Bones 455/455, Runtime 147/153 with exactly the 6 documented platform/waiver failures. `Wip.Bones.Host` (0 errors) and `Wip.Bones.Web` (0 errors) build. One **out-of-scope** test (`Wip.Bones.Web.Tests.BonesViewerWorkflowTests.BonesViewerWorkflow_GivenLearningRun_ExpectedShellStdoutContainsViewerUrl`) fails on a stale markdown fixture; it is independent of this diff (§6.2).
- **Scope**: `WIP/Wip.Builder/WipBuilder.cs`; `WIP/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs`; `WIP/Wip.Runtime.Tests/Runtime/WipRuntimeOrchestratorTests.cs`; `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflow.cs`; `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflowRegistration.cs`; `WIP/Wip.Bones.Tests/Workflow/BonesLearningWorkflowRegistrationTests.cs` (only `CreateBonesLearningBuilderWithoutObserveTool`); this document. No excluded path was modified.

## 1. Scope boundary

`git status --porcelain=v1` observed the same six modified WP6 paths listed above. The other entries in the working tree (`Wip.slnx`, `docs/plan/test-waivers.json`, `tests/Wip.Architecture.Tests/*`, `tools/run-wip-tests.ps1`, `WIP/Wip.Tholdem*`, `docs/goal-tholdem/`) were already present before this work package and were not touched. `git diff --stat -- WIP/Wip.Builder WIP/Wip.Builder.Tests WIP/Wip.Runtime.Tests WIP/Wip.Bones.Agents WIP/Wip.Bones.Tests docs/execution/harness/round-6` observed:

```text
 WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflow.cs              |  16 +--
 WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflowRegistration.cs  |   8 +-
 WIP/Wip.Bones.Tests/Workflow/BonesLearningWorkflowRegistrationTests.cs | 3 +-
 WIP/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs |  64 ++++++++++
 WIP/Wip.Builder/WipBuilder.cs                                       |  32 +++++
 WIP/Wip.Runtime.Tests/Runtime/WipRuntimeOrchestratorTests.cs        | 135 +++++++++++++++++++++
 6 files changed, 236 insertions(+), 22 deletions(-)
```

All six files are UTF-8 without BOM: first bytes of every edited file are `117,115,105` (`usi`), no `EF BB BF` (observed with `System.IO.File::ReadAllBytes`).

## 2. What changed

| File:line | Change |
|---|---|
| `WIP/Wip.Builder/WipBuilder.cs:245-275` | New `AddDeclarativeWorkflow<TDefinition,TRequest,TResult>(WorkflowId, string)`: rejects a blank display name, registers `TDefinition` as a singleton (`:261`), registers `IWorkflowDefinition` through a factory that resolves **that same** instance (`:262`), and appends exactly one `WorkflowRegistration` (`:265-273`) with the workflow id, `typeof(TDefinition)`, request/result types, a `WorkflowDescriptor<TRequest,TResult>` and a `CreateRunRequest` delegate that throws `InvalidOperationException` (`:271-273`). Returns the builder (`:274`). |
| `WIP/Wip.Builder/WipBuilder.cs:305` | `AddWorkflowDefinition<TDefinition>()` left unchanged (still `Services.AddSingleton<IWorkflowDefinition, TDefinition>()`). |
| `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflow.cs:11` | Class now implements only `IWorkflowDefinition`. The `IWorkflow<BonesLearningWorkflowRequest, BonesLearningWorkflowResult>` base, the stub `ExecuteAsync` (which returned the `workflow-dispatch` ids) and the now-unused `using Wip.Runtime.Runtime;` were removed. `WorkflowId` (`:13`) and `Compile()` (`:19-45`) are unchanged. |
| `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflowRegistration.cs:18-22` | `AddWorkflow<...>` + `AddWorkflowDefinition<...>` replaced by `AddDeclarativeWorkflow<BonesLearningWorkflow, BonesLearningWorkflowRequest, BonesLearningWorkflowResult>(BonesLearningWorkflowIds.WorkflowId, "Bones learning workflow")`. The four capability registrations below it (`:24-44`, with their supporting `AddSingleton` agent registrations) are unchanged. |
| `WIP/Wip.Bones.Tests/Workflow/BonesLearningWorkflowRegistrationTests.cs:340-343` | `CreateBonesLearningBuilderWithoutObserveTool` helper now calls `AddDeclarativeWorkflow` instead of `AddWorkflow` + `AddWorkflowDefinition`; the test intent (no observe-tool capability ⇒ deterministic missing-capability failure, no artifacts) is unchanged and still passes (`:247-307`). |
| `WIP/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs:413-465` | Two new tests; definition fixture at `:570`. |
| `WIP/Wip.Runtime.Tests/Runtime/WipRuntimeOrchestratorTests.cs:1701-1775` | New declarative-only end-to-end test; fixtures at `:1807-1863`. |

### 2.1 Builder tests

- `AddDeclarativeWorkflow_GivenWorkflowId_ExpectedRegistersDefinitionAndSingleSelectionRegistration` (`WipBuilderExplicitRegistrationTests.cs:413`): asserts the builder is returned; `provider.GetRequiredService<DeclarativeOnlyWorkflow>()` and `Assert.Single(provider.GetServices<IWorkflowDefinition>())` are the **same instance** (`:428`); the single registration has `workflow.declarative`, `typeof(DeclarativeOnlyWorkflow)`, request `typeof(WorkflowRequest)`, result `typeof(WorkflowResult)`, and a descriptor carrying the display name and the same types (`:425-437`).
- `AddDeclarativeWorkflow_GivenWorkflowId_ExpectedNoLegacyWorkflowServiceAndRunRequestFactoryThrows` (`:439`): asserts no service descriptor whose `ServiceType` is `IWorkflow<WorkflowRequest, WorkflowResult>` (`:451`), the provider resolves `null` for that legacy service, `registration.WorkflowType` is not assignable to it (`:452-453`), and the run-request factory throws `InvalidOperationException` whose message contains `workflow.declarative`, `declarative` and `definition plan` (`:455-464`).

### 2.2 Runtime test (registered ONLY with `AddDeclarativeWorkflow`)

`RunWorkflowAsync_GivenWorkflowRegisteredOnlyAsDeclarative_ExpectedSelectsRegistrationAndExecutesDefinitionPlan` (`WipRuntimeOrchestratorTests.cs:1701`):

- builder path: `builder.AddDeclarativeWorkflow<DeclarativeRunThenPlanWorkflow, DeclarativeRunRequest, DeclarativeRunResult>(...)` (`:1711-1715`) — no `AddWorkflow` anywhere in the test; the definition (`:1807-1823`) compiles a two-stage plan: **Run** `DeclarativeRunRequest → DeclarativeRunResult` then **Plan** `DeclarativeFollowUpRequest → DeclarativeFollowUpResult`, with one `StageMap` between them and an `initialRequest` from `session.TaskDescription`.
- capabilities: `AddTool<RecordingDeclarativeRunTool, ...>` (`:1717-1719`) records the incoming request; `AddAgent<RecordingDeclarativeFollowUpAgent, ...>` (`:1720-1722`) records the mapped request.
- assertions: result workflow id `workflow.declarative.only` (`:1745`); registration type `typeof(DeclarativeRunThenPlanWorkflow)` (`:1746`); exactly **2** stages (`:1747`), i.e. the compiled definition plan rather than the legacy six-stage linear compilation; `Assert.Collection` verifies stage kinds, request types, producer capability ids and execution artifacts (`:1748-1766`); **no Validate stage** (`:1767`) and exactly **2** stage-execution artifacts were persisted (`:1768`); the tool saw `declarative-only-task` (`:1769`) and the agent saw the mapped `ran:declarative-only-task` (`:1770`); a `WorkflowExecuted` event was published (`:1771-1774`).

## 3. Test-first evidence

The two Builder tests were added first and run against the unchanged builder. Observed red (compile failure, exit code 1):

```text
WIP/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs(418,32): error CS1061: 'WipBuilder' does not contain a definition for 'AddDeclarativeWorkflow' and no accessible extension method 'AddDeclarativeWorkflow' accepting a first argument of type 'WipBuilder' could be found (are you missing a using directive or an assembly reference?)
WIP/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs(444,17): error CS1061: 'WipBuilder' does not contain a definition for 'AddDeclarativeWorkflow' ...
[exit code: 1]
```

After adding `AddDeclarativeWorkflow`:

```text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 1 s - Wip.Builder.Tests.dll (net10.0)
```

The Runtime test was added before the registration method and then run. Its **first** green attempt failed on a test-authoring assertion, not on production behaviour: I expected `MappedInputContractName` to be the map **source** (`DeclarativeRunResult`), but `LinearWorkflowStageCompilation.ResolveMappedInputContractName` returns the adapter **target** contract name (`WIP/Wip.Runtime/Runtime/WorkflowExecutionPipeline.cs:25-30`), observed as `DeclarativeFollowUpRequest`. Corrected the assertion to the target type; rerun:

```text
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 1 s - Wip.Runtime.Tests.dll (net10.0)
```

Focused Bones registration tests after the production switch:

```text
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 15 s - Wip.Bones.Tests.dll (net10.0)
```

## 4. Grep proof (retired symbols)

Command and observed output:

```text
$ git grep -n -E "IWorkflow<BonesLearningWorkflow|AddWorkflow<BonesLearningWorkflow|AddWorkflowDefinition<BonesLearningWorkflow|workflow-dispatch" -- WIP
grep-exit=1
```

Zero hits for the legacy base interface, the `AddWorkflow<BonesLearningWorkflow...>` sites, the former `AddWorkflowDefinition<BonesLearningWorkflow>` call and the stub `workflow-dispatch` ids. Positive control:

```text
$ git grep -n "IWorkflowDefinition" -- WIP/Wip.Bones.Agents/Workflow
WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflow.cs:11:public sealed class BonesLearningWorkflow : IWorkflowDefinition
```

`docs/plan/test-waivers.json` was not edited.

## 5. Verification matrix (all commands via the mandated WSL runner)

Runner: `wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh ...`; one `setlocale: LC_ALL` warning is printed by the runner on every invocation and is harmless.

| Suite / build | Command (project, `-c Release`) | Observed result |
|---|---|---|
| Builder | `test WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj` | `Passed! - Failed: 0, Passed: 34, Skipped: 0, Total: 34` (32 pre-existing + 2 new; the waiver floor stays 32) |
| Shell | `test WIP/Wip.Shell.Tests/Wip.Shell.Tests.csproj` | `Passed! - Failed: 0, Passed: 34, Skipped: 0, Total: 34` |
| Architecture | `test tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj` | `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4` (7 m) |
| Runtime | `test WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj` | `Failed! - Failed: 6, Passed: 147, Skipped: 0, Total: 153, Duration: 6 s` — exactly the 6 documented failures, §6.1 |
| Bones | `test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj` | `Passed! - Failed: 0, Passed: 455, Skipped: 0, Total: 455, Duration: 1 m 30 s` |
| Bones Host build | `build WIP/Wip.Bones.Host/Wip.Bones.Host.csproj` | `Build succeeded. ... 7 Warning(s) 0 Error(s)` (also built `Wip.Bones.Web.dll`) |
| Bones Web build | `build WIP/Wip.Bones.Web/Wip.Bones.Web.csproj` | First attempt failed `MSB3021` copying `Wip.Runtime.pdb` (concurrent session in the same tree). Retried after ~35 s: `Build succeeded. ... 10 Warning(s) 0 Error(s)` |

Focused commands that produced the §3 evidence:

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj -c Release --filter "FullyQualifiedName~AddDeclarativeWorkflow"
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~GivenWorkflowRegisteredOnlyAsDeclarative"
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesLearningWorkflowRegistrationTests"
```

## 6. Anything not passing / deviations

### 6.1 Runtime: 153 total, not 152; the same 6 documented failures

- **Count deviation**: expected 152 total; observed **153** because WP6 adds one Runtime test. Passed rose from the documented 146 to 147; failed stayed at 6. No other Runtime test failed.
- The six failures in this run were exactly:
  1. `DeepSeekModelProviderTests.DeepSeekProvider_GivenCancellationRequested_ExpectedRequestAbortedWithoutBackgroundContinuation` (`Expected: 1, Actual: 0`) — the documented waiver (`docs/plan/test-waivers.json:146-151`).
  2. `WipRuntimeValidationReportTests.ValidateAsync_GivenRuntimeDotNetValidationRequest_PersistsAuthoritativeBuildAndTestEvidence`
  3. `WipRuntimeValidationReportTests.HostLifecycle_GivenValidationPasses_ExpectedReadinessTransitionsToHealthyWithExecutionEvidence`
  4. `WipRuntimeValidationReportTests.HostLifecycle_GivenValidationFails_ExpectedReadinessRemainsBlockedWithDeterministicFailureState`
  5. `WipRuntimeValidationReportTests.ValidateAsync_GivenRuntimeValidationAndReattach_PersistsReplayableEvidenceAndRestoresCorrelationContinuity`
  6. `WipRuntimeValidationReportTests.HostLifecycle_GivenRepeatedValidationRuns_ExpectedStateTransitionsRemainDeterministicAndIsolated`

  All five fail with the same WSL platform error: `Unable to resolve dotnet executable '/tmp/.../dotnet.cmd': ... Permission denied` from `DotNetValidationValidator.ResolveDotNetExecutableAsync`. This is the pre-existing platform failure set recorded at WP5 (`Runtime 152/146/6 (documented)`, `docs/execution/harness/state.md:15,33`); WP6 does not touch validation code.

### 6.2 Out-of-scope: `Wip.Bones.Web.Tests` viewer workflow test fails on a stale fixture

I additionally ran `--filter "FullyQualifiedName~BonesViewerWorkflowTests"` (not in the required suite list) to check a second consumer of `AddBonesLearningWorkflow`:

```text
Failed Wip.Bones.Web.Tests.BonesViewerWorkflowTests.BonesViewerWorkflow_GivenLearningRun_ExpectedShellStdoutContainsViewerUrl
Error Message:
 System.InvalidOperationException : Model response did not contain a compilable IBonesPlayerSlot script.
   at Wip.Bones.Agents.Ponder.BonesPonderAgent.ExecuteAsync(...)
Failed!  - Failed: 1, Passed: 1, Skipped: 0, Total: 2
```

This is **not caused by the WP6 diff**: the fixture's `StubBonesStrategyModelProvider` returns markdown (`WIP/Wip.Bones.Web.Tests/Viewer/BonesViewerWorkflowTests.cs:296-305`), while the runtime-ported Ponder requires a compilable `IBonesPlayerSlot` script. Before WP6 the same test already selected the definition plan, because `RunWorkflowAsync` prefers `ResolveDefinitionPlan` over the legacy plan whenever an `IWorkflowDefinition` is resolvable (`WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs:257-263`) and the old registration called `AddWorkflowDefinition<BonesLearningWorkflow>()`. The WP6 diff removes only the legacy `AddWorkflow` service/registration; it does not change which plan runs or the stub response, so this failure is structurally independent of WP6. ❓ The test file is unmodified since commit `f8323ea` and predates the WP2 Ponder port; I did not re-run the baseline HEAD to capture the identical failure empirically. The file is outside the contracted suites and was not changed; per the task, the Web deliverable is the build, which succeeds.

### 6.3 Notes

- The three pre-existing waived Bones meta-tests (`docs/plan/test-waivers.json:129-139,164-170`) **passed** in this run (Bones 455/455, 0 failed), i.e. they are not failing locally today.
- The XML doc comment on `BonesLearningWorkflow.Compile()` still says "The runtime prefers this plan over the legacy static map registry" (`BonesLearningWorkflow.cs:15-18`). The contract said to keep `Compile()` exactly as-is, so the stale sentence was deliberately left untouched; it has no behavioural effect.
- Build warnings are pre-existing (nullable/analyzer/NU1510); no new warnings were introduced in the edited files. The Web build retry was the only transient failure, cleared by the documented ~30 s wait.
- No test was skipped, weakened or deleted; no waiver was changed.

## 7. Reproduction

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj -c Release
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Shell.Tests/Wip.Shell.Tests.csproj -c Release
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj -c Release
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj -c Release
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh build WIP/Wip.Bones.Host/Wip.Bones.Host.csproj -c Release
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh build WIP/Wip.Bones.Web/Wip.Bones.Web.csproj -c Release
```

Expected tails: Builder `34/34`, Shell `34/34`, architecture `4/4`, Runtime `147 passed / 6 documented failed / 153 total`, Bones `455/455`, both builds `0 Error(s)`.