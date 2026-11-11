using System.Reflection;
using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Cards;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

public sealed class HandCategoryScoreTests
{
    [Fact]
    public void FromHeuristic_GivenAcesPreflop_ReturnsUnitIntervalScore()
    {
        var score = HandCategoryScore.FromHeuristic(TestCards.Parse("As Ah"), Array.Empty<Card>());

        Assert.InRange(score.Score, 0.0, 1.0);
        Assert.True(
            score.Score > 0.7,
            $"Expected a preflop score above 0.7 for aces; observed {score.Score}.");
    }

    [Fact]
    public void FromHeuristic_GivenFullHouse_ReturnsHighScore()
    {
        var score = HandCategoryScore.FromHeuristic(
            TestCards.Parse("As Ah"),
            TestCards.Parse("Ad Kc Kd 7s 2c"));

        Assert.InRange(score.Score, 0.0, 1.0);
        Assert.True(
            score.Score > 0.8,
            $"Expected a full-house score above 0.8; observed {score.Score}.");
    }

    [Fact]
    public void HandCategoryScore_GivenType_HasNoEquityConversion()
    {
        var conversions = typeof(HandCategoryScore)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .ToArray();

        Assert.Empty(conversions);
        Assert.Null(typeof(HandCategoryScore).GetMethod(
            "op_Implicit",
            BindingFlags.Public | BindingFlags.Static));
        Assert.Null(typeof(HandCategoryScore).GetMethod(
            "op_Explicit",
            BindingFlags.Public | BindingFlags.Static));
    }
}
