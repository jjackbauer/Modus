# Round 3 implementation — bounded direct script moves

Workspace: `C:\Users\ricar\dev\Modus`. No API key was used or printed. No existing assertion was weakened, widened, deleted, or skipped. No file outside the task SCOPE was changed except this evidence record and scratch evidence under `.tmp/goal-round3/`.

## Objective

A model-authored script that never returns from `IBonesPlayerSlot.ChooseMove` must not block the match worker forever on the direct execution path. `ExecuteChooseMoveDirect` now accepts an opt-in deadline (`BonesStrategyScriptHostOptions.EnableDirectExecutionTimeout`, default `false`) and the checked-in host turns it on with the existing `ScriptHost:ExecutionTimeout = 00:00:05`.

## Test-first failure observed before the behavior change

Scaffold step (no behavior): added the inert `EnableDirectExecutionTimeout` property with default `false` so the new test could compile against the pre-change behavior. Nothing read the property yet.

New test:
`WIP/Wip.Bones.Tests/Engine/BonesStrategyScriptHostDirectTimeoutTests.cs` —
`ExecuteChooseMoveDirect_GivenInfiniteLoopScriptAndDirectTimeoutEnabled_ExpectedThrowsTimeoutWithinBoundedDeadline`.
It compiles an infinite-loop `IBonesPlayerSlot`, invokes `ExecuteChooseMoveDirect` on a 5-second-bounded `Task.Wait` with `EnableDirectExecutionTimeout = true` and `ExecutionTimeout = 200 ms`, and asserts the bounded return plus `BonesStrategyScriptExecutionTimeoutException.Timeout == 200 ms`.

```powershell
dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj --filter FullyQualifiedName~BonesStrategyScriptHostDirectTimeoutTests --nologo -v minimal
```

Observed before the `Thread`/`Join` implementation (exit code 1; the run returned because the test capped the wait at 5 s):

```text
  Failed Wip.Bones.Tests.Engine.BonesStrategyScriptHostDirectTimeoutTests.ExecuteChooseMoveDirect_GivenInfiniteLoopScriptAndDirectTimeoutEnabled_ExpectedThrowsTimeoutWithinBoundedDeadline [5 s]
  Error Message:
   ExecuteChooseMoveDirect did not return within 5 s; the direct script path is not bounded by ExecutionTimeout.
  Stack Trace:
     at Wip.Bones.Tests.Engine.BonesStrategyScriptHostDirectTimeoutTests.ExecuteChooseMoveDirect_GivenInfiniteLoopScriptAndDirectTimeoutEnabled_ExpectedThrowsTimeoutWithinBoundedDeadline() in ...\BonesStrategyScriptHostDirectTimeoutTests.cs:line 67
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 5 s - Wip.Bones.Tests.dll (net10.0)
EXIT_CODE=1
```

This was a runtime behavior failure, not a compile error: the test assembly built, the compile succeeded, the script ran, and the direct call never returned to the test.

## Change applied

- `BonesStrategyScriptHostOptions.EnableDirectExecutionTimeout { get; init; }` defaults to `false`; `ExecutionTimeout` and its `> 0` constructor validation are unchanged.
- `ExecuteChooseMoveDirect` keeps the exact original call as the disabled branch:
  ```csharp
  if (!_options.EnableDirectExecutionTimeout)
      return compiled.PlayerSlot.ChooseMove(state, legalMoves);
  ```
  When enabled it starts the move on a dedicated `IsBackground = true` thread and `Join(_options.ExecutionTimeout)`; on timeout it throws `BonesStrategyScriptExecutionTimeoutException(_options.ExecutionTimeout)`. The script's original exception is preserved with `ExceptionDispatchInfo.Capture(...).Throw()` (no `AggregateException` wrapper, no swallowing). A null move is passed through exactly as the disabled path does.
- `BonesHostOptions.ResolveScriptHostOptions` binds `BonesHost:ScriptHost:EnableDirectExecutionTimeout` (fallback = `false`).
- `WIP/Wip.Bones.Host/appsettings.json` sets `"EnableDirectExecutionTimeout": true` next to the existing `"ExecutionTimeout": "00:00:05"`.
- `WIP/Wip.Bones.Host/README.md` configuration table gains one row (table only) for the setting and its environment-variable spelling.
- New tests (same file): the timeout test above, plus `ExecuteChooseMoveDirect_GivenScriptThatThrowsAndDirectTimeoutEnabled_ExpectedRethrowsOriginalException` and `ExecuteChooseMoveDirect_GivenDefaultOptions_ExpectedReturnsMoveWithoutDirectTimeout`.

## Observed pass after the change

Exact required timeout test (exit code 0):

```powershell
dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj --filter "FullyQualifiedName=Wip.Bones.Tests.Engine.BonesStrategyScriptHostDirectTimeoutTests.ExecuteChooseMoveDirect_GivenInfiniteLoopScriptAndDirectTimeoutEnabled_ExpectedThrowsTimeoutWithinBoundedDeadline" --nologo -v minimal
```

```text
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 929 ms - Wip.Bones.Tests.dll (net10.0)
EXIT_CODE=0
```

All three new tests (class filter, exit code 0):

```powershell
dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj --filter "FullyQualifiedName~BonesStrategyScriptHostDirectTimeoutTests" --nologo -v minimal
```

```text
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s - Wip.Bones.Tests.dll (net10.0)
EXIT_CODE=0
```

Existing `BonesStrategyScriptHostTests` filter (exit code 0) — no assertion changed, all six pre-existing tests still pass, including the 250 ms `ExecuteChooseMove` timeout test:

```powershell
dotnet test WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj --filter "FullyQualifiedName~BonesStrategyScriptHostTests" --nologo -v minimal
```

```text
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 1 s - Wip.Bones.Tests.dll (net10.0)
EXIT_CODE=0
```

Builds (both exit code 0):

```powershell
dotnet build WIP/Wip.Bones/Wip.Bones.csproj --nologo -v minimal
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
BUILD_EXIT=0
```

```powershell
dotnet build WIP/Wip.Bones.Host/Wip.Bones.Host.csproj --nologo -v minimal
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
BUILD_EXIT=0
```

The test-project build emits one analyzer warning (`xUnit1031`) for the required `Task.Wait(5 s)` call; the task explicitly requires that bounded wait instead of an unbounded await, and the warning does not fail the build.

## Disabled path is untouched

The disabled branch is the original one-line call, unchanged. Default `BonesStrategyScriptHostOptions` has `EnableDirectExecutionTimeout = false`, so `new BonesStrategyScriptHost()` and every existing caller keep the inline path. In particular the frozen benchmark constructs `new BonesStrategyScriptHost()` in `WIP/Wip.Bones.Benchmark/Arena.cs:36`, so benchmark execution is not routed through the new thread. The new default-path test also observes a normal move return with default options, and all six existing script-host tests pass.

Host binding was checked with a scratch harness under `.tmp/goal-round3/bindcheck` (stub provider, in-memory configuration):

```text
bound.enableDirectExecutionTimeout=True
bound.executionTimeout=00:00:05
default.enableDirectExecutionTimeout=False
BIND_EXIT=0
```

The checked-in `appsettings.json` parses and contains:

```text
appsettings.json valid JSON: True
appsettings.ScriptHost.EnableDirectExecutionTimeout=True
appsettings.ScriptHost.ExecutionTimeout=00:00:05
appsettings.ScriptHost.CompilationTimeout=00:00:30
```

No assertion was weakened or skipped. The existing test file was not edited; the new tests add assertions only.

## What did not pass

- Nothing in the scoped behavior failed: the timeout test fails before the `Join` implementation and passes after it, and the disabled/default path passes.
- The non-returning script thread itself is not reclaimed. `Thread.Join` unblocks the caller, but a non-cooperative `while (true)` thread cannot be aborted; it keeps spinning as a background thread until process exit. The objective is satisfied (the match worker/caller is no longer blocked and the match fails), and this matches the requirement's dedicated-thread design, but repeated timeouts would accumulate CPU-burning background threads. Process isolation is explicitly out of scope.
- No xUnit host test covers the new `ResolveScriptHostOptions` binding because adding a test file under `Wip.Bones.Host.Tests` is outside SCOPE, and the full host suite was forbidden. Binding was observed with the scratch harness and the checked-in JSON above, not claimed as a host-suite pass.
- The frozen benchmark numbers were not reproduced in this slice (benchmark work is outside this objective; no benchmark number was changed). Benchmark behavior is unchanged because it uses the default-off host options.
- The two supplementary tests (rethrow and default path) were added after the timeout implementation; only the infinite-loop timeout test is the test-first observation required by the task.

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Bones/Engine/BonesStrategyScriptHostOptions.cs` | Added `EnableDirectExecutionTimeout` (default `false`). |
| `WIP/Wip.Bones/Engine/BonesStrategyScriptHost.cs` | Added the opt-in dedicated-thread/`Join(ExecutionTimeout)` path; disabled branch remains the original inline call; original exceptions rethrown via `ExceptionDispatchInfo`. |
| `WIP/Wip.Bones.Host/BonesHostOptions.cs` | `ResolveScriptHostOptions` binds `EnableDirectExecutionTimeout`. |
| `WIP/Wip.Bones.Host/appsettings.json` | `ScriptHost:EnableDirectExecutionTimeout = true` with existing `00:00:05` execution timeout. |
| `WIP/Wip.Bones.Host/README.md` | One configuration-table row (table only). |
| `WIP/Wip.Bones.Tests/Engine/BonesStrategyScriptHostDirectTimeoutTests.cs` | New: timeout, rethrow, and default-path tests. |
| `docs/goal-bones-v3-learning/round-3/implementation.md` | This evidence record. |

Evidence logs: `.tmp/goal-round3/direct-timeout-exact-test.log`, `.tmp/goal-round3/direct-timeout-tests.log`, `.tmp/goal-round3/script-host-existing-tests.log`, and the scratch binding harness under `.tmp/goal-round3/bindcheck/`.
