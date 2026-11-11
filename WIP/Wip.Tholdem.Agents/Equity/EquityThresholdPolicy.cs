namespace Wip.Tholdem.Agents.Equity;

/// <summary>Confidence-gated call, fold or uncertain decision for a fixed threshold.</summary>
public enum EquityDecision
{
    Call,
    Fold,
    Uncertain,
}

/// <summary>An equity decision with its closed pot-share interval and the break-even threshold.</summary>
public sealed record EquityCallDecision(
    EquityDecision Decision,
    string Reason,
    double LowerBound,
    double UpperBound,
    double BreakEvenEquity);

/// <summary>
/// Maps an estimated expected-pot-share interval to a call only when the whole interval is above
/// the break-even equity, to a fold only when the whole interval is below it, and otherwise to an
/// explicit uncertain outcome.
/// </summary>
public static class EquityThresholdPolicy
{
    /// <summary>Reason recorded when the whole interval sits strictly above the threshold.</summary>
    public const string CallReason = "call-interval-above-threshold";

    /// <summary>Reason recorded when the whole interval sits strictly below the threshold.</summary>
    public const string FoldReason = "fold-interval-below-threshold";

    /// <summary>Reason recorded when the interval covers or touches the threshold.</summary>
    public const string UncertainReason = "uncertain-interval-straddles-threshold";

    /// <summary>
    /// Returns <see cref="EquityDecision.Call"/> when the estimate's lower pot-share bound is
    /// strictly above <paramref name="breakEvenEquity"/>, <see cref="EquityDecision.Fold"/> when
    /// its upper bound is strictly below the threshold, and otherwise
    /// <see cref="EquityDecision.Uncertain"/>. Either bound equalling the threshold is uncertain.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="equity"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="breakEvenEquity"/> is not in the closed interval [0, 1].
    /// </exception>
    public static EquityCallDecision DecideCall(EquityEstimate equity, double breakEvenEquity)
    {
        ArgumentNullException.ThrowIfNull(equity);

        if (!(breakEvenEquity >= 0.0 && breakEvenEquity <= 1.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(breakEvenEquity),
                breakEvenEquity,
                "The break-even equity must be between 0 and 1.");
        }

        var interval = equity.ExpectedPotShareInterval;

        if (interval.Low > breakEvenEquity)
        {
            return new EquityCallDecision(
                EquityDecision.Call,
                CallReason,
                interval.Low,
                interval.High,
                breakEvenEquity);
        }

        if (interval.High < breakEvenEquity)
        {
            return new EquityCallDecision(
                EquityDecision.Fold,
                FoldReason,
                interval.Low,
                interval.High,
                breakEvenEquity);
        }

        return new EquityCallDecision(
            EquityDecision.Uncertain,
            UncertainReason,
            interval.Low,
            interval.High,
            breakEvenEquity);
    }
}
