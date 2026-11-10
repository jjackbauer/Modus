# Round 7 — Offline transcript/replay and Ponder tool-path evidence

**Date**: 2026-11-09
**Scope**: three new test files under `WIP/Wip.Tholdem.Tests/ModelHarness/` and this report. No product file, existing test, csproj or `Wip.slnx` was edited.

## 1. Test-first execution and the observed red evidence

The three test files were written first, then the project was built and each class was executed
before this document existed. First execution (each per-class run exited 0):

~~~text
=== filter=PonderToolPathTests
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
=== filter=BudgetTests
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
=== filter=LiveReplayTests
TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0
~~~

Read honestly: **none of the three new tests failed on its first execution**. The product bodies
they exercise already exist (this round changes no product code), so there was no implementation
gap to turn red, and I did not manufacture a failure. The only red test in the suite remains the
mandated `Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall` gate (Section 5).

To show the new assertions are coupled to real observed output rather than vacuous, I ran a
temporary mutation check (the same practice as the round-6 validator check): one expected value in
each new test was changed to a wrong value, the assembly rebuilt, and each class re-run. Observed
(each filter exited 1):

~~~text
=== filter=PonderToolPathTests
FAIL PonderToolPathTests.Ponder_GivenToolCallingModel_ExpectedToolResultAndLegalAction: EqualException: Assert.Equal() Failure: Values differ
Expected: 0.25
Actual:   0.20000000000000001
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
=== filter=BudgetTests
FAIL BudgetTests.LlmRefinementPolicy_GivenExpiredWallClock_ExpectedBudgetExceededWithoutCall: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "The refinement wall-clock budget was exce"···
Not found: "no-such-budget-token"
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
=== filter=LiveReplayTests
FAIL LiveReplayTests.Transcript_GivenRecordedLiveArmTranscripts_ExpectedReplayMatchesPersistedChallengers: TrueException: raiseThreshold differs from the persisted artifact for challenger 'tholdem-strategy-v2-c4c15937' at iteration 1.
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
~~~

The Ponder failure prints the actual breaking-even equity as `0.20000000000000001` because xUnit
formats the double with round-trip precision; that is the same 64-bit double as the literal `0.2`
(the restored assertion passes), and it is the double produced by `25 / (100 + 25)`
(`WIP/Wip.Tholdem.Agents/Runtime/PotOddsTool.cs:36-40`). The full budget error text begins
"The refinement wall-clock budget was exceeded: ... MaxWallClock=..." (the restored
`Assert.Contains("MaxWallClock", ...)` at `BudgetTests.cs:47` passes), and the replay failure names
the exact challenger and iteration.

The mutations were reverted with `StrReplace`-equivalent targeted edits. Every file was restored
byte-for-byte; SHA-256 before the mutation and after the restore are identical:

| File | SHA-256 before mutation | SHA-256 after restore |
|---|---|---|
| `PonderToolPathTests.cs` | `3b538a55149f55610a57776c0ba8458f339f2d5aea51aeb5e70873ca87dc86e9` | same |
| `BudgetTests.cs` | `48ea12a376ca71953699024ffdd9b1644c6ec313014c8fe82b8371841f72ce51` | same |
| `LiveReplayTests.cs` | `4a12bbe43ee10ea34436b2f5414f311fdda47eb0012cac67360e6244252043aa` | same |

The restored assembly was rebuilt and the three classes re-ran green (Section 2, rows 11-13).

## 2. Every build/test command and its observed result

Commands, in order. `<runner>` = `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe`;
`<dll>` = `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`.

| # | Command | Exit | Observed result |
|---:|---|---:|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (baseline, before the new files) | 0 | `Build succeeded.` / `0 Warning(s)` / `0 Error(s)` (projects up-to-date); no warning lines |
| 2 | `<runner> <dll>` (baseline) | 1 | `TOTAL 439 PASSED 438 FAILED 1 SKIPPED 0`; failure = the mandated gate |
| 3 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (new files) | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)`; the 12 warnings are pre-existing (Learning/TrainingLoopTests.cs, Learning/LearningWebIntegrationTests.cs, Training/ThreatControlTests.cs); a filter over the build log for the three new file names printed nothing |
| 4 | `<runner> <dll> PonderToolPathTests` (first execution) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 5 | `<runner> <dll> BudgetTests` (first execution) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 6 | `<runner> <dll> LiveReplayTests` (first execution) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 7 | `dotnet build ... -c Release` (temporary wrong expectations) | 0 | `Build succeeded.` |
| 8 | `<runner> <dll> ` filters for the three classes (temporary wrong expectations) | 1 each | `TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0` each; outputs quoted in Section 1 |
| 9 | Targeted revert edits, then SHA-256 recheck | 0 | Hashes match the pre-mutation values in the table above |
| 10 | `dotnet build ... -c Release` (after restore) | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)`; no warning in the new files |
| 11 | `<runner> <dll> PonderToolPathTests` (final) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 12 | `<runner> <dll> BudgetTests` (final) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 13 | `<runner> <dll> LiveReplayTests` (final) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 14 | `<runner> <dll> ModelHarness` (whole namespace) | 1 | `TOTAL 48 PASSED 47 FAILED 1 SKIPPED 0`; the one failure is the mandated gate |
| 15 | `<runner> <dll>` (final full suite) | 1 | `TOTAL 442 PASSED 441 FAILED 1 SKIPPED 0`; the one failure is the mandated gate |

Baseline to final: 439 + 3 new tests = 442 total, with exactly the same single failure and no
other failure or skip.

## 3. The three new tests and what each pins

### 3.1 `WIP/Wip.Tholdem.Tests/ModelHarness/PonderToolPathTests.cs`

Test `Ponder_GivenToolCallingModel_ExpectedToolResultAndLegalAction` at line 27.

- The pipeline is composed by `RuntimePipeline.Create` at line 39 — the same composition helper the
  runtime tests use (`RuntimeAgentTestSupport.cs:194-229`): it registers the real `EquityTool`,
  `PotOddsTool`, `HandHistoryTool` and `OpponentStatsTool` (`:209-212`), the real
  `RuntimeToolInvoker` over `TholdemToolCatalog.CreateDescriptors()` (`:216`), the real
  `AgentExecutor` (`:217`) and the real `TholdemRuntimeAgent` (`:220-226`). The only test double is
  the chat model (`ScriptedChatModel`, `RuntimeAgentTestSupport.cs:22-59`).
- `Assert.Contains(ActionKind.Fold, observation.LegalActionKinds)` at line 30 verifies that fold is
  legal for `RuntimeAgentTestSupport.Observation()` before the scripted final message
  `{"action":"fold"}` (line 38) is used. The scripted first message is an assistant tool call named
  `pot-odds` with `{"pot":100,"amountToCall":25}` (lines 32-37).
- Line 46 asserts exactly 2 model requests. Lines 48-50 select exactly one `ChatRole.Tool` message
  from the second request. Lines 51-55 parse its content as JSON and assert
  `breakEvenEquity == 0.2`, `callAmount == 25` and `potOdds == 0.2`. The member names are the real
  invoker serialization: `RuntimeToolInvoker` serializes with `JsonSerializerDefaults.Web`
  (`WIP/Wip.Runtime/Runtime/RuntimeToolInvoker.cs:22-25`, `:85`, `:167-180`), which camel-cases the
  `PotOddsResult` record's members (`WIP/Wip.Tholdem.Agents/Runtime/PotOddsTool.cs:7`), and
  `PotOddsTool` computes `amountToCall / (pot + amountToCall) = 25 / 125 = 0.2` (`:36-40`).
- Line 57 asserts the returned action is exactly `PokerAction.Fold()`; line 58 asserts it is one of
  the observation's legal kinds; line 59 asserts `pipeline.Agent.Violations` is empty. Lines 61-66
  assert the outgoing request `Tools` of both requests are exactly
  `equity-calculator, pot-odds, hand-history-query, opponent-statistics`.
- This proves the agent's four-tool invocation path end to end with a scripted model; it makes no
  network call and no live-model claim.

### 3.2 `WIP/Wip.Tholdem.Tests/ModelHarness/BudgetTests.cs`

Test `LlmRefinementPolicy_GivenExpiredWallClock_ExpectedBudgetExceededWithoutCall` at line 16.

- A fresh temp data directory (`line 18`, `AgentTestSupport.CreateTempDirectory`), a
  `RecordingChatModel` (`line 21`), a real `LlmTranscriptStore` (`line 22`) and the fake live
  environment lookup `THOLDEM_LIVE_MODEL=1` / `DEEPSEEK_API_KEY=fake-test-key` (lines 55-60).
- `LlmRefinerOptions` (lines 23-30) sets `MaxWallClock: TimeSpan.FromTicks(1)`, which is valid
  because the option only requires a positive value (`Wip.Tholdem.ModelHarness/LlmRefinerOptions.cs:45-48`).
  `MaxCallsPerRun` is 8, so the call-budget branch (`LlmRefinementPolicy.cs:241-262`) cannot
  intercept; the wall-clock branch is the one under test (`:264-283`).
- The minimal `RefinementContext` (lines 62-66) uses the default genome, the required
  `SessionAnalysis(120, 0.48, -3.5, 1.7, 0.21, 0.0)`, `SessionSignatures.Empty` and iteration 1.
- Observed through the passing assertions: the exception's `Code` is `"budget-exceeded"` (line 41),
  `model.CallCount` is 0 (line 42), and exactly one transcript exists (line 43) with iteration 1,
  correlation id `llm-1`, outcome `LlmTranscriptOutcomes.BudgetExceeded` and an error containing
  `MaxWallClock` (lines 44-47). This closes the wall-clock branch of M-09.

### 3.3 `WIP/Wip.Tholdem.Tests/ModelHarness/LiveReplayTests.cs`

Test `Transcript_GivenRecordedLiveArmTranscripts_ExpectedReplayMatchesPersistedChallengers` at
line 24.

- Paths are resolved from `AgentTestSupport.RepositoryRoot()` (line 26): the arm directory
  `.tmp/goal-llm-harness/arms/arm-llm` (line 27), its `llm-transcripts.jsonl` (line 28) and the
  report `docs/goal-llm-harness/round-3/llm-arm-report.json` (line 29). Missing inputs fail with a
  message that names the operator run (lines 31-38); the operator command is at lines 17-21.
- Lines 40-41 open the transcripts through `LlmTranscriptStore.ReadAll()` and assert exactly 10
  records. Lines 42-57 assert every record has outcome `ok`, served model `deepseek-flash`, a
  non-empty response, `promptTokens > 0` and `latencyMilliseconds >= 0`. `ReadAll` itself validates
  canonical form, schema/outcome vocabulary and prompt/response hashes
  (`Wip/Wip.Tholdem.ModelHarness/LlmTranscriptStore.cs:53-102`, `:146-159`).
- Lines 59-61 parse the report and assert exactly 10 iterations. For each report iteration
  (lines 63-93) the test finds the transcript with correlation id `llm-<iteration>`, reads the
  persisted challenger `strategies/<challengerId>.json`, replays the record with
  `new LlmTranscriptReplay().Propose(record)` and compares the replayed genome with the artifact.
- The comparison at lines 98-120 checks the four numerics by exact `==` against
  `parameters.raiseThreshold/callThreshold/bluffFrequency/sizingMultiplier`, and lines 122-162
  check the skills exactly (both sides absent, or all six skill fields equal) — the same comparison
  shape as `.tmp/goal-llm-harness/arms/Program.cs` `CandidateMatchesProposal`/`SkillsMatch`
  (`:440-488`). Observed in the persisted data: every one of the ten replayed proposals and every
  challenger artifact carries no `skills` member, so the both-absent branch is the one exercised;
  the four numeric comparisons run for every iteration.
- `LlmTranscriptReplay` holds no model and makes no call (`LlmTranscriptReplay.cs:8-15`), and the
  test constructs no chat model, so replaying the live run costs zero model calls and no network.

## 4. Files created

| File | Tests | Asserted behaviour | Bytes / SHA-256 |
|---|---:|---|---|
| `WIP/Wip.Tholdem.Tests/ModelHarness/PonderToolPathTests.cs` | 1 | Scripted tool call to real `pot-odds` returns `breakEvenEquity=0.2` / `callAmount=25` in the one tool message of request 2; scripted legal fold returned; no violations; both requests expose exactly the four tools. | 2537 / `3b538a…86e9` |
| `WIP/Wip.Tholdem.Tests/ModelHarness/BudgetTests.cs` | 1 | Expired `MaxWallClock` refuses with `budget-exceeded`, 0 model calls, one `budget-exceeded` transcript whose error names `MaxWallClock`. | 2475 / `48ea12…ce51` |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LiveReplayTests.cs` | 1 | Ten live-arm transcripts (`ok`, `deepseek-flash`) replay offline; all ten replayed genomes match the persisted challenger artifacts' four numerics and skills. | 8335 / `4a12bb…43aa` |
| `docs/goal-llm-harness/round-7/implementation.md` | — | This report. | — |

All three test files are UTF-8 without a BOM (first bytes `75 73 69`) and end with a single `0A`
newline. The three test classes add exactly 3 tests to the 439-test baseline.

## 5. The one expected failure (unchanged)

~~~text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
TOTAL 442 PASSED 441 FAILED 1 SKIPPED 0
~~~

`WIP/Wip.Tholdem.Tests/ModelHarness/PonderEvidenceTests.cs:80-99` was not edited, skipped,
deleted or weakened. It remains the measured D6 gate.

## 6. Anything not passing, and scope/evidence notes

- The only failing test is the mandated gate above. No other test fails and none is skipped.
- No product source or existing test was modified. A modification-time scan after the session
  start found only the three new test files:

~~~text
15:15:07 WIP\Wip.Tholdem.Tests\ModelHarness\PonderToolPathTests.cs
15:15:07 WIP\Wip.Tholdem.Tests\ModelHarness\BudgetTests.cs
15:15:07 WIP\Wip.Tholdem.Tests\ModelHarness\LiveReplayTests.cs
~~~

  Command: `$cut=(Get-Date).AddMinutes(-40); Get-ChildItem WIP -Recurse -Include *.cs -File |
  Where-Object { $_.LastWriteTime -gt $cut -and $_.FullName -notmatch '\\(bin|obj)\\' } |
  Sort-Object LastWriteTime` plus a time-and-path projection. The scan printed five files: the
  three 15:15:07 files above plus `ProposalSurfaceTests.cs` (15:03:59) and
  `BehaviorProofComplianceTests.cs` (15:05:36), which are the two round-6 test files named in
  `docs/goal-llm-harness/round-6/implementation.md:71-74`; both timestamps predate this session's
  first write at 15:15:07.
- No network call and no live model call was made in this round. The only environment inputs are
  the literal fake lookup values in `BudgetTests.cs:55-60`; no secret file was opened and no key
  value was read or printed.
- Not observed (?): the tests do not print the exact prompt/completion token sums; the replay test
  asserts only `promptTokens > 0` per record. The live-arm run's own recorded totals remain the
  round-3 evidence.
- The `0.20000000000000001` text in Section 1 is xUnit's formatting of the actual double in its
  failure message; the same double satisfies `== 0.2` once the mutation is reverted (observed by
  the green rerun).
