# Round 2 - WP-3 Live Refiner Policy

Scope: the live `LlmRefinementPolicy` outside the Learning directory, with the M-02 prompt, the M-03
schema and one-repair path, the M-07 live-gate refusal and the M-09 call budget. No live model call
and no network call was made: every policy test injects the scripted `RecordingChatModel` and the
prompt test calls the pure builder.

## (a) Test-first command and the observed failing output

The three test files were written first:
`LlmRefinementPolicyTests.cs`, `LlmPromptBuilderTests.cs` and `RecordingChatModel.cs`. No
production type existed yet, so the exact test-first command was:

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed output (exit code 1):

```
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\LlmRefinementPolicyTests.cs(458,20): error CS0246: The type or namespace name 'LlmRefinerOptions' could not be found (are you missing a using directive or an assembly reference?) [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
Build FAILED.
    0 Warning(s)
```

To obtain a runnable red state as well, the three missing production files were then added as
signature stubs whose bodies threw `NotImplementedException`, and the filtered suite was run:

```
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll LlmRefinementPolicy
```

Observed output (exit code 1; representative lines):

```
FAIL LlmRefinementPolicyTests.LlmRefinementPolicy_GivenValidJson_ExpectedExactGenomeAndCanonicalTranscript: NotImplementedException: The method or operation is not implemented.
FAIL LlmRefinementPolicyTests.LlmRefinementPolicy_GivenMissingLiveKey_ExpectedRefusalBeforeAnyCall: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
...
TOTAL 13 PASSED 0 FAILED 13 SKIPPED 0
```

The stubs were then replaced by the real bodies reported in (c). No stub remains; the final grep in
(d) is clean.

## (b) Build and test commands with observed summaries

| # | Command | Observed summary |
|---|---|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (baseline, before edits) | `Build succeeded.` / `0 Warning(s)` / `0 Error(s)` |
| 2 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` (baseline) | `TOTAL 404 PASSED 404 FAILED 0 SKIPPED 0` |
| 3 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (tests only, no production types) | `Build FAILED.` / 1 x CS0246 (see (a)) |
| 4 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (signature stubs present) | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)` |
| 5 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll LlmRefinementPolicy` (against stubs) | `TOTAL 13 PASSED 0 FAILED 13 SKIPPED 0` |
| 6 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (real bodies) | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)`; new-file warnings: none |
| 7 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll LlmRefinementPolicy` (real bodies) | `TOTAL 13 PASSED 13 FAILED 0 SKIPPED 0` |
| 8 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll LlmPromptBuilder` | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 9 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` (final clean rebuild) | `Build succeeded.` / `16 Warning(s)` / `0 Error(s)`; `NO_WARNINGS_IN_NEW_FILES` |
| 10 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` (final full suite) | `TOTAL 418 PASSED 418 FAILED 0 SKIPPED 0` |

Rows 1-2 are the frozen baseline (404). The final total is 404 + 14 new tests = 418, with 0 failed
and 0 skipped. Row 10 was run after the row-9 clean rebuild.

### Newly emitted build warnings

The clean rebuild reports 16 warnings. A warning filter over `Wip.Tholdem.ModelHarness`, 
`ModelHarness\\Llm` and `ModelHarness\\Recording` printed `NO_WARNINGS_IN_NEW_FILES`. All 16 are
pre-existing and outside scope: `WIP/Wip.Runtime/Runtime/AgentExecutor.cs:143` (CS8425),
`WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs:125` (CA2024),
`WIP/Wip.Tholdem.Host/Commands/TrainCommand.cs:54,90` (2 x CS8602),
`WIP/Wip.Tholdem.Tests/Learning/TrainingLoopTests.cs:113-114,199,211-212,218` (9 x CS8602/CS8604),
`WIP/Wip.Tholdem.Tests/Learning/LearningWebIntegrationTests.cs:69,70` (2 x CS8602) and
`WIP/Wip.Tholdem.Tests/Training/ThreatControlTests.cs:136` (xUnit2013).

## (c) Files created

All files are UTF-8 without BOM (verified with `[System.IO.File]::ReadAllBytes`: the C# files start
`117,115,105` or `110,97,109`, and this report starts `35,32,82`).

| File | Purpose |
|---|---|
| `WIP/Wip.Tholdem.ModelHarness/LlmRefinerOptions.cs` | Validated options record: non-empty model id, positive tokens/timeout/calls, temperature in [0,2], 0..1 repair retries (constructor at line 6). |
| `WIP/Wip.Tholdem.ModelHarness/LlmPromptBuilder.cs` | `LlmPromptContext` plus the fixed `SystemPromptText` (line 32), the canonical ordered user JSON (line 46) and `BuildPrompt` (line 184). |
| `WIP/Wip.Tholdem.ModelHarness/LlmRefinementPolicy.cs` | `IRefinementPolicy` implementation: live gate (constructor line 45), M-02 context and transcription (line 81), budget (line 241), lineage pointer read (line 295) and `LlmRefinementRefusalException` (line 466). |
| `WIP/Wip.Tholdem.Tests/ModelHarness/RecordingChatModel.cs` | Scripted local `IChatModel`: records every request, dequeues responses/failures in order, configurable served model. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LlmRefinementPolicyTests.cs` | 13 policy tests: valid/clamped genome, repair and double failure, gate refusals, budget, poisoned memory, served-model mismatch, fresh-directory root lineage, HTTP 5xx/429 and cancellation. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LlmPromptBuilderTests.cs` | The full-context prompt test: section order, every value, bounds, and no seed/card/tools leakage. |
| `docs/goal-llm-harness/round-2/implementation.md` | This report. |

Required coverage is present at these test lines:
`LlmRefinementPolicyTests.cs:21` (valid transcript), `:74` (clamp with recorded original), `:122`
(one repair then success), `:160` (malformed twice), `:188` (missing key), `:216` (absent flag),
`:245` (budget), `:280` (poisoned memory), `:321` (served-model mismatch), `:345` (fresh-directory
root lineage, which answers OQ-003 for a fresh directory) and `LlmPromptBuilderTests.cs:18`. The
three extra tests are `:387` (HTTP 503), `:412` (HTTP 429) and `:436` (cancellation).

## (d) Stub, TODO and NotImplementedException confirmation

A regex content search (`TODO|NotImplementedException|stub|FIXME`) over the six new source files
returned:

```
CLEAN WIP/Wip.Tholdem.ModelHarness/LlmRefinerOptions.cs
CLEAN WIP/Wip.Tholdem.ModelHarness/LlmPromptBuilder.cs
CLEAN WIP/Wip.Tholdem.ModelHarness/LlmRefinementPolicy.cs
CLEAN WIP/Wip.Tholdem.Tests/ModelHarness/RecordingChatModel.cs
CLEAN WIP/Wip.Tholdem.Tests/ModelHarness/LlmRefinementPolicyTests.cs
CLEAN WIP/Wip.Tholdem.Tests/ModelHarness/LlmPromptBuilderTests.cs
```

Every delivered member has a working body. The only test double is `RecordingChatModel`
(`RecordingChatModel.cs:11`), which exists to prove the policy's request and transcript behaviour
without a network call.

## (e) Not made to pass, and deviations from the spec

Nothing in this round's scope is failing. The final observed line is:

```
TOTAL 418 PASSED 418 FAILED 0 SKIPPED 0
```

Deviations and clarifications, all deliberate:

1. `LlmRefinerOptions` is a `sealed record` with an explicit validating constructor and `init`
   properties instead of the positional primary-constructor form. The public constructor signature is
   identical (named arguments and `with` still work); body validation was the reason (spec permits
   "constructor body or a factory").
2. `MaxWallClock`, when supplied, is additionally required to be positive. The spec listed six
   validations and was silent on this one; refusing a non-positive wall clock is fail-closed.
3. The public `IRefinementPolicy.Propose` seam carries no `CancellationToken`. Therefore every
   `OperationCanceledException` observed is the per-call timeout and is recorded as
   `transport-timeout`; the spec's `"cancelled"` error (for a cancelled caller token) is unreachable
   through this seam rather than omitted by choice.
4. `LlmTranscript` has one `Prompt` field. The first transcript stores `SystemPromptText + "\n\n" +
   userText`; the repair transcript stores that same text followed by the first reply and the repair
   instruction, joined by blank lines, so the repaired conversation is recoverable from the record.
5. The bounds are serialized with the canonical numeric form `[0,1]`, `[0,1]`, `[0,1]`,
   `[0.25,3]` rather than the prose `[0.0,1.0]` / `[0.25,3.0]`. They are the same JSON numbers; the
   test asserts the parsed numeric arrays.
6. The incumbent's `skills` member is `null` when `StrategyGenome.Skills` is absent; the builder
   does not fabricate a derived skill set.
7. Three tests beyond the required list were added (HTTP 5xx, HTTP 429, cancellation) to observe the
   transport-failure transcript path of spec step (e).
