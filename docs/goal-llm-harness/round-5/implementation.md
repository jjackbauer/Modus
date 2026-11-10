# Round 5 — Remaining model-harness tests

Status: ten tests in seven new files under `WIP/Wip.Tholdem.Tests/ModelHarness/`. The full suite is
**TOTAL 435 PASSED 434 FAILED 1 SKIPPED 0**; the single failure is the mandated
`Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall` (recorded session `summary.toolCalls=0`).
No product file and no existing test file was changed.

## 1. Test-first run and the observed failure

The seven files were written first, then the test project was built and the new classes executed, with
no product or existing-test change before or after that run (rounds 1-4 already ship the bodies these
tests assert). Commands:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll ModelHarness
```

Observed first execution of the new tests (exit 1):

```text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
TOTAL 41 PASSED 40 FAILED 1 SKIPPED 0
```

Read honestly: **nine of the ten new tests passed on their first execution**, because the product
bodies they assert were delivered in rounds 1-4 and this round forbids touching product code, so no
other red existed to observe. The one observed red is
`PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall`
(`WIP/Wip.Tholdem.Tests/ModelHarness/PonderEvidenceTests.cs:80-99`), which is the goal's outstanding
requirement and must stay red. I did not manufacture or claim any other failure.

Why the green tests are not vacuous (reasoning; each names a concrete literal or persisted byte, so a
stubbed body cannot satisfy it): the promotion test requires the pointer bytes to differ and to carry
`evaluationReportHash` equal to the persisted report hash (`PromotionPointerTests.cs:88-101`); a stub
that returned the incumbent genome would fail the challenger parameter literals (`:70-75`). The
failure test requires exactly one transcript with outcome `http-5xx` whose error text is the scripted
503 message, plus an empty evaluation directory (`ModelFailureTests.cs:47-80`); a refusal that is not
raised fails the `Assert.Throws`. The cancellation test requires an `OperationCanceledException`, a
zero-entry ledger and unchanged pointer bytes (`CancellationTests.cs:37-76`). The flag-off test pins a
SHA-256 literal (`DeterministicOffTests.cs:16-17`). The guard test pins the exact
`unknown-tool: no tool named 'run_code' is registered.` tool result (`PonderGuardTests.cs:107`).
Marked as reasoning (?): I did not stub a product body to observe these failures, because editing
product code is out of scope.

## 2. Build and test commands with observed TOTAL lines

Build (exit 0):

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed tail:

```text
  Wip.Tholdem.Tests -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll

Build succeeded.

    12 Warning(s)
    0 Error(s)

Time Elapsed 00:00:55.20
```

All twelve warnings are in pre-existing test files (`Learning/TrainingLoopTests.cs`,
`Learning/LearningWebIntegrationTests.cs`, `Training/ThreatControlTests.cs`); the seven new files
produce none.

Focused per-class filters (one runner invocation per class, wall clock measured around the runner):

```text
=== filter=PromotionPointerTests ms=2944 exit=0
TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0
=== filter=ModelFailureTests ms=1722 exit=0
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
=== filter=CancellationTests ms=1656 exit=0
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
=== filter=PonderEvidenceTests ms=1363 exit=1
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
TOTAL 2 PASSED 1 FAILED 1 SKIPPED 0
=== filter=PonderGuardTests ms=1898 exit=0
TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0
=== filter=DeterministicOffTests ms=3574 exit=0
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
=== filter=LearningIsolationTests ms=932 exit=0
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
```

Focused filter over the whole `ModelHarness` namespace (31 pre-existing + 10 new tests):

```text
TOTAL 41 PASSED 40 FAILED 1 SKIPPED 0
```

Full filter (no type filter):

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 435 PASSED 434 FAILED 1 SKIPPED 0
```

Baseline before this round was 425 passed / 0 failed; 425 + 10 new tests = 435 total, with exactly
the one expected failure and no other.

## 3. Files created

| File | Tests | Asserted behaviour |
|---|---|---|
| `WIP/Wip.Tholdem.Tests/ModelHarness/PromotionPointerTests.cs` | 2 | Verified LLM challenger promotes and moves the pointer with the report hash; an unjustified always-promote control moves nothing. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/ModelFailureTests.cs` | 1 | An HTTP 503 from the model is a `model-failure` refusal with one `http-5xx` transcript, no evaluation file and an unchanged pointer. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/CancellationTests.cs` | 1 | Cancellation inside `Decide` aborts before persistence: no pointer move, no evaluation file, no ledger row. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/PonderEvidenceTests.cs` | 2 | The recorded live session is live, model-sourced on every turn, transcript-canonical and catalog-exact; and its `summary.toolCalls` is at least one (expected to fail). |
| `WIP/Wip.Tholdem.Tests/ModelHarness/PonderGuardTests.cs` | 2 | The live gate refuses with zero HTTP sends and no session file; an injection in memory cannot add a tool or a permission. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/DeterministicOffTests.cs` | 1 | Two flag-off runs are byte-identical, write no transcript/harness directory, and the v1 champion artifact hashes to the pinned SHA-256. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LearningIsolationTests.cs` | 1 | The harness project exists; no learning source mentions `IChatModel`, `HttpClient`, `DeepSeek` or `ModelHarness`; the safety scan keeps its token list. |
| `docs/goal-llm-harness/round-5/implementation.md` | — | This report. |

Measured fixture results (observed through the passing assertions; the tests do not print values):

- The 0.80/0.40/0.10/1.0 challenger from the 0.70/0.70/0.12/1.0 start at seed 20261109 with
  24 seeds x 20 hands came back `Verified=true`, `Promoted=true`, reason `promoted`, and the pointer
  moved to the challenger with `evaluationReportHash` equal to the persisted report hash
  (`PromotionPointerTests.cs:57-101`). The exact lower bound was not printed by the test, so I do not
  claim the 10.87 figure; the assertion that it cleared the 5.0 margin is what passed.
- The unjustified control 0.60/0.55/0.18/1.15 did not clear the margin, so the verifier refused the
  always-promote verdict (`PromotionPointerTests.cs:141-154`).
- The deterministic champion artifact `strategies/tholdem-strategy-v1-f100e793.json` hashes to
  `6675d7975b69eafd7cd64f334e70ad5adbfa09fbcc32176a6098aa9649124c14`. The same value was computed
  independently from the canonical payload before the build, which also re-derived the id suffix
  `f100e793`.

## 4. Exact observed failure output of `Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall`

From the focused run and again in the full run, verbatim:

```text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
```

The assertion is `Assert.True(toolCalls >= 1, ...)` over
`.tmp/goal-llm-harness/ponder/live/ponder-session.json` `summary.toolCalls`, which is `0`
(`PonderEvidenceTests.cs:80-99`). It is not skipped, not conditional, not weakened, and its assertion
was not changed. The recorded session still reads `modelKind=live`, 20 hands, 33 model turns, 33
legal model actions and 0 tool calls.

## 5. Anything else not passing or not observed

- Only the mandated tool-call test fails. Nothing else is red in either the focused or the full run.
- Design note (inference ?): the failure and cancellation tests prepare the 0.70/0.70/0.12/1.0 start
  exactly as the promotion test does, because "the pointer bytes are unchanged" needs a pre-run
  pointer to compare; without one, `TrainingLoop.Run` would create the pointer itself and the
  comparison would be vacuous.
- Not observed: the exact lower bound and mean of the promotion evaluation (the test asserts the
  verdict, not the number), and any live model call — no network call and no secret file was used in
  this round.
- Encoding: all seven test files are UTF-8 without a BOM (checked: the first three bytes are not
  `EF BB BF`) and end with a single newline.
- Scope: a modification-time scan after the run listed exactly the seven new test files under
  `WIP/Wip.Tholdem.Tests` and no product source changed after the session start
  (`Get-ChildItem -Recurse WIP/Wip.Tholdem.ModelHarness,WIP/Wip.Tholdem.Agents,WIP/Wip.Tholdem.Host,WIP/Wip.Runtime -Filter *.cs | Where-Object LastWriteTime -gt 2026-11-09T13:15` printed nothing).
