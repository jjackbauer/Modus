# WP1 — Bones-side `IAgentExecutor` adapter

- **Round**: 1 (goal WIP-IMPL-001 continuation, D1)
- **Work package**: WP1 — add a Bones-side adapter from `IAgent<TRequest,TResult>` to `IAgentExecutor`
- **Date**: 2026-11-06
- **Repository HEAD**: `ba5debf` (`git rev-parse HEAD` output: `ba5debff480f44a1e4cda4095e4c8fc11cfeea68`)
- **Result**: PASS — focused adapter tests 14/14, Release build 0 errors.
- **Not done (out of scope)**: no domain agent was ported, no host DI / prompt / workflow / existing-test change, no commit.

## 1. Files added

| File | Production/test | Lines |
|---|---|---|
| `WIP/Wip.Bones.Agents/Runtime/BonesAgentExecutionException.cs` | production | 38 |
| `WIP/Wip.Bones.Agents/Runtime/BonesAgentAdapter.cs` | production | 51 |
| `WIP/Wip.Bones.Tests/Runtime/BonesAgentAdapterTests.cs` | test | 378 |

These are the only files created by this work package; they add exactly two production types (no additional production type, no DI type, no compatibility shim). The new paths are untracked in the shared working tree:

```text
$ git status --porcelain -- WIP/Wip.Bones.Agents WIP/Wip.Bones.Tests docs/execution/harness/round-1
?? WIP/Wip.Bones.Agents/Runtime/
?? WIP/Wip.Bones.Tests/Runtime/
?? docs/execution/harness/round-1/
```

The same `git status` (without pathspec) also lists unrelated modifications/untracked paths that were already present or are owned by other concurrent work (e.g. `Wip.slnx`, `docs/plan/test-waivers.json`, `WIP/Wip.Tholdem/`, `docs/goal-tholdem/`); none of them is part of this work package.

## 2. Test-first: observed failing run (red)

The test file was written before either production file existed, then the exact focused command was run.

Command (PowerShell):

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesAgentAdapterTests"
```

Observed output (tail; the only error, compilation stops the test run):

```text
  Wip.Bones.Agents -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/bin/Release/net10.0/Wip.Bones.Agents.dll
  Wip.ShellHost -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.ShellHost/bin/Release/net10.0/Wip.ShellHost.dll
  Wip.Shell.E2E -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Shell.E2E/bin/Release/net10.0/Wip.Shell.E2E.dll
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesAgentAdapterTests.cs(6,24): error CS0234: The type or namespace name 'Runtime' does not exist in the namespace 'Wip.Bones.Agents' (are you missing an assembly reference?) [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj]
EXIT=1
```

Cause: `BonesAgentAdapterTests.cs:6` imports `Wip.Bones.Agents.Runtime`, which did not exist. This is the expected red for a new public API: the tests cannot compile until the adapter types exist.

A second red occurred after the production types were added, from a named-argument typo in the test fixture (`AgentExecutionResult`'s positional parameter is `Error`, not `error`):

```text
  Wip.Bones.Agents -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/bin/Release/net10.0/Wip.Bones.Agents.dll
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Runtime/BonesAgentAdapterTests.cs(331,13): error CS1739: The best overload for 'AgentExecutionResult' does not have a parameter named 'error' [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj]
EXIT=1
```

The fixture was corrected to pass the fifth positional argument (`BonesAgentAdapterTests.cs:326-331`), not to weaken any assertion.

## 3. Build (green)

Command:

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh build WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj -c Release
```

Observed output (relevant tail):

```text
  Wip.Bones.Agents -> /mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/bin/Release/net10.0/Wip.Bones.Agents.dll

Build succeeded.

/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Sandbox.WorkerClient/Wip.Sandbox.WorkerClient.csproj : warning NU1510: PackageReference System.IO.Pipes.AccessControl will not be pruned. Consider removing this package from your dependencies, as it is likely unnecessary. [/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Bones.Agents/Wip.Bones.Agents.csproj]
/mnt/c/Users/ricar/dev/Modus/WIP/Wip.Sandbox.WorkerClient/Wip.Sandbox.WorkerClient.csproj : warning NU1510: PackageReference System.IO.Pipes.AccessControl will not be pruned. Consider removing this package from your dependencies, as it is likely unnecessary.
    2 Warning(s)
    0 Error(s)

Time Elapsed 00:01:10.07
EXIT=0
```

Both warnings are `NU1510` in `Wip.Sandbox.WorkerClient` (outside WP1); the same warning appeared in the pre-change probe run of the focused command, so it is not introduced here. No warning originates from the two new production files.

## 4. Focused test run (green)

Command (identical to the test-first command):

```powershell
wsl.exe -d Ubuntu -- bash /mnt/c/Users/ricar/dev/Modus/.tmp/wsl-dotnet.sh test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj -c Release --filter "FullyQualifiedName~BonesAgentAdapterTests"
```

Observed output (tail):

```text
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 1 s - Wip.Bones.Tests.dll (net10.0)
EXIT=0
```

14 = 8 `[Fact]` methods plus 6 `[Theory]` cases (the five non-success statuses plus the second `Failed` row). No test is skipped or filtered out within the selected class.

## 5. Members implemented

### `BonesAgentExecutionException` (`WIP/Wip.Bones.Agents/Runtime/BonesAgentExecutionException.cs`)

| Member | Location | Implementation |
|---|---|---|
| `sealed class ... : InvalidOperationException` | `:11` | Public sealed domain exception; base type is the existing domain failure contract. |
| `.ctor(AgentExecutionStatus status, IReadOnlyList<AgentStep> steps, string? error)` | `:13-23` | Null-checks `steps` (`:19`), stores `Status` (`:21`) and `Steps` (`:22`). |
| `public AgentExecutionStatus Status { get; }` | `:25` | The stopping status. |
| `public IReadOnlyList<AgentStep> Steps { get; }` | `:27` | The executor's executed steps. |
| `private static string BuildMessage(AgentExecutionStatus, string?)` | `:29-37` | `Failed` -> `error ?? "agent execution failed"` (`:33`); every other status -> `$"agent execution stopped with status {status}: {error}"` (`:36`). |

### `BonesAgentAdapter<TRequest, TResult>` (`WIP/Wip.Bones.Agents/Runtime/BonesAgentAdapter.cs`)

| Member | Location | Implementation |
|---|---|---|
| `sealed class ... : IAgent<TRequest, TResult>` with `where TRequest : notnull`, `where TResult : notnull` | `:12-14` | Implements the existing domain contract. |
| `.ctor(IAgentExecutor executor, Func<...> createRequest, Func<...> mapResult)` | `:20-28` | Null-checks every argument with the exact parameter names `executor` (`:25`), `createRequest` (`:26`), `mapResult` (`:27`). |
| `ExecuteAsync(TRequest, CapabilityContext, CancellationToken)` | `:30-50` | Null-checks `request`/`context` (`:35-36`); builds the runtime request exactly once (`:38`); awaits `IAgentExecutor.ExecuteAsync` exactly once with the same context and token (`:39`); on success returns `_mapResult(request, context, executionResult, cancellationToken)` (`:41-44`); otherwise throws `BonesAgentExecutionException(status, steps, error)` (`:46-49`). There is no `catch` clause in the method, so `OperationCanceledException` is not caught, wrapped, or translated. |

No other production members were added.

## 6. Test coverage evidence

| Requirement | Test | Concrete assertions |
|---|---|---|
| Completed execution forwards exactly the factory-built request and returns the mapper value | `ExecuteAsync_GivenCompletedExecution_ForwardsFactoryRequestAndReturnsMappedResult` `BonesAgentAdapterTests.cs:16` | factory receives the same request/context instances; executor called once (`:68`); executor receives the same `AgentExecutionRequest` instance (`:69`); literal asserts for `SessionId` `session-42` (`:72`), `Task` (`:73`), `ModelId` (`:74`), `SystemPrompt` (`:75`), one `ToolDefinition` `bones.observe` (`:78`), non-default options `MaxSteps=5`/`MaxTotalTokens=4096`/`Timeout=30s`/`MaxConsecutiveToolFailures=2` (`:82-85`); mapped value `final answer` (`:89`). |
| Mapper receives the executor's Steps/Usage | `ExecuteAsync_GivenCompletedExecution_PassesExecutorStepsAndUsageToMapper` `:95` | mapper called once (`:138`); same request (`:139`), context, result (`:144`), step (`:145`), usage (`:146`), and token (`:141`) instances. |
| Every non-success status throws the domain exception with the executor's status; it is an `InvalidOperationException`; mapper not called | `ExecuteAsync_GivenNonSuccessStatus_ThrowsDeterministicInvalidOperationException` `:159`, rows `:153-158` (`Failed` with/without error, `MaxStepsReached`, `TokenBudgetExhausted`, `ToolFailureLimitReached`, `TimedOut`) | exact `BonesAgentExecutionException` (`:184`), `Status` equals result status (`:188`), assignable to `InvalidOperationException` (`:189`), steps preserved (`:190`), deterministic message per status (`:191`), mapper calls `0` (`:192`), executor calls `1` (`:193`). |
| Caller cancellation propagates as `OperationCanceledException` and is not translated | `ExecuteAsync_GivenExecutorCancellation_PropagatesOperationCanceledException` `:197` | exact `OperationCanceledException` type (`:228`), same `CancellationToken` (`:229`), mapper calls `0` (`:230`), executor calls `1` (`:231`). |
| Null request -> `ArgumentNullException` | `ExecuteAsync_GivenNullRequest_ThrowsArgumentNullExceptionWithoutCallingCollaborators` `:235` | `ParamName == "request"` (`:255`), factory not called (`:256`), executor not called. |
| Null context -> `ArgumentNullException` | `ExecuteAsync_GivenNullContext_...` `:261` | `ParamName == "context"` (`:280`), factory not called (`:281`), executor not called. |
| Constructor null checks (`executor`, `createRequest`, `mapResult`) | `Constructor_GivenNull*` `:286`, `:298`, `:310` | each exact `ParamName` asserted. |

All fakes are hand-written (`FakeAgentExecutor` `:343`); no mocking library is used.

## 7. Scope and evidence limits

- The focused filter was used deliberately; the full Bones/Runtime/Shell suites were **not** run here (orchestrator instruction). Their status is not claimed by this artifact.
- ❓ Adding 14 discovered tests raises the full `Wip.Bones.Tests` count; whether the offline count gate needs an update was not verified in this WP (that gate file is outside WP1 scope and was concurrently modified in the shared tree); the orchestrator should reconcile the discovered total.
- ❓ The WSL stderr line `setlocale: LC_ALL: cannot change locale (en_US.UTF-8)` is emitted by the runner wrapper, not by the test host; it appeared in both red and green runs and did not affect exit codes.
- No commit was made.

## 8. No stub / TODO

Command and observed output (the script is shown verbatim; `$hits` is null when no pattern matches):

```powershell
$files = @('WIP/Wip.Bones.Agents/Runtime/BonesAgentAdapter.cs','WIP/Wip.Bones.Agents/Runtime/BonesAgentExecutionException.cs','WIP/Wip.Bones.Tests/Runtime/BonesAgentAdapterTests.cs')
$hits = Select-String -Path $files -Pattern 'TODO','FIXME','stub','NotImplementedException','NotSupportedException' -SimpleMatch
if ($null -eq $hits) { "NO STUB/TODO MATCHES in $($files.Count) files" } else { $hits }
```

Observed output:

```text
NO STUB/TODO MATCHES in 3 files
```

Encoding verification for all three new files (UTF-8 without BOM; first bytes are `u`, `s`, `i`, i.e. `117,115,105`):

```text
WIP/Wip.Bones.Agents/Runtime/BonesAgentAdapter.cs: first3bytes=117,115,105
WIP/Wip.Bones.Agents/Runtime/BonesAgentExecutionException.cs: first3bytes=117,115,105
WIP/Wip.Bones.Tests/Runtime/BonesAgentAdapterTests.cs: first3bytes=117,115,105
```