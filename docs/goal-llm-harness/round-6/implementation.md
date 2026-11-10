# Round 6 — Behavior-proof gate and M-05 inert-metadata proof

**Date**: 2026-11-09
**Scope**: `docs/goal-llm-harness/behavior-proof.md`, the two new `ModelHarness` test files, and this report. No product file, no existing test, no csproj, and no `Wip.slnx` was edited.

## 1. Test-first — the compliance gate failed before the document existed

The two new test files were created first, then the test project was built and the new filter was run before `docs/goal-llm-harness/behavior-proof.md` was written.

Command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll BehaviorProofCompliance
```

Observed (exit 1):

```text
FAIL BehaviorProofComplianceTests.BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts: FileNotFoundException: Could not find file 'C:\Users\ricar\dev\Modus\docs\goal-llm-harness\behavior-proof.md'.
FAIL BehaviorProofComplianceTests.BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem: FileNotFoundException: Could not find file 'C:\Users\ricar\dev\Modus\docs\goal-llm-harness\behavior-proof.md'.
TOTAL 3 PASSED 1 FAILED 2 SKIPPED 0
```

The metadata-only test (`BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant`) already passed because it supplies a synthetic in-memory table; tests 1 and 3 require the real document.

The M-05 test was created first as well and was green against the existing parser (no parser change was needed or made):

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll ProposalSurface
```

Observed (exit 0): `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0`.

## 2. Every build/test command and its observed result

| # | Command | Exit | Observed result |
|---:|---|---:|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` | 0 | Build succeeded, 12 warnings, 0 errors |
| 2 | `RunXunit <dll> BehaviorProofCompliance` (before the document) | 1 | `TOTAL 3 PASSED 1 FAILED 2 SKIPPED 0` (both real-document tests failed with `FileNotFoundException`) |
| 3 | `RunXunit <dll> ProposalSurface` | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 4 | `RunXunit <dll> BehaviorProofCompliance` (after the document) | 0 | `TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0` |
| 5 | `RunXunit <dll> ProposalSurface` (after the document) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 6 | Validator mutation build: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` | 0 | Build succeeded, 0 errors |
| 7 | Validator mutation run: `RunXunit <dll> BehaviorProofCompliance` | 1 | `TOTAL 3 PASSED 1 FAILED 2 SKIPPED 0` (tests 2 and 3 failed once the allowed-root check and the category validator were removed) |
| 8 | Rebuild after restoring the validator: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` | 0 | Build succeeded, 12 warnings, 0 errors |
| 9 | Restored filters: `RunXunit <dll> BehaviorProofCompliance` and `RunXunit <dll> ProposalSurface` | 0 | `TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0` and `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 10 | Full suite, first run: `RunXunit <dll>` | 1 | `TOTAL 439 PASSED 438 FAILED 1 SKIPPED 0` |
| 11 | Full suite, final run after restore and rebuild: `RunXunit <dll>` | 1 | `TOTAL 439 PASSED 438 FAILED 1 SKIPPED 0` |

`<dll>` is `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` and `RunXunit` is `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe`.

### Validator is not a stub — observed mutation check

The validator source was temporarily mutated in two places: the allowed-root check at `WIP/Wip.Tholdem.Tests/ModelHarness/BehaviorProofComplianceTests.cs:369` was made false, and `ValidateApiFocusedCategories` returned compliant without checking. The mutation run (row 7) failed both synthetic-input tests:

```text
FAIL BehaviorProofComplianceTests.BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts: FalseException: Assert.False() Failure
Expected: False
Actual:   True
FAIL BehaviorProofComplianceTests.BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "Expected exactly 15 requirement rows, fou"···
Not found: "M-01"
TOTAL 3 PASSED 1 FAILED 2 SKIPPED 0
```

The file was restored byte-for-byte (SHA-256 before and after: `9fa62750f98ff84b9c9b2f2a11ee6429cfebe8383c994f6c887e744c554d9fd4`; restore check printed `restored=True`), rebuilt, and re-ran green (row 9).

## 3. Files created

| File | What it contains |
|---|---|
| `docs/goal-llm-harness/behavior-proof.md` | One table with exactly the required seven columns; header at line 5 and 15 requirement rows at lines 7–21 |
| `WIP/Wip.Tholdem.Tests/ModelHarness/ProposalSurfaceTests.cs` | `LlmProposalParser_GivenPromptTextField_ExpectedIgnoredAsUnknown` at line 15: parses the injected `promptText` payload, asserts 0.61/0.39/0.18/1.4, `Genome.PromptText == StrategyGenome.DefaultPromptText`, empty adjustments and no exception |
| `WIP/Wip.Tholdem.Tests/ModelHarness/BehaviorProofComplianceTests.cs` | Three tests at lines 24, 37 and 58 plus the test-local `BehaviorProofValidator` at line 202 |
| `docs/goal-llm-harness/round-6/implementation.md` | This report |

All three new text files were verified UTF-8 without BOM (first bytes `75 73 69`, no `EF BB BF`).

### Behavior-proof content

- Exactly 15 unique rows, `M-01` through `M-15` (`behavior-proof.md:7-21`).
- The five category cells are non-empty and each names an existing test or artifact path for `M-01` (line 7), `M-04` (line 10), `M-07` (line 13) and `M-14` (line 20).
- `M-05` is backed by the new `ProposalSurfaceTests.cs` (line 11).
- `M-06` states the measured gap explicitly in its Negative cell at line 12: “Measured gap: 0 tool calls in the 33-turn live match”. The validator enforces that phrase (`BehaviorProofComplianceTests.cs:265-280`).
- Every Runtime proof path is under `WIP/Wip.Tholdem.Tests/`, `docs/goal-llm-harness/` or `.tmp/goal-llm-harness/`; the validator checks existence and non-zero length (`BehaviorProofComplianceTests.cs:331-361`, `:363-394`). No product source under `WIP/Wip.Tholdem.ModelHarness` or `WIP/Wip.Tholdem.Agents` is accepted (`BehaviorProofComplianceTests.cs:369-376`).

The validator parses the real markdown table (`BehaviorProofComplianceTests.cs:115-185`), reads paths only, and makes no network or model call.

## 4. Exact observed failure of the one expected red test

```text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
TOTAL 439 PASSED 438 FAILED 1 SKIPPED 0
```

`PonderEvidenceTests.cs:79-100` was not edited, skipped, deleted or weakened. The total moved from the pre-round 435 to 439 because four tests were added (3 compliance + 1 proposal surface).

## 5. Anything not passing

- The single failing test is still `PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall`; it is the measured D6 gate and remains red by design. The recorded live session has 0 tool calls in 33 model turns.
- No other test fails and none is skipped.
- `docs/goal-llm-harness/round-4/ponder-live.log` is UTF-16LE with a BOM (first bytes `FF FE`), so a UTF-8 text reader classifies it as binary. The compliance validator treats it as an opaque existing non-empty artifact; the M-06 gap is stated in the table rather than inferred from reading that file.
- No product code, existing test, csproj or `Wip.slnx` was modified; no network call or live model call was made in this round.
