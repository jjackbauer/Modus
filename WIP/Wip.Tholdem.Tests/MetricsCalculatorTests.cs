using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class MetricsCalculatorTests
{
    [Fact]
    public void Compute_GivenFourSyntheticHands_ExpectedExactMetricsAndBigBlindInterval()
    {
        var outcomes = new[]
        {
            Outcome(1, 1010, 990, winners: new[] { 0 }, showdown: false, aggressive: new[] { 1, 0 }, calls: new[] { 0, 1 }, participants: new[] { false, false }),
            Outcome(2, 990, 1010, winners: new[] { 1 }, showdown: false, aggressive: new[] { 0, 1 }, calls: new[] { 1, 0 }, participants: new[] { false, false }),
            Outcome(3, 1020, 980, winners: new[] { 0 }, showdown: true, aggressive: new[] { 2, 1 }, calls: new[] { 1, 1 }, participants: new[] { true, true }),
            Outcome(4, 1030, 970, winners: new[] { 0 }, showdown: true, aggressive: new[] { 1, 0 }, calls: new[] { 2, 0 }, participants: new[] { true, true }),
        };

        var metrics = MetricsCalculator.Compute("literal-agent", 0, outcomes, 1000L, 10L);

        Assert.Equal("literal-agent", metrics.AgentName);
        Assert.Equal(4, metrics.Hands);
        Assert.Equal(3, metrics.Wins);
        Assert.Equal(0.75, metrics.WinRate);
        Assert.Equal(50L, metrics.NetChips);
        Assert.Equal(125.0, metrics.BigBlindsPer100);
        Assert.Equal(1.0, metrics.AggressionFactor);
        Assert.Equal(0.5, metrics.ShowdownRate);
        Assert.Equal(-42.363787105299444, metrics.BigBlindsPer100Ci.Low, 9);
        Assert.Equal(292.36378710529942, metrics.BigBlindsPer100Ci.High, 9);
    }

    [Fact]
    public void Compute_GivenFiftyWinsOfOneHundred_ExpectedWilsonInterval()
    {
        var outcomes = new HandOutcome[100];
        for (var index = 0; index < outcomes.Length; index++)
        {
            var won = index < 50;
            outcomes[index] = won
                ? Outcome(index + 1, 1010, 990, new[] { 0 }, false, new[] { 0, 0 }, new[] { 0, 0 }, new[] { false, false })
                : Outcome(index + 1, 990, 1010, new[] { 1 }, false, new[] { 0, 0 }, new[] { 0, 0 }, new[] { false, false });
        }

        var metrics = MetricsCalculator.Compute("wilson-agent", 0, outcomes, 1000L, 10L);

        Assert.Equal(100, metrics.Hands);
        Assert.Equal(50, metrics.Wins);
        Assert.Equal(0.5, metrics.WinRate);
        Assert.InRange(metrics.WinRateCi.Low, 0.4038 - 0.0005, 0.4038 + 0.0005);
        Assert.InRange(metrics.WinRateCi.High, 0.5962 - 0.0005, 0.5962 + 0.0005);
        Assert.Equal(0.40383153036599562, metrics.WinRateCi.Low, 12);
        Assert.Equal(0.59616846963400438, metrics.WinRateCi.High, 12);
        Assert.Equal(0.0, metrics.AggressionFactor);
        Assert.Equal(0L, metrics.NetChips);
        Assert.Equal(0.0, metrics.BigBlindsPer100);
    }

    [Fact]
    public void Compute_GivenSingleHand_ExpectedDegenerateBigBlindIntervalAndFullWinRate()
    {
        var outcomes = new[]
        {
            Outcome(1, 1020, 980, new[] { 0 }, true, new[] { 3, 1 }, new[] { 2, 0 }, new[] { true, true }),
        };

        var metrics = MetricsCalculator.Compute("single-hand", 0, outcomes, 1000L, 10L);

        Assert.Equal(1, metrics.Hands);
        Assert.Equal(1, metrics.Wins);
        Assert.Equal(1.0, metrics.WinRate);
        Assert.Equal(20L, metrics.NetChips);
        Assert.Equal(200.0, metrics.BigBlindsPer100);
        Assert.Equal(200.0, metrics.BigBlindsPer100Ci.Low);
        Assert.Equal(200.0, metrics.BigBlindsPer100Ci.High);
        Assert.Equal(1.5, metrics.AggressionFactor);
        Assert.Equal(1.0, metrics.ShowdownRate);
        Assert.InRange(metrics.WinRateCi.Low, 0.2065 - 0.0005, 0.2065 + 0.0005);
        Assert.Equal(1.0, metrics.WinRateCi.High);
    }

    private static HandOutcome Outcome(
        int handNumber,
        long seat0Stack,
        long seat1Stack,
        int[] winners,
        bool showdown,
        int[] aggressive,
        int[] calls,
        bool[] participants) =>
        new(
            handNumber,
            new[] { seat0Stack, seat1Stack },
            winners,
            showdown,
            aggressive,
            calls,
            participants);
}