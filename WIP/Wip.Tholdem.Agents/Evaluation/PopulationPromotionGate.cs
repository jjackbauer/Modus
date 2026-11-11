namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>The promotion verdict for one pre-registered four-seat evaluation.</summary>
public enum PromotionVerdict
{
    VerifiedImprovement = 0,
    Rejected = 1,
    Inconclusive = 2,
    Unsafe = 3,
    Unverified = 4,
}

/// <summary>The gate's decision: its verdict, stable reason code and the evidence it used.</summary>
public sealed record PopulationPromotionDecision(
    PromotionVerdict Verdict,
    string Reason,
    double LowerBoundBbPer100,
    double MarginBbPer100,
    int UnitCount,
    double AdjustedConfidenceLevel);

/// <summary>
/// The pre-registered, multiplicity-aware, block-level promotion gate. It first proves the two
/// reports belong to the two supplied plans (manifest, policies and unit counts), applies the
/// Bonferroni-adjusted confidence, then recomputes the block-level bounds from
/// <see cref="PopulationEvaluationReport.Units"/> and the anchor. The stored
/// <see cref="PopulationEvaluationReport.Estimate"/> is never read. There is no clock, GUID,
/// culture or ambient randomness.
/// </summary>
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
        EvaluationPlanV2 anchorPlan)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(anchorPlan);

        var adjustedConfidence =
            1.0 - ((1.0 - plan.ConfidenceLevel) / plan.PlannedCandidates);

        if (!string.Equals(primary.PlanHash, plan.PlanHash, StringComparison.Ordinal)
            || !string.Equals(primary.GameConfigHash, plan.Game.ConfigHash, StringComparison.Ordinal)
            || !string.Equals(primary.OpponentPoolId, plan.OpponentPoolId, StringComparison.Ordinal)
            || !string.Equals(
                primary.EvaluatorVersion,
                PopulationMatchEvaluator.EvaluatorVersion,
                StringComparison.Ordinal)
            || !string.Equals(anchor.PlanHash, anchorPlan.PlanHash, StringComparison.Ordinal)
            || !string.Equals(
                anchor.GameConfigHash,
                anchorPlan.Game.ConfigHash,
                StringComparison.Ordinal)
            || !string.Equals(
                anchor.OpponentPoolId,
                anchorPlan.OpponentPoolId,
                StringComparison.Ordinal)
            || !string.Equals(
                plan.Game.ConfigHash,
                anchorPlan.Game.ConfigHash,
                StringComparison.Ordinal)
            || !string.Equals(
                plan.Holdout.DomainHash,
                anchorPlan.Holdout.DomainHash,
                StringComparison.Ordinal))
        {
            return Unverified(ManifestMismatchReason, primary, plan, adjustedConfidence);
        }

        if (!string.Equals(primary.ChallengerId, anchor.ChallengerId, StringComparison.Ordinal)
            || !string.Equals(primary.IncumbentId, anchor.IncumbentId, StringComparison.Ordinal)
            || !string.Equals(
                primary.ChallengerPolicyHash,
                anchor.ChallengerPolicyHash,
                StringComparison.Ordinal)
            || !string.Equals(
                primary.IncumbentPolicyHash,
                anchor.IncumbentPolicyHash,
                StringComparison.Ordinal))
        {
            return Unverified(PolicyMismatchReason, primary, plan, adjustedConfidence);
        }

        if (primary.SeedBlocks != plan.SeedBlocks
            || primary.Units.Count != plan.SeedBlocks * plan.Compositions.Count
            || anchor.SeedBlocks != anchorPlan.SeedBlocks
            || anchor.Units.Count != anchorPlan.SeedBlocks * anchorPlan.Compositions.Count)
        {
            return Unverified(UnitCountMismatchReason, primary, plan, adjustedConfidence);
        }

        if (plan.SeedBlocks < 1)
        {
            return Inconclusive(
                InsufficientBlocksReason,
                0.0,
                plan,
                primary.Units.Count,
                adjustedConfidence);
        }

        if (!TryComputeBlockMeans(primary, plan, out var primaryBlockMeans)
            || !TryComputeBlockMeans(anchor, anchorPlan, out var anchorBlockMeans))
        {
            return Unverified(UnitCountMismatchReason, primary, plan, adjustedConfidence);
        }

        var bootstrapSeed = SeedDerivation.Derive(plan.Holdout.Seeds[0], 0xB007, 1, 0, 0);
        var estimate = BlockStatistics.Compute(
            primaryBlockMeans,
            adjustedConfidence,
            bootstrapSeed);
        var anchorEstimate = BlockStatistics.Compute(
            anchorBlockMeans,
            adjustedConfidence,
            bootstrapSeed);

        if (anchorEstimate.LowerBoundBbPer100 < -plan.AnchorRegressionToleranceBbPer100)
        {
            return new PopulationPromotionDecision(
                PromotionVerdict.Unsafe,
                AnchorRegressionReason,
                anchorEstimate.LowerBoundBbPer100,
                plan.MinimumPracticalEffectBbPer100,
                estimate.UnitCount,
                adjustedConfidence);
        }

        if (estimate.LowerBoundBbPer100 > plan.MinimumPracticalEffectBbPer100)
        {
            return new PopulationPromotionDecision(
                PromotionVerdict.VerifiedImprovement,
                VerifiedReason,
                estimate.LowerBoundBbPer100,
                plan.MinimumPracticalEffectBbPer100,
                estimate.UnitCount,
                adjustedConfidence);
        }

        if (estimate.LowerBoundBbPer100 <= 0.0)
        {
            return new PopulationPromotionDecision(
                PromotionVerdict.Rejected,
                NoImprovementReason,
                estimate.LowerBoundBbPer100,
                plan.MinimumPracticalEffectBbPer100,
                estimate.UnitCount,
                adjustedConfidence);
        }

        return Inconclusive(
            NearThresholdReason,
            estimate.LowerBoundBbPer100,
            plan,
            estimate.UnitCount,
            adjustedConfidence);
    }

    /// <summary>
    /// Recomputes one block mean per block index by averaging that block's composition means,
    /// requiring exactly the planned number of blocks and units per block.
    /// </summary>
    private static bool TryComputeBlockMeans(
        PopulationEvaluationReport report,
        EvaluationPlanV2 plan,
        out double[] blockMeans)
    {
        var sums = new Dictionary<int, double>();
        var counts = new Dictionary<int, int>();
        foreach (var unit in report.Units)
        {
            sums[unit.BlockIndex] = sums.GetValueOrDefault(unit.BlockIndex) + unit.MeanBbPer100;
            counts[unit.BlockIndex] = counts.GetValueOrDefault(unit.BlockIndex) + 1;
        }

        if (sums.Count != plan.SeedBlocks)
        {
            blockMeans = Array.Empty<double>();
            return false;
        }

        var orderedBlocks = sums.Keys.OrderBy(static block => block).ToArray();
        blockMeans = new double[orderedBlocks.Length];
        for (var index = 0; index < orderedBlocks.Length; index++)
        {
            var block = orderedBlocks[index];
            if (counts[block] != plan.Compositions.Count)
            {
                blockMeans = Array.Empty<double>();
                return false;
            }

            blockMeans[index] = sums[block] / counts[block];
        }

        return true;
    }

    private static PopulationPromotionDecision Unverified(
        string reason,
        PopulationEvaluationReport primary,
        EvaluationPlanV2 plan,
        double adjustedConfidence) =>
        new(
            PromotionVerdict.Unverified,
            reason,
            0.0,
            plan.MinimumPracticalEffectBbPer100,
            primary.Units.Count,
            adjustedConfidence);

    private static PopulationPromotionDecision Inconclusive(
        string reason,
        double lowerBoundBbPer100,
        EvaluationPlanV2 plan,
        int unitCount,
        double adjustedConfidence) =>
        new(
            PromotionVerdict.Inconclusive,
            reason,
            lowerBoundBbPer100,
            plan.MinimumPracticalEffectBbPer100,
            unitCount,
            adjustedConfidence);
}
