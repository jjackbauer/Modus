
## EQ-006 addendum — confidence-gated call decisions

~~~csharp
public enum EquityDecision { Call, Fold, Uncertain }

public sealed record EquityCallDecision(
    EquityDecision Decision, string Reason,
    double LowerBound, double UpperBound, double BreakEvenEquity);

public static class EquityThresholdPolicy
{
    public const string CallReason = "call-interval-above-threshold";
    public const string FoldReason = "fold-interval-below-threshold";
    public const string UncertainReason = "uncertain-interval-straddles-threshold";

    public static EquityCallDecision DecideCall(EquityEstimate equity, double breakEvenEquity);
}
~~~

Rules: validate equity non-null and break-even in [0,1]. Use the estimator's
ExpectedPotShareInterval: if Low > breakEven -> Call; if High < breakEven -> Fold; otherwise
(including either bound equal to the threshold) -> Uncertain with the named reason. No clock/culture.

Tests (WIP/Wip.Tholdem.Tests/Equity/EquityThresholdTests.cs, four):
1. interval (0.60, 0.70), threshold 0.50 -> Call / CallReason.
2. interval (0.20, 0.30), threshold 0.50 -> Fold / FoldReason.
3. interval (0.45, 0.55), threshold 0.50 -> Uncertain / UncertainReason.
4. interval (0.50, 0.60), threshold 0.50 -> Uncertain; threshold 1.5 -> ArgumentOutOfRangeException.
