# CI fix — deterministic absolute-cadence gate (2026-11-05)

**Work package:** unblock the red Modus CI hard gate on `WIP`.
**Commit:** the commit carrying this file (test change + records).
**Status:** fix verified locally; remote CI confirmation is recorded in
`docs/execution/state.json` after the push.

## Symptom

Modus CI run `42003338267` on HEAD `4dc6559a8eb96ff4d0a16f36d4206c3cbbf6189e` failed at the hard
step **“Gate migration regression suite”** (`.github/workflows/modus-ci.yml:45-46`). The failure
annotation names:

```
Modus.Host.IntegrationTests.RuntimeAddedScheduledJobsAbsoluteGatesTests.ScheduledJobs_GivenRuntimeAddedTimerPlugin_ExpectedDeterministicRegistrationAndAbsoluteCadenceGates
```

Tests CI run `42003337898` on the same HEAD succeeded in all six jobs; the non-blocking legacy
suite step was skipped after the hard gate failed. The same test is the only failing test in the
three most recent Modus CI failures:

| Run | HEAD | Failing test |
|---|---|---|
| `41964636244` | `af089e6` | the scheduled-jobs cadence test |
| `41966815553` | `ee99821` | the scheduled-jobs cadence test |
| `42003338267` | `4dc6559` | the scheduled-jobs cadence test |

## Root cause (source + reproduction)

`AssemblyLifecycleHost.RegisterAndRunSchedules` invokes `ExecuteScheduledOperation` **synchronously
at registration** (`src/Modus.Host/Domain/Plugins/Host/AssemblyLifecycleHost.cs:141`) and only then
starts a `PeriodicTimer` inside `Task.Run` (`:147-154`). The test recorded that registration
execution as cadence sample 0 and asserted every consecutive gap against `interval ± max(250 ms,
20%)`.

The 0 → 1 gap is therefore **startup latency** (task scheduling, timer construction, first-use JIT,
DI resolution), not an interval between periodic ticks. On a contended CI runner it can exceed
550 ms while the steady-state cadence stays correct.

Primary reproduction on a clean Linux clone at `4dc6559`:

| Condition | Runs | Result |
|---|---|---|
| 24-core host, no load | 12 | 12 pass |
| 24-core host, 24 busy loops | 10 | 10 pass |
| 2 cores (`taskset -c 0,1`), 2 busy loops, `DOTNET_PROCESSOR_COUNT=2` | 8 | **1 failed** |

Observed failure (TRX kept at `.tmp/sched2-fail.trx`):

```
Recurring cadence outside tolerance. Expected interval 300ms +/- 250ms but observed 649.5054ms between run 0 and 1.
```

## Fix

`tests/Modus.Host.IntegrationTests/RuntimeAddedScheduledJobsAbsoluteGatesTests.cs`:

- sample 0 → 1 is bounded as **first-tick latency** (≤ 5 s, generous but finite);
- absolute cadence is asserted between **periodic ticks only** (index ≥ 2).

No tolerance was widened (still 300 ms ± 250 ms), no assertion was removed, no retry or waiver was
added. The gate still requires ≥ 5 recurring executions inside the observation window.

## Verification

| Case | Method | Result |
|---|---|---|
| Fixed test under 2-core contention | 10 runs, same `taskset`/load as the reproduction | **10/10 pass, 0 failures** |
| Wrong interval injected (600 ms instead of 300 ms) | clone-only `PeriodicTimer(TimeSpan.FromMilliseconds(600))` | **fails**: `observed 597.4609ms between run 1 and 2` |
| Scheduler never ticks | clone-only `PeriodicTimer(TimeSpan.FromHours(1))` | **fails**: `TimeoutException` waiting for 5 executions (2.8 s bound) |
| Full migration filter | `dotnet test ... --filter "MigrationRegression=true"` on the clone | **19/19 pass** |
| Encoding | `tools/encoding/Test-StagedTextFileEncoding.ps1` | pass (recorded at commit) |

The wrong-interval control failing at **run 1 → 2** shows the cadence assertion still detects a
bad scheduler independently of the startup sample; the no-ticks control shows the liveness
assertion still fires.

## Environment note

Local Windows `dotnet test`/`dotnet vstest` cannot start a testhost in this session: it aborts with
`Win32Exception (5)` in `DefaultEngineInvoker.SetParentProcessExitCallback`. Reproduction and
verification therefore ran on the WSL Ubuntu clean clone at the same commit; the TRX was read and
validated from Windows. This is a session environment limitation, not a repository defect and not a
waiver. CI remains the authoritative gate.

## Remote confirmation

| HEAD | Modus CI | Tests CI |
|---|---|---|
| `4dc6559` (pre-fix) | `42003338267` **failure** (this test) | `42003337898` success |
| post-fix HEAD | recorded in `docs/execution/state.json` after the push | recorded likewise |
