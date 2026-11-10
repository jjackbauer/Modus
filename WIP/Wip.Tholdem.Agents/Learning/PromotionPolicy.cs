namespace Wip.Tholdem.Agents.Learning;

/// <summary>One policy verdict over one evaluation report.</summary>
public sealed record PromotionDecision(
    bool Promoted,
    string Reason,
    double LowerBoundBbPer100,
    double MarginBbPer100,
    int Seeds);

/// <summary>Turns an evaluation report into a promotion verdict.</summary>
public interface IPromotionPolicy
{
    PromotionDecision Decide(EvaluationReport report, EvaluationCriteria criteria);
}

/// <summary>
/// The real gate: promote only when the challenger's lower confidence bound strictly exceeds the
/// configured margin and the evaluation covers at least the configured number of seeds.
/// </summary>
public sealed class StatisticalPromotionPolicy : IPromotionPolicy
{
    public const string PromotedReason = "promoted";

    public const string LowerBoundBelowMarginReason = "lcb-below-margin";

    public const string InsufficientSeedsReason = "insufficient-seeds";

    public PromotionDecision Decide(EvaluationReport report, EvaluationCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(criteria);

        var aboveMargin = report.LowerBoundBbPer100 > criteria.MarginBbPer100;
        var enoughSeeds = report.Seeds >= criteria.MinSeeds;
        var reason = !aboveMargin
            ? LowerBoundBelowMarginReason
            : enoughSeeds ? PromotedReason : InsufficientSeedsReason;

        return new PromotionDecision(
            reason == PromotedReason,
            reason,
            report.LowerBoundBbPer100,
            criteria.MarginBbPer100,
            report.Seeds);
    }
}

/// <summary>
/// The negative control: it promotes unconditionally. It exists so the pipeline can prove that a
/// promotion which the report does not justify is refused by <see cref="PromotionVerifier"/>.
/// </summary>
public sealed class AlwaysPromotePolicy : IPromotionPolicy
{
    public const string Reason = "always-promote-control";

    public PromotionDecision Decide(EvaluationReport report, EvaluationCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(criteria);

        return new PromotionDecision(
            true,
            Reason,
            report.LowerBoundBbPer100,
            criteria.MarginBbPer100,
            report.Seeds);
    }
}
