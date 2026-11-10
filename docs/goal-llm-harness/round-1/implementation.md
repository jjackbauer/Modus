# Round 1 - WP-1 and WP-2 Implementation

Scope: the optional refinement-policy seam on `TrainingLoop.Run` and the new
`Wip.Tholdem.ModelHarness` transcript/replay/parser project, with new tests. WP-3 to WP-7 were out
of scope for this round. No live model call and no external network call was made by this work.

## (a) Test-first command and the observed failing output

The new test files were written before any implementation. The new project contained only its
`.csproj` (so the `ProjectReference` could be wired), and `Wip.Tholdem.ModelHarness` had no source
files yet. The exact test-first command was:

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed output (exit code 1):

```
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\LlmProposalParserTests.cs(1,19): error CS0234: The type or namespace name 'ModelHarness' does not exist in the namespace 'Wip.Tholdem' (are you missing an assembly reference?)
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\LlmTranscriptTests.cs(5,19): error CS0234: The type or namespace name 'ModelHarness' does not exist in the namespace 'Wip.Tholdem' (are you missing an assembly reference?)
Build FAILED.
    0 Warning(s)
    2 Error(s)
```

After the transcript/replay/parser sources existed but before the `TrainingLoop` seam was added, the
same command produced the seam-specific red state (exit code 1):

```
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\TrainingLoopRefinementPolicySeamTests.cs(33,49): error CS1739: The best overload for 'Run' does not have a parameter named 'refinementPolicy' [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\TrainingLoopRefinementPolicySeamTests.cs(74,17): error CS1739: The best overload for 'Run' does not have a parameter named 'refinementPolicy' [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
Build FAILED.
```

No new-behaviour test could run before the API existed; the compile refusals are the observed red
state for this round.

## (b) Build and test commands with observed summaries

| # | Command | Observed summary |
|---|---|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (baseline, before edits) | `Build succeeded.` / `0 Warning(s)` / `0 Error(s)` |
| 2 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` (baseline) | `TOTAL 394 PASSED 394 FAILED 0 SKIPPED 0` |
| 3 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (test-first red, no ModelHarness sources) | `Build FAILED.` / `2 Error(s)` (both CS0234, see (a)) |
| 4 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (sources present, seam absent) | `Build FAILED.` / 2 x CS1739 (see (a)) |
| 5 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (after the seam) | `Build succeeded.` / `0 Error(s)`; 14 warnings, all in pre-existing files |
| 6 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release -t:Rebuild` (clean rebuild) | `Build succeeded.` / `16 Warning(s)` / `0 Error(s)` |
| 7 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Wip.Tholdem.Tests.ModelHarness` | `TOTAL 10 PASSED 10 FAILED 0 SKIPPED 0` |
| 8 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` (final, after a clean rebuild) | `TOTAL 404 PASSED 404 FAILED 0 SKIPPED 0` |

The final suite total is the baseline 394 plus the 10 new tests; 0 failed and 0 skipped. The full
suite was run twice after the last code change and both runs printed the same final line.

### Newly emitted build warnings

The baseline build at row 1 reported 0 warnings. The clean rebuild (`-t:Rebuild`, row 6) emits
16 warnings. None are in
`WIP/Wip.Tholdem.ModelHarness` or `WIP/Wip.Tholdem.Tests/ModelHarness`:

- `WIP/Wip.Runtime/Runtime/AgentExecutor.cs:143` - CS8425 (async iterator cancellation token is not
  decorated with `EnumeratorCancellation`).
- `WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs:125` - CA2024 (`reader.EndOfStream` in an async method).
- `WIP/Wip.Tholdem.Host/Commands/TrainCommand.cs:54,90` - CS8602 (2 warnings).
- `WIP/Wip.Tholdem.Tests/Learning/TrainingLoopTests.cs:113-114,199,211-212,218` - CS8602 / CS8604
  (9 warnings across those lines).
- `WIP/Wip.Tholdem.Tests/Learning/LearningWebIntegrationTests.cs:69,70` - CS8602 (2 warnings).
- `WIP/Wip.Tholdem.Tests/Training/ThreatControlTests.cs:136` - xUnit2013 (1 warning).

These warnings are in files outside this round's scope and were not changed.

## (c) Files created or changed

| File | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | Added optional `IRefinementPolicy? refinementPolicy = null` (lines 54-61) and the branch that uses the supplied policy or lazily constructs today's `SignatureDrivenRefinementPolicy` (lines 193-210). |
| `WIP/Wip.Tholdem.ModelHarness/Wip.Tholdem.ModelHarness.csproj` | New net10.0 library, ImplicitUsings/Nullable enabled, references `Wip.Tholdem.Agents` and `Wip.Runtime`. |
| `WIP/Wip.Tholdem.ModelHarness/LlmTranscript.cs` | Canonical record with the exact member order, `LlmUsage`, the eight-outcome vocabulary, `sha256:<lowercase hex>` hashing and the stable `LlmTranscriptException`. |
| `WIP/Wip.Tholdem.ModelHarness/LlmTranscriptStore.cs` | Append-only `llm-transcripts.jsonl` writer and all-or-nothing `ReadAll` that refuses BOM/invalid UTF-8, non-canonical lines and hash mismatches. |
| `WIP/Wip.Tholdem.ModelHarness/LlmRedaction.cs` | Replaces `sk-`, `Bearer ` and `DEEPSEEK_API_KEY=` secret-shaped spans with `[redacted]` before hashing/storage. |
| `WIP/Wip.Tholdem.ModelHarness/LlmProposalParser.cs` | Deterministic single-object parser: required finite numbers, clamping with recorded adjustments, optional skills/memory/reason, stable refusal codes, unknown top-level fields ignored. |
| `WIP/Wip.Tholdem.ModelHarness/LlmTranscriptReplay.cs` | `Propose(LlmTranscript)` parses the recorded response; the type holds and calls no model. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/TrainingLoopRefinementPolicySeamTests.cs` | `TrainingLoop_GivenSuppliedRefinementPolicy_ExpectedPolicyUsedForEveryIteration` and `TrainingLoop_GivenNullPolicy_ExpectedSameArtifactsAsExplicitDeterministicPolicy`. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LlmTranscriptTests.cs` | `Transcript_GivenRecordedResponse_ExpectedReplayWithoutModelCall`, `Transcript_GivenRoundTrip_ExpectedCanonicalHashesAndNoTamper` and `Transcript_GivenSecretShapedText_ExpectedRedactionAndNoKey`. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LlmProposalParserTests.cs` | The five parser tests (valid genome, clamping, missing number, unknown fields, skills). |
| `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` | Added the `Wip.Tholdem.ModelHarness` ProjectReference only. |
| `Wip.slnx` | Added `WIP/Wip.Tholdem.ModelHarness/Wip.Tholdem.ModelHarness.csproj` under `/WIP/Tholdem/` only. |
| `docs/goal-llm-harness/round-1/implementation.md` | This report. |

All new text files were verified to start with the expected ASCII first bytes and no UTF-8 BOM
(PowerShell byte read: `[System.IO.File]::ReadAllBytes`; for example `LlmTranscript.cs` starts
`75 73 69` and the `.csproj` starts `3C 50 72`).

## (d) Stub, TODO and NotImplementedException confirmation

A content search over both new directories returned no match. The search used the workspace grep
tool with the pattern `TODO|NotImplemented|not implemented|stub`, once with path
`WIP/Wip.Tholdem.ModelHarness` and once with path `WIP/Wip.Tholdem.Tests/ModelHarness`.

Observed result: zero matches in `WIP/Wip.Tholdem.ModelHarness` and zero matches in
`WIP/Wip.Tholdem.Tests/ModelHarness`. Every delivered member has a working body; the seam is a real
policy call, the store writes and validates real lines, the parser returns real genomes and replay
parses the recorded text. The only test double is the test-local
`KnownGenomeRefinementPolicy` (`TrainingLoopRefinementPolicySeamTests.cs:105-119`), which exists
precisely to prove that a supplied policy drives the loop.

## (e) Anything not made to pass

Nothing in this round's scope is failing. The final observed suite line is:

```
TOTAL 404 PASSED 404 FAILED 0 SKIPPED 0
```

WP-3 (real `LlmRefinementPolicy`), WP-4 (live gate/secrets) and WP-5 to WP-7 (gate run, ponder
agent, live evidence) were out of scope for this round and were not attempted. No live model call or
external network call was made by this work.
