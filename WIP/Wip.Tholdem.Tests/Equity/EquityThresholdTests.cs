using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

public sealed class EquityThresholdTests
{
    [Fact]
    public void DecideCall_GivenIntervalAboveThreshold_ReturnsCall()
    {
        var decision = EquityThresholdPolicy.DecideCall(Estimate(0.60, 0.70), 0.50);

        Assert.Equal(EquityDecision.Call, decision.Decision);
        Assert.Equal(EquityThresholdPolicy.CallReason, decision.Reason);
        Assert.Equal(0.60, decision.LowerBound);
        Assert.Equal(0.70, decision.UpperBound);
        Assert.Equal(0.50, decision.BreakEvenEquity);
    }

    [Fact]
    public void DecideCall_GivenIntervalBelowThreshold_ReturnsFold()
    {
        var decision = EquityThresholdPolicy.DecideCall(Estimate(0.20, 0.30), 0.50);

        Assert.Equal(EquityDecision.Fold, decision.Decision);
        Assert.Equal(EquityThresholdPolicy.FoldReason, decision.Reason);
        Assert.Equal(0.20, decision.LowerBound);
        Assert.Equal(0.30, decision.UpperBound);
        Assert.Equal(0.50, decision.BreakEvenEquity);
    }

    [Fact]
    public void DecideCall_GivenIntervalStraddles_ReturnsUncertain()
    {
        var decision = EquityThresholdPolicy.DecideCall(Estimate(0.45, 0.55), 0.50);

        Assert.Equal(EquityDecision.Uncertain, decision.Decision);
        Assert.Equal(EquityThresholdPolicy.UncertainReason, decision.Reason);
        Assert.Equal(0.45, decision.LowerBound);
        Assert.Equal(0.55, decision.UpperBound);
        Assert.Equal(0.50, decision.BreakEvenEquity);
    }

    [Fact]
    public void DecideCall_GivenBoundaryEquality_IsUncertainAndInvalidThresholdThrows()
    {
        var decision = EquityThresholdPolicy.DecideCall(Estimate(0.50, 0.60), 0.50);

        Assert.Equal(EquityDecision.Uncertain, decision.Decision);
        Assert.Equal(EquityThresholdPolicy.UncertainReason, decision.Reason);
        Assert.Equal(0.50, decision.LowerBound);
        Assert.Equal(0.60, decision.UpperBound);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EquityThresholdPolicy.DecideCall(Estimate(0.50, 0.60), 1.5));
    }

    private static EquityEstimate Estimate(double low, double high) =>
        new(
            (low + high) / 2.0,
            0.0,
            (low + high) / 2.0,
            1000,
            3,
            "test",
            0,
            0,
            0,
            (high - low) / 3.92,
            new ConfidenceInterval(low, high));
}
