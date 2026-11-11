namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>
/// The independent promotion verifier: a second party recomputes both paired reports with fresh
/// evaluation instances and refuses any decision the recomputed reports do not justify.
/// </summary>
public static class PopulationPromotionVerifier
{
    public const string VerifiedReason = "verifier:verified";

    public const string ReportMismatchReason = "verifier:report-mismatch";

    public const string DecisionMismatchReason = "verifier:decision-mismatch";

    public const string PolicyMismatchReason = "verifier:policy-mismatch";

    public const string RecomputationFailedReason = "verifier:recomputation-failed";

    public static bool Verify(
        PopulationPromotionDecision decision,
        PopulationEvaluationReport primary,
        PopulationEvaluationReport anchor,
        EvaluationPlanV2 plan,
        EvaluationPlanV2 anchorPlan,
        Func<PopulationEvaluationReport> recomputePrimary,
        Func<PopulationEvaluationReport> recomputeAnchor,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(anchorPlan);
        ArgumentNullException.ThrowIfNull(recomputePrimary);
        ArgumentNullException.ThrowIfNull(recomputeAnchor);

        // The plan hash is recomputed from the canonical plan on every access, so comparing it with
        // the report field recomputes the plan hash instead of trusting a persisted value.
        if (!string.Equals(primary.PlanHash, plan.PlanHash, StringComparison.Ordinal)
            || !string.Equals(anchor.PlanHash, anchorPlan.PlanHash, StringComparison.Ordinal))
        {
            reason = PolicyMismatchReason;
            return false;
        }

        // Both delegates are always invoked: a failure in one must not mask the other.
        var primaryRecomputed = TryRecompute(recomputePrimary, out var recomputedPrimary);
        var anchorRecomputed = TryRecompute(recomputeAnchor, out var recomputedAnchor);
        if (!primaryRecomputed || !anchorRecomputed)
        {
            reason = RecomputationFailedReason;
            return false;
        }

        if (recomputedPrimary is null
            || recomputedAnchor is null
            || !string.Equals(
                recomputedPrimary.ReportHash,
                primary.ReportHash,
                StringComparison.Ordinal)
            || !string.Equals(
                recomputedAnchor.ReportHash,
                anchor.ReportHash,
                StringComparison.Ordinal))
        {
            reason = ReportMismatchReason;
            return false;
        }

        var expected = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);
        if (expected.Verdict != decision.Verdict
            || !string.Equals(expected.Reason, decision.Reason, StringComparison.Ordinal))
        {
            reason = DecisionMismatchReason;
            return false;
        }

        reason = VerifiedReason;
        return true;
    }

    private static bool TryRecompute(
        Func<PopulationEvaluationReport> recompute,
        out PopulationEvaluationReport? recomputed)
    {
        try
        {
            recomputed = recompute();
            return true;
        }
        catch (Exception)
        {
            recomputed = null;
            return false;
        }
    }
}
