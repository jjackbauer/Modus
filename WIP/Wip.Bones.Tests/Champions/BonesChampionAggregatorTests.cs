using Wip.Bones.Agents.Champions;
using Xunit;

namespace Wip.Bones.Tests.Champions;

public sealed class BonesChampionAggregatorTests
{
    [Fact]
    public void BonesChampionAggregator_GivenTwoSeedSamples_ExpectedCombinedGamesWinsAndSeatRates()
    {
        var first = CreateSample("candidate-a", games: 2000, wins: 500, meanScoreDifferential: -13.0, targetSeatRate: 0.25);
        var second = CreateSample("candidate-a", games: 2000, wins: 550, meanScoreDifferential: -12.0, targetSeatRate: 0.27);

        var result = Assert.Single(BonesChampionAggregator.Aggregate([first, second], targetSeat: 1));

        Assert.Equal("candidate-a", result.StrategyId);
        Assert.Equal(1, result.Seat);
        Assert.Equal(4000, result.Games);
        Assert.Equal(1050, result.Wins);
        Assert.Equal(-12.5, result.MeanScoreDifferential, precision: 9);
        Assert.Equal(0.26, result.WinRateBySeat[1], precision: 9);
        Assert.Equal(0.20, result.WinRateBySeat[2], precision: 9);
    }

    [Fact]
    public void BonesChampionAggregator_GivenTwoStrategies_ExpectedTwoResultsSortedById()
    {
        var b = CreateSample("b", games: 2000, wins: 400, meanScoreDifferential: -14.0, targetSeatRate: 0.20);
        var a = CreateSample("a", games: 2000, wins: 600, meanScoreDifferential: -12.0, targetSeatRate: 0.30);

        var results = BonesChampionAggregator.Aggregate([b, a], targetSeat: 1);

        Assert.Equal(new[] { "a", "b" }, results.Select(result => result.StrategyId));
    }

    private static BonesChampionCandidateResult CreateSample(
        string strategyId,
        int games,
        int wins,
        double meanScoreDifferential,
        double targetSeatRate)
    {
        var seatRates = new Dictionary<int, double>
        {
            [1] = targetSeatRate,
            [2] = 0.20,
            [3] = 0.20,
            [4] = 0.20,
        };

        return new BonesChampionCandidateResult(
            strategyId,
            1,
            games,
            wins,
            meanScoreDifferential,
            seatRates);
    }
}