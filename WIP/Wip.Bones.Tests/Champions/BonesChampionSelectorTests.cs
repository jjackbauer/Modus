using Wip.Bones.Agents.Champions;
using Xunit;

namespace Wip.Bones.Tests.Champions;

public sealed class BonesChampionSelectorTests
{
    private const int SeatCount = 4;

    [Fact]
    public void BonesChampionSelector_GivenOneWinInOneGameAndTwelveWinsInTwenty_ExpectedTwentyGameCandidateRanksHigher()
    {
        var oneGame = CreateCandidate("candidate-one-game", games: 1, wins: 1, targetSeat: 1);
        var twentyGames = CreateCandidate("candidate-twenty-games", games: 20, wins: 12, targetSeat: 1);

        var selection = BonesChampionSelector.Select(
            [oneGame, twentyGames],
            confirmationResults: null,
            incumbentId: null,
            targetSeat: 1,
            minGames: 10);

        Assert.Equal(BonesChampionSelectionOutcome.Promoted, selection.Outcome);
        Assert.Equal("candidate-twenty-games", selection.StrategyId);
        Assert.Null(selection.IncumbentId);
        Assert.True(BonesChampionSelector.WilsonLowerBound(12, 20) > BonesChampionSelector.WilsonLowerBound(1, 1));
        Assert.True(selection.SelectionLcb > 0.30);
        Assert.True(selection.SelectionLcb < 0.40);
    }

    [Fact]
    public void BonesChampionSelector_GivenChallengerWinsSelectionButLosesConfirmation_ExpectedRetainsIncumbent()
    {
        const string incumbentId = "incumbent-strategy";
        const string challengerId = "challenger-strategy";

        var incumbent = CreateCandidate(incumbentId, games: 2000, wins: 900, targetSeat: 2);
        var challenger = CreateCandidate(challengerId, games: 2000, wins: 1100, targetSeat: 2);
        var incumbentConfirmation = CreateCandidate(incumbentId, games: 2000, wins: 1000, targetSeat: 2);
        var challengerConfirmation = CreateCandidate(challengerId, games: 2000, wins: 800, targetSeat: 2);

        var selection = BonesChampionSelector.Select(
            [challenger, incumbent],
            [challengerConfirmation, incumbentConfirmation],
            incumbentId,
            targetSeat: 2,
            minGames: 1000);

        Assert.Equal(BonesChampionSelectionOutcome.Retained, selection.Outcome);
        Assert.Equal(incumbentId, selection.StrategyId);
        Assert.Equal(incumbentId, selection.IncumbentId);
        Assert.Equal("ConfirmationFailed", selection.Reason);
        Assert.True(selection.SelectionLcb > selection.IncumbentLcb);
        Assert.True(selection.ConfirmationLcb < selection.IncumbentConfirmationLcb);
    }

    [Fact]
    public void BonesChampionSelector_GivenChallengerWinsSelectionAndConfirmation_ExpectedPromoted()
    {
        const string incumbentId = "incumbent-strategy";
        const string challengerId = "challenger-strategy";

        var incumbent = CreateCandidate(incumbentId, games: 2000, wins: 900, targetSeat: 3);
        var challenger = CreateCandidate(challengerId, games: 2000, wins: 1200, targetSeat: 3);
        var incumbentConfirmation = CreateCandidate(incumbentId, games: 2000, wins: 900, targetSeat: 3);
        var challengerConfirmation = CreateCandidate(challengerId, games: 2000, wins: 1100, targetSeat: 3);

        var selection = BonesChampionSelector.Select(
            [challenger, incumbent],
            [incumbentConfirmation, challengerConfirmation],
            incumbentId,
            targetSeat: 3,
            minGames: 1000);

        Assert.Equal(BonesChampionSelectionOutcome.Promoted, selection.Outcome);
        Assert.Equal(challengerId, selection.StrategyId);
        Assert.Equal(incumbentId, selection.IncumbentId);
        Assert.Equal("Promoted", selection.Reason);
        Assert.True(selection.SelectionLcb > selection.IncumbentLcb);
        Assert.True(selection.ConfirmationLcb > selection.IncumbentConfirmationLcb);
    }

    [Fact]
    public void BonesChampionSelector_GivenObservedSeat1GapFixture_ExpectedV2PromotedOverV3()
    {
        const string incumbentId = "initial-seat-1-v3@8bf48727";
        const string challengerId = "initial-seat-1-v2@004e6719";

        var incumbent = CreateCandidate(incumbentId, games: 4000, wins: 911, targetSeat: 1);
        var challenger = CreateCandidate(challengerId, games: 4000, wins: 1037, targetSeat: 1);

        var selection = BonesChampionSelector.Select(
            [incumbent, challenger],
            confirmationResults: null,
            incumbentId,
            targetSeat: 1,
            minGames: 4000);

        Assert.Equal(BonesChampionSelectionOutcome.Promoted, selection.Outcome);
        Assert.Equal(challengerId, selection.StrategyId);
        Assert.Equal(incumbentId, selection.IncumbentId);
        Assert.Equal("Promoted", selection.Reason);
        Assert.True(selection.SelectionLcb > selection.IncumbentLcb);
    }

    [Fact]
    public void BonesChampionSelector_GivenCandidateBelowMinGames_ExpectedSkipped()
    {
        var thin = CreateCandidate("candidate-thin", games: 1, wins: 1, targetSeat: 1);

        var selection = BonesChampionSelector.Select(
            [thin],
            confirmationResults: null,
            incumbentId: null,
            targetSeat: 1,
            minGames: 10);

        Assert.Equal(BonesChampionSelectionOutcome.Skipped, selection.Outcome);
        Assert.Null(selection.StrategyId);
        Assert.Equal("NoEligibleCandidates", selection.Reason);
        Assert.True(BonesChampionSelector.WilsonLowerBound(0, 0) == 0.0);
        Assert.True(BonesChampionSelector.WilsonLowerBound(5, 1) == BonesChampionSelector.WilsonLowerBound(1, 1));
    }

    [Fact]
    public void BonesChampionSelector_GivenTiedOverallLcb_ExpectedTargetSeatTieBreak()
    {
        const string lowSeatRateId = "candidate-low-seat-rate";
        const string highSeatRateId = "candidate-high-seat-rate";

        var lowSeatRate = CreateCandidate(
            lowSeatRateId,
            games: 2000,
            wins: 1000,
            targetSeat: 2,
            targetSeatWinRate: 0.20);
        var highSeatRate = CreateCandidate(
            highSeatRateId,
            games: 2000,
            wins: 1000,
            targetSeat: 2,
            targetSeatWinRate: 0.30);

        var selection = BonesChampionSelector.Select(
            [lowSeatRate, highSeatRate],
            confirmationResults: null,
            incumbentId: lowSeatRateId,
            targetSeat: 2,
            minGames: 1000);

        Assert.Equal(BonesChampionSelectionOutcome.Promoted, selection.Outcome);
        Assert.Equal(highSeatRateId, selection.StrategyId);
        Assert.True(selection.SelectionLcb == selection.IncumbentLcb);
    }

    [Fact]
    public void BonesChampionSelector_GivenNoIncumbent_ExpectedHighestLcbSelected()
    {
        var weaker = CreateCandidate("candidate-weaker", games: 2000, wins: 900, targetSeat: 4);
        var stronger = CreateCandidate("candidate-stronger", games: 2000, wins: 1100, targetSeat: 4);

        var selection = BonesChampionSelector.Select(
            [weaker, stronger],
            confirmationResults: null,
            incumbentId: null,
            targetSeat: 4,
            minGames: 1000);

        Assert.Equal(BonesChampionSelectionOutcome.Promoted, selection.Outcome);
        Assert.Equal("candidate-stronger", selection.StrategyId);
        Assert.Null(selection.IncumbentId);
        Assert.True(selection.SelectionLcb > BonesChampionSelector.WilsonLowerBound(900, 2000));
    }

    [Fact]
    public void BonesChampionSelector_GivenSameResultsInDifferentOrder_ExpectedSameSelection()
    {
        const string incumbentId = "strategy-weak";

        var weak = CreateCandidate(incumbentId, games: 2000, wins: 900, targetSeat: 1);
        var strong = CreateCandidate("strategy-strong", games: 2000, wins: 1100, targetSeat: 1);

        var first = BonesChampionSelector.Select(
            [weak, strong],
            confirmationResults: null,
            incumbentId,
            targetSeat: 1,
            minGames: 1000);
        var second = BonesChampionSelector.Select(
            [strong, weak],
            confirmationResults: null,
            incumbentId,
            targetSeat: 1,
            minGames: 1000);

        Assert.Equal("strategy-strong", first.StrategyId);
        Assert.Equal(first.StrategyId, second.StrategyId);
        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.Reason, second.Reason);
        Assert.True(first.SelectionLcb == second.SelectionLcb);
        Assert.True(first.IncumbentLcb == second.IncumbentLcb);
    }

    private static BonesChampionCandidateResult CreateCandidate(
        string strategyId,
        int games,
        int wins,
        int targetSeat,
        double? targetSeatWinRate = null)
    {
        var overallWinRate = games > 0 ? (double)wins / games : 0.0;
        var winRateBySeat = new Dictionary<int, double>(SeatCount);

        for (var seat = 1; seat <= SeatCount; seat++)
        {
            winRateBySeat[seat] = overallWinRate;
        }

        if (targetSeatWinRate.HasValue)
        {
            winRateBySeat[targetSeat] = targetSeatWinRate.Value;
        }

        return new BonesChampionCandidateResult(
            strategyId,
            targetSeat,
            games,
            wins,
            MeanScoreDifferential: 0.0,
            winRateBySeat);
    }
}