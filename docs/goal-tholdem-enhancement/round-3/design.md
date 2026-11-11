# Round 3 design contract — statistical gate, independent verifier, control experiment

**Depends on Round 2:** `PopulationEvaluationReport`, `PopulationMatchEvaluator`, `BlockStatistics`,
`FrozenOpponentPool`.
**Objective:** turn a paired four-seat report into a pre-registered, multiplicity-aware verdict that a
second party can recompute, and run the Milestone B control experiment end to end.

## 1. `Evaluation/PopulationPromotionGate.cs` (new)

```csharp
public enum PromotionVerdict { VerifiedImprovement, Rejected, Inconclusive, Unsafe, Unverified }

public sealed record PopulationPromotionDecision(
    PromotionVerdict Verdict, string Reason, double LowerBoundBbPer100,
    double MarginBbPer100, int UnitCount, double AdjustedConfidenceLevel);

public static class PopulationPromotionGate
{
    public const string VerifiedReason = "verified-improvement";
    public const string NoImprovementReason = "no-improvement";
    public const string NearThresholdReason = "inconclusive-near-threshold";
    public const string InsufficientBlocksReason = "inconclusive-insufficient-blocks";
    public const string AnchorRegressionReason = "unsafe-anchor-regression";
    public const string ManifestMismatchReason = "unverified-manifest-mismatch";
    public const string PolicyMismatchReason = "unverified-policy-mismatch";
    public const string UnitCountMismatchReason = "unverified-unit-count-mismatch";

    public static PopulationPromotionDecision Decide(
        PopulationEvaluationReport primary,
        PopulationEvaluationReport anchor,
        EvaluationPlanV2 plan,
        EvaluationPlanV2 anchorPlan);
}
```

Validation order (first failure wins):
1. `primary.PlanHash == plan.PlanHash`, `primary.GameConfigHash == plan.Game.ConfigHash`,
   `primary.OpponentPoolId == plan.OpponentPoolId`, `primary.EvaluatorVersion ==
   PopulationMatchEvaluator.EvaluatorVersion`; `anchor.PlanHash == anchorPlan.PlanHash`; the two
   plans have the same `Game.ConfigHash` and the same `Holdout.DomainHash`; `anchorPlan.OpponentPoolId
   == anchor.OpponentPoolId`. Otherwise `Unverified / ManifestMismatchReason`.
2. `primary` and `anchor` have the same challenger/incumbent ids AND policy hashes; otherwise
   `Unverified / PolicyMismatchReason`.
3. `primary.Units.Count == plan.SeedBlocks * plan.Compositions.Count` and the same for anchor;
   otherwise `Unverified / UnitCountMismatchReason`.
4. `plan.PlannedCandidates >= 1`; `adjustedAlpha = (1 - plan.ConfidenceLevel) / plan.PlannedCandidates`;
   `adjustedConfidence = 1 - adjustedAlpha`.
5. Recompute overall block means from `Units` (average the composition means per `BlockIndex`) and
   call `BlockStatistics.Compute(blockMeans, adjustedConfidence, bootstrapSeed)` where
   `bootstrapSeed = SeedDerivation.Derive(plan.Holdout.Seeds[0], 0xB007, 1, 0, 0)`.
6. Anchor recomputation the same way with `anchorPlan`; if its lower bound <
   `-plan.AnchorRegressionToleranceBbPer100` → `Unsafe / AnchorRegressionReason`.
7. If primary lower bound > `plan.MinimumPracticalEffectBbPer100` → `VerifiedImprovement`.
8. Else if primary lower bound <= 0 → `Rejected / NoImprovementReason`.
9. Else `Inconclusive / NearThresholdReason`.
10. If `plan.SeedBlocks` is below 1 or `primary.SeedBlocks != plan.SeedBlocks` →
    `Inconclusive / InsufficientBlocksReason` (checked before step 5).

The gate never trusts the report's stored `Estimate`; it recomputes from `Units`.

## 2. `Evaluation/PopulationEvaluationReportJson.cs` and `Evaluation/EvaluationPlanV2Json.cs` (new)

- `Write(report)` → `report.ToCanonicalJson()`.
- `Parse(string json)` → rebuild the report with `System.Text.Json`, then require
  `candidate.ToCanonicalJson() == json` and `candidate.ReportHash` recomputes; otherwise throw
  `FormatException` with a stable code (`report-not-json`, `report-shape-invalid`,
  `report-not-canonical`). Every list must match its declared count.
- `EvaluationPlanV2Json`: `Write(plan)` → canonical JSON; `Parse(json)` reads the game object and
  each domain (id, purpose, baseSeed, count, seeds), calls `EvaluationPlanV2.Create(...)`, then
  requires the recomputed `PlanHash` to equal the hash of the persisted JSON and each stored seed to
  equal the re-derived seed. Otherwise throw `FormatException`.
- The report parser must not accept a mutated number, a reordered key, a missing unit, or an extra
  key.

## 3. `Evaluation/PopulationPromotionVerifier.cs` (new)

```csharp
public static class PopulationPromotionVerifier
{
    public const string VerifiedReason = "verifier:verified";
    public const string ReportMismatchReason = "verifier:report-mismatch";
    public const string DecisionMismatchReason = "verifier:decision-mismatch";
    public const string PolicyMismatchReason = "verifier:policy-mismatch";

    public static bool Verify(
        PopulationPromotionDecision decision,
        PopulationEvaluationReport primary,
        PopulationEvaluationReport anchor,
        EvaluationPlanV2 plan,
        EvaluationPlanV2 anchorPlan,
        Func<PopulationEvaluationReport> recomputePrimary,
        Func<PopulationEvaluationReport> recomputeAnchor,
        out string reason);
}
```

- Recompute both reports with the supplied delegates; their `ReportHash` must equal the claimed
  reports' hashes, otherwise `false / ReportMismatchReason`.
- Recompute `PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan)`; the verdict and
  reason must equal the supplied decision, otherwise `false / DecisionMismatchReason`.
- Recompute the plan hashes and compare with the report fields; mismatch →
  `false / PolicyMismatchReason`.
- Never accepts a decision that the reports do not justify.

## 4. Control experiment (Milestone B exit)

**Pre-registration artifact** `docs/goal-tholdem-enhancement/experiments/control-v1/preregistration.json`
written before the run, containing:
- plan: game = four-seat cash 1000/5/10; pool = `tholdem.opponents.holdout.v1`; compositions =
  [`primary-mixed`, `three-tag`, `three-station`, `pressure`]; holdout 192 blocks; selection 64;
  training 64; audit 32 (disjoint domains); hands per seat match 20; margin 5 bb/100; confidence
  0.95; planned candidates 1; anchor tolerance 5 bb/100.
- anchor plan: same game, same holdout domain hash, pool `tholdem.opponents.holdout.v1` (or the
  selection pool), compositions [`primary-mixed`].
- policies: challenger = nit genome (0.90, 0.85, 0.00, 1.0); incumbent = scripted fold-to-any-bet.
- pilot evidence: 96 selection blocks, mean 45.92, sd 159.56, margin 5 → planned holdout power ≥ 0.99
  if the pilot mean repeats (SE ≈ 11.5).
- decision rule: exactly the gate above; verdict recorded even if it is inconclusive or rejected.

**Run:** a test or scratch harness calls `PopulationMatchEvaluator.Evaluate` for primary and anchor,
persists the two report JSONs and the plan JSON under `experiments/control-v1/results/`, calls the
gate, then calls the verifier with fresh agent instances. The orchestrator copies the observed
report hashes and verdict into `evidence.md` only after re-running it.

**Required outcomes:** policy vs itself = 0.0; nit vs fold = VerifiedImprovement (or an honest
Inconclusive/Rejected recorded); a forced always-promote decision = verifier refusal; a tampered
report = verifier refusal.

## 5. Tests

`WIP/Wip.Tholdem.Tests/Evaluation/PopulationPromotionGateTests.cs`
1. `Decide_GivenSelfComparison_RejectsAsNoImprovement`.
2. `Decide_GivenStrongPositiveLowerBound_Verifies` (synthetic units).
3. `Decide_GivenLowerBoundBetweenZeroAndMargin_IsInconclusive`.
4. `Decide_GivenAnchorRegression_IsUnsafe`.
5. `Decide_GivenPlanHashMismatch_IsUnverified`.
6. `Decide_GivenFewerUnitsThanPlanned_IsInconclusive`.
7. `Decide_GivenTwoCandidates_AppliesBonferroni` (same units; adjusted confidence lowers the bound).

`WIP/Wip.Tholdem.Tests/Evaluation/PopulationVerifierTests.cs`
1. `Verify_GivenRecomputedReportsAgree_ReturnsVerified`.
2. `Verify_GivenAlwaysPromoteControl_ReturnsDecisionMismatch`.
3. `Verify_GivenTamperedReportHash_ReturnsReportMismatch`.
4. `Verify_GivenRecomputationDiffers_ReturnsReportMismatch`.

`WIP/Wip.Tholdem.Tests/Evaluation/PopulationReportJsonTests.cs`
1. `Parse_GivenCanonicalReport_RoundTrips`.
2. `Parse_GivenMutatedNumber_RefusesAsNotCanonical`.
3. `Parse_GivenMissingUnit_RefusesAsShapeInvalid`.
4. `Parse_GivenExtraKey_RefusesAsNotCanonical`.

## 6. Forbidden

Do not change v1 `MatchEvaluator`, `PromotionPolicy`, `PromotionVerifier`, `TrainingLoop`, engine,
history, host or web. No clock/GUID/culture/ambient randomness. No test weakening.
