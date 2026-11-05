# CI fix — deterministic cadence gate (2026-11-05)

**Work package:** unblock the red Modus CI hard gate on `WIP`.
**Status:** fix verified locally (12/12 under the failing condition); remote confirmation is recorded
in `docs/execution/state.json` after the push.

## Symptom

Modus CI failed at the hard step **“Gate migration regression suite”**
(`.github/workflows/modus-ci.yml`) on every HEAD from `af089e6` through `5cea565`. The only failing
test in all three latest failures is:

```
Modus.Host.IntegrationTests.RuntimeAddedScheduledJobsAbsoluteGatesTests.ScheduledJobs_GivenRuntimeAddedTimerPlugin_ExpectedDeterministicRegistrationAndAbsoluteCadenceGates
```

| Run | HEAD | Failing test |
|---|---|---|
| `41964636244` | `af089e6` | the scheduled-jobs cadence test |
| `41966815553` | `ee99821` | the scheduled-jobs cadence test |
| `42003338267` | `4dc6559` | the scheduled-jobs cadence test |
| `42022906402` | `5cea565` (first fix) | the same test, still |

Tests CI was green on every one of those HEADs; this is a Modus-CI-only hard gate.

## Root cause 1 — registration sample counted as a cadence interval

`AssemblyLifecycleHost.RegisterAndRunSchedules` invokes `ExecuteScheduledOperation` **synchronously
at registration** (`src/Modus.Host/Domain/Plugins/Host/AssemblyLifecycleHost.cs:141`) and only then
starts a `PeriodicTimer` inside `Task.Run` (`:147-154`). The test recorded that registration
execution as cadence sample 0 and asserted the 0 → 1 gap as an interval; that gap is startup latency
(task scheduling, timer construction, first-use JIT, DI resolution).

Reproduced on a clean Linux clone at `4dc6559` under 2-core contention (1/8 runs):

```
Recurring cadence outside tolerance. Expected interval 300ms +/- 250ms but observed 649.5054ms between run 0 and 1.
```

First fix: bound sample 0 → 1 as first-tick latency (≤ 5 s) and assert cadence from index 2.
That shipped as `5cea565`; Modus CI still failed.

## Root cause 2 — wall-clock cadence is not observable under CPU starvation

After the first fix, the same test still failed under 2-core contention (1/8 and 1/12 runs). The
instrumented failure message captured the full gap sequence:

```
observed 10.5451ms between run 1 and 2. Gaps=[935.5, 10.5, 959.1, 166.0]
```

The runtime's own reference `PeriodicTimer` behaved the same way in the same run:

```
ControlGaps=[1.5, 248.8, 304.0]
```

Under thread-pool/CPU starvation the timer delivers catch-up bursts. Per-gap wall-clock precision
therefore cannot distinguish a scheduler defect from host starvation — the gate was asserting a
property the runtime does not provide on a loaded runner.

## Final fix — rate parity with a reference timer

`tests/Modus.Host.IntegrationTests/RuntimeAddedScheduledJobsAbsoluteGatesTests.cs`:

- a **reference `PeriodicTimer(300 ms)`** starts when `StartActivatedPlugins` returns and records its
  tick times during the same observation window;
- every job tick must be within **one tick** of the reference tick count at that instant
  (`|jobTickIndex − referenceTicksByThen| ≤ 1`), which is the actual cadence invariant and is
  immune to bursts that hit both timers;
- the first-tick latency is still bounded (≤ 5 s), liveness still requires ≥ 5 executions, the
  one-time schedule still must execute exactly once, and all registration/cadence/ownership/
  `source=scheduled` diagnostics assertions are unchanged;
- no nominal tolerance was widened into a free pass: a wrong configured interval changes the rate
  and fails.

## Verification (all commands run by the orchestrator)

| Case | Method | Result |
|---|---|---|
| **Fixed test under the failing condition** | 12 × full migration filter, `taskset -c 0,1`, two busy loops, `DOTNET_PROCESSOR_COUNT=2` | **12/12 pass** (0 fail) |
| Slow interval injected (600 ms) | clone-only `PeriodicTimer(TimeSpan.FromMilliseconds(600))` | **fails**: `Recurring tick 3 drifted … 5 reference ticks … (job ticks=4, reference ticks=8)` |
| Fast interval injected (150 ms) | clone-only `PeriodicTimer(TimeSpan.FromMilliseconds(150))` | **fails**: `Recurring tick 2 drifted … 0 reference ticks … (job ticks=4, reference ticks=2)` |
| Scheduler never ticks | clone-only `PeriodicTimer(TimeSpan.FromHours(1))` | **fails**: bounded wait canceled before 5 executions |
| Full migration filter on the clone | `dotnet test … --filter "MigrationRegression=true"` | **19/19 pass** (4 s) |
| First fix alone | 10 × single test under 2-core contention | 10/10 pass, but full-suite runs still failed → superseded |

## CI diagnostics improvement

The “Dump migration regression diagnostics” step now parses the TRX and emits each failed test's
**error message** as a GitHub annotation (previously only the test name). This makes a future flake
diagnosable from the public annotations endpoint without log access. The script was validated
against the real failing TRX above (it reproduces the `649.5054ms` message) and the workflow file
parses as YAML.

## Environment note

Local Windows `dotnet test`/`dotnet vstest` cannot start a testhost in this session: it aborts with
`Win32Exception (5)` in `DefaultEngineInvoker.SetParentProcessExitCallback`. Reproduction and
verification therefore ran on the WSL Ubuntu clean clone at the same commit; the TRX was read and
validated from Windows. This is a session limitation, not a repository defect and not a waiver.
CI remains the authoritative gate.

## Remote confirmation

| HEAD | Modus CI | Tests CI |
|---|---|---|
| `4dc6559` (pre-fix) | `42003338267` failure (this test) | `42003337898` success |
| `5cea565` (first fix) | `42022906402` failure (same test, burst) | `42022906370` success |
| final fix HEAD | recorded in `docs/execution/state.json` after the push | likewise |
