namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// Re-derives the promotion criterion from the evaluation report alone. The verifier never trusts a
/// policy: a promotion the report does not justify is refused, and so is a rejection the report
/// contradicts. This is what makes an always-promote control impossible to pass through the loop.
/// </summary>
public static class PromotionVerifier
{
    public const string VerifiedReason = "verified";

    public const string LowerBoundFailureReason = "verifier:lcb-below-margin";

    public const string InsufficientSeedsFailureReason = "verifier:insufficient-seeds";

    public const string UnjustifiedRejectionReason = "verifier:unjustified-rejection";

    public const string JustifiedRejectionReason = "rejection-justified";

    public static bool Verify(
        PromotionDecision decision,
        EvaluationReport report,
        EvaluationCriteria criteria,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(criteria);

        var clearsMargin = report.LowerBoundBbPer100 > criteria.MarginBbPer100;
        var enoughSeeds = report.Seeds >= criteria.MinSeeds;

        if (decision.Promoted)
        {
            if (!clearsMargin)
            {
                reason = LowerBoundFailureReason;
                return false;
            }

            if (!enoughSeeds)
            {
                reason = InsufficientSeedsFailureReason;
                return false;
            }

            reason = VerifiedReason;
            return true;
        }

        if (clearsMargin && enoughSeeds)
        {
            reason = UnjustifiedRejectionReason;
            return false;
        }

        reason = JustifiedRejectionReason;
        return true;
    }
}
