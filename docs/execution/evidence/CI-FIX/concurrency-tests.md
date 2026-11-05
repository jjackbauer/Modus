# CI fix — deterministic BonesLoopState concurrency tests (2026-11-05)

**Work package:** unblock the Tests CI offline gate after the cadence fix landed.
**Status:** fix verified locally; remote confirmation recorded in `docs/execution/state.json`.

## Symptom

Tests CI run `42028137512` on `22bcdd5` failed `wip-offline-tests` at
“Gate known failures, zero tests, and test counts” with two **unwaived** failures in
`Wip.Bones.Host.Tests`:

```
Wip.Bones.Host.Tests.BonesLoopStateConcurrencyTests.BonesLoopState_GivenConcurrentReadWrite_SeesCoherentIsRunning
Wip.Bones.Host.Tests.BonesLoopStateConcurrencyTests.BonesLoopState_GivenConcurrentIncrement_SeesMonotonicIterationCount
```

No WIP product code changed in `22bcdd5` (only the Modus migration test, the Modus CI
diagnostics step and run records); the previous HEAD was green. This is a pre-existing test-design
flake surfaced by runner scheduling.

## Root cause (reproduced)

Both tests started a writer and a reader on a `Barrier(2)` and then ran a fixed 1,000,000
iterations in each task. On a loaded single-core runner the reader can run to completion before the
writer is scheduled:

- the coherence test observes only `false` → `Reader should have observed IsRunning=true at least once`;
- the monotonicity test observes only the initial `0` → `maxObserved > 0` fails.

Reproduction: `taskset -c 0`, one busy loop, `DOTNET_PROCESSOR_COUNT=1`, 4 runs → **4/4 failures**
(`.tmp/concurrency-repro/conc-1..4.trx`, first message above).

## Fix

`WIP/Wip.Bones.Host.Tests/BonesLoopStateConcurrencyTests.cs`:

- **`SeesCoherentIsRunning`**: the writer alternates values until a stop signal; the reader waits
  (≤ 10 s) until it has observed **both** `true` and `false`, yielding between reads. The two
  coercions are the same assertions as before.
- **`SeesMonotonicIterationCount`**: the writer publishes `IterationCount = 1` and signals before
  incrementing to 1,000,000; the reader waits (≤ 10 s) for that signal, then checks monotonicity.
  The final-value and monotonicity assertions are unchanged.

No assertion was removed or relaxed; no retry, skip or waiver was added.

## Verification

| Case | Method | Result |
|---|---|---|
| Fixed class, failing condition | 15 runs, one core, busy loop | **15/15 pass** |
| Coherence violation injected | clone-only writer always `true` | **fails**: `Reader should have observed IsRunning=false at least once` |
| Monotonicity violation injected | clone-only writer assigns `1_000_000 - i` | **fails**: `Assert.Equal() Failure` |
| Full Host suite + 16-project gate | see `docs/execution/harness/evidence.md` EVD-043 | recorded there |

## Environment note

Runs used the WSL Ubuntu clone at the same commit because Windows-local VSTest is blocked in this
session (`Win32Exception (5)` in the testhost parent-process callback). CI remains authoritative.
