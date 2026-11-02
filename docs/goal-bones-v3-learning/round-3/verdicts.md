# Round 3 — Direct-Execution Deadline Verdicts (orchestrator verification)

The round-3 falsifier agent was interrupted after exceeding the round budget without writing an
artifact; this is recorded as RISK-006. Every row below was rerun by the orchestrator.

| # | Corrected claim | Verdict | Evidence | Counterexample attempted | Reason |
|---|---|---|---|---|---|
| 1 | A non-returning direct `ChooseMove` is bounded by `ExecutionTimeout` when `EnableDirectExecutionTimeout=true`; it throws `BonesStrategyScriptExecutionTimeoutException` and returns within 5 s | **Supported** | `BonesStrategyScriptHostDirectTimeoutTests` passed; test waits 5 s and observes the 200 ms timeout; code `BonesStrategyScriptHost.cs:118-160` | Run an infinite loop inline; run with a long timeout; disable the option | The test returns in <1 s with the timeout exception; the caller is no longer blocked. |
| 2 | With `EnableDirectExecutionTimeout=false`, the direct path is the original inline call | **Supported** | `BonesStrategyScriptHost.cs:127-128`; `ExecuteChooseMoveDirect_GivenDefaultOptions_ExpectedReturnsMoveWithoutDirectTimeout` passed | Default-options script selecting last legal move | The disabled branch is exactly one inline call; no thread/timeout is created. |
| 3 | The host binds `ScriptHost:EnableDirectExecutionTimeout=true` and a bounded run still stops cleanly | **Supported** | `appsettings.json:50-55`; `BonesHostOptions.cs:605-606`; bounded stub run: 20/20 games, `capReached=True`, `stopReason=GameBudgetExhausted`, no timeout/unhandled log | Run to cap with watchdog enabled | The run completes at its cap; no hang. |
| 4 | A throwing script still rethrows the original exception, not a timeout | **Supported** | `ExecuteChooseMoveDirect_GivenScriptThatThrowsAndDirectTimeoutEnabled_ExpectedRethrowsOriginalException` passed | Throwing script with the watchdog enabled | The test observes `InvalidOperationException("script-boom")`. |
| 5 | New direct-timeout tests + existing script-host tests pass 9/9 | **Supported** | `dotnet test ... --filter "BonesStrategyScriptHostDirectTimeoutTests|BonesStrategyScriptHostTests"` -> 9 passed / 0 failed | Run the class filters | All pass. |

Supported: 5; Falsified: 0; Unresolved: 0.

Residual risk (not a claim): the timed-out thread is abandoned as a background thread and is not
killed; process isolation remains open (EVD-017, RISK-005).
