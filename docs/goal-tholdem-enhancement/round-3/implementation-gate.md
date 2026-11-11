# Implementation gate — population promotion gate

Round 3, section 1: the pre-registered, multiplicity-aware, block-level promotion gate.

## Files

- `WIP/Wip.Tholdem.Agents/Evaluation/PopulationPromotionGate.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/PopulationPromotionGateTests.cs` (new)
- `docs/goal-tholdem-enhancement/round-3/implementation-gate.md` (new, this file)

No other file was edited.

## What the gate does

`PopulationPromotionGate.Decide` applies the section 1 order and returns a
`PopulationPromotionDecision`:

1. Manifest: primary/anchor `PlanHash`, `GameConfigHash`, `OpponentPoolId`, evaluator
   version, both plans' game config hashes, and both holdout domain hashes. Failure is
   `Unverified / unverified-manifest-mismatch`.
2. Policy: both reports carry the same challenger/incumbent ids and policy hashes. Failure is
   `Unverified / unverified-policy-mismatch`.
3. Unit counts: `SeedBlocks == plan.SeedBlocks` and
   `Units.Count == plan.SeedBlocks * plan.Compositions.Count`, for both reports. Failure is
   `Unverified / unverified-unit-count-mismatch`.
4. Multiplicity: `adjustedConfidence = 1 - ((1 - plan.ConfidenceLevel) / plan.PlannedCandidates)`.
5. Block means are recomputed from `Units`: group by `BlockIndex`, average the composition
   means, require exactly `plan.SeedBlocks` distinct blocks and
   `plan.Compositions.Count` units in every block. A structural failure is
   `Unverified / unverified-unit-count-mismatch`.
6. `bootstrapSeed = SeedDerivation.Derive(plan.Holdout.Seeds[0], 0xB007, 1, 0, 0)`; both
   estimates are `BlockStatistics.Compute(blockMeans, adjustedConfidence, bootstrapSeed)`.
7. Anchor lower bound below `-plan.AnchorRegressionToleranceBbPer100` is
   `Unsafe / unsafe-anchor-regression`.
8. Primary lower bound above `plan.MinimumPracticalEffectBbPer100` is
   `VerifiedImprovement / verified-improvement`.
9. Primary lower bound at or below zero is `Rejected / no-improvement`.
10. Otherwise `Inconclusive / inconclusive-near-threshold`. `plan.SeedBlocks < 1` is
    `Inconclusive / inconclusive-insufficient-blocks`.

The gate never reads `report.Estimate`. The tests poison every synthetic report's stored
estimate with `12345.0`; the self-comparison case still rejects, so a stored-estimate shortcut
would fail the suite.

## Test-first red (stub gate)

`dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` succeeded with the
stub (0 errors). Then:

```
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.PopulationPromotionGateTests
```

Observed:

```
FAIL PopulationPromotionGateTests.Decide_GivenAnchorRegression_IsUnsafe: EqualException: Assert.Equal() Failure: Values differ
Expected: Unsafe
Actual:   Inconclusive
FAIL PopulationPromotionGateTests.Decide_GivenLowerBoundBetweenZeroAndMargin_IsInconclusive: EqualException: Assert.Equal() Failure: Strings differ
                        ↓ (pos 13)
Expected: "inconclusive-near-threshold"
Actual:   "inconclusive-insufficient-blocks"
                        ↑ (pos 13)
FAIL PopulationPromotionGateTests.Decide_GivenPlanHashMismatch_IsUnverified: EqualException: Assert.Equal() Failure: Values differ
Expected: Unverified
Actual:   Inconclusive
FAIL PopulationPromotionGateTests.Decide_GivenSelfComparison_RejectsAsNoImprovement: EqualException: Assert.Equal() Failure: Values differ
Expected: Rejected
Actual:   Inconclusive
FAIL PopulationPromotionGateTests.Decide_GivenStrongPositiveLowerBound_Verifies: EqualException: Assert.Equal() Failure: Values differ
Expected: VerifiedImprovement
Actual:   Inconclusive
FAIL PopulationPromotionGateTests.Decide_GivenTwoCandidates_AppliesBonferroni: EqualException: Assert.Equal() Failure: Values differ
Expected: 0.97499999999999998
Actual:   1
FAIL PopulationPromotionGateTests.Decide_GivenUnitCountMismatch_IsUnverified: EqualException: Assert.Equal() Failure: Values differ
Expected: Unverified
Actual:   Inconclusive
TOTAL 7 PASSED 0 FAILED 7 SKIPPED 0
```

## Green (after implementation)

Build: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` — succeeded,
0 errors.

Gate tests:

```
TOTAL 7 PASSED 7 FAILED 0 SKIPPED 0
```

Full suite:

```
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 491 PASSED 491 FAILED 0 SKIPPED 0
```

491 = 484 pre-existing tests + 7 new gate tests; no pre-existing test regressed.

## Stub removal

Observed searches over `PopulationPromotionGate.cs`:

- `NotImplementedException|Stub|stub`: no matches.
- `\.Estimate`: only the XML doc sentence stating the stored estimate is never read; no field
  access exists in the implementation.

The temp `.tmp/tholdem-verify` runner was used because VSTest's testhost is unavailable in this
session; `dotnet test` was not run.
