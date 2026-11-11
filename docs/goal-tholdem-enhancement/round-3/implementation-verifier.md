# Implementation verifier — independent promotion verifier

Round 3, section 3: an independent second party that recomputes both paired reports and refuses
any promotion decision the recomputed reports do not justify.

## Files

- `WIP/Wip.Tholdem.Agents/Evaluation/PopulationPromotionVerifier.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/PopulationVerifierTests.cs` (new)
- `docs/goal-tholdem-enhancement/round-3/implementation-verifier.md` (new, this file)

No other file was edited.

## What the verifier does

`PopulationPromotionVerifier.Verify` applies the dispatched contract's numbered logic:

1. Null-checks every argument, including both delegates (`ArgumentNullException`).
2. Policy: `primary.PlanHash == plan.PlanHash` and
   `anchor.PlanHash == anchorPlan.PlanHash`, otherwise `false / verifier:policy-mismatch`.
   `EvaluationPlanV2.PlanHash` recomputes the content address from the canonical plan on every
   access, so this comparison recomputes the plan hashes rather than trusting a persisted value.
3. Calls both recompute delegates, each inside its own guard; any thrown exception is
   `false / verifier:recomputation-failed`.
4. Compares the recomputed `ReportHash` of each report with the claimed report's hash; a
   differing hash (or a null report) is `false / verifier:report-mismatch`.
5. Recomputes `PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan)` and compares
   `Verdict` and `Reason` with the supplied decision; any difference is
   `false / verifier:decision-mismatch`.
6. Otherwise `true / verifier:verified`.

The dispatched contract adds the fifth reason code `verifier:recomputation-failed` (the
design draft listed four) and the throwing-delegate test requires it, so the implementation carries
all five. The order above also follows the dispatched logic (policy before recomputation), which
fails fast before running fresh evaluations against a mismatched plan.

The verifier never trusts a persisted decision: an honest gate decision is only accepted after
both reports are reproduced byte-for-byte under the same plans.

## Test-first red (stub verifier)

The stub performed only the seven null checks and then returned
`false` with `reason = VerifiedReason` — every contract path was unimplemented.

Build:

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded. 0 Error(s)`.

Run:

```
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.PopulationVerifierTests
```

Observed (exit code 1):

```
FAIL PopulationVerifierTests.Verify_GivenAlwaysPromoteControl_ReturnsDecisionMismatch: EqualException: Assert.Equal() Failure: Strings differ
                    ↓ (pos 9)
Expected: "verifier:decision-mismatch"
Actual:   "verifier:verified"
                    ↑ (pos 9)
FAIL PopulationVerifierTests.Verify_GivenRecomputationThrows_ReturnsRecomputationFailed: EqualException: Assert.Equal() Failure: Strings differ
                    ↓ (pos 9)
Expected: "verifier:recomputation-failed"
Actual:   "verifier:verified"
                    ↑ (pos 9)
FAIL PopulationVerifierTests.Verify_GivenRecomputedReportsAgree_ReturnsVerified: TrueException: the verifier refused an honest verified decision: verifier:verified
FAIL PopulationVerifierTests.Verify_GivenTamperedReportHash_ReturnsReportMismatch: EqualException: Assert.Equal() Failure: Strings differ
                    ↓ (pos 9)
Expected: "verifier:report-mismatch"
Actual:   "verifier:verified"
                    ↑ (pos 9)
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

## Green (after implementation)

Build: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` — observed
`Build succeeded. 0 Error(s)` (0 warnings in the incremental build).

Verifier tests (same command as the red run), observed:

```
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Full suite:

```
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (exit code 0):

```
TOTAL 495 PASSED 495 FAILED 0 SKIPPED 0
```

495 = 491 pre-existing tests + 4 new verifier tests; no pre-existing test regressed.

## Stub removal

Observed content searches after implementation:

- `(?i)stub|NotImplementedException|TODO|FIXME` over
  `PopulationPromotionVerifier.cs`: no matches.
- `NotImplementedException` over `PopulationVerifierTests.cs`: no matches.

Every `return false` belongs to a named refusal branch
(`policy-mismatch`, `recomputation-failed`, `report-mismatch`, `decision-mismatch`); the only
success path returns `true` with `verifier:verified`.

## Scope and harness notes

- Created files only: the two sources above and this document. Modification times confirm the
  pre-existing working-tree changes (`MatchSimulator.cs` 12:03, `SimulationOptions.cs`
  12:05) predate this round's writes (12:38); they were not touched.
- `dotnet test` was not run. The `.tmp/tholdem-verify` supplementary xUnit runner
  executes the same compiled test assembly because the VSTest testhost aborts in this session; CI
  remains the authoritative gate.
