using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesMatchSimulatorTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.MatchSimulator;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchSimulator_GivenFixedSeed_ExpectedReproducibleTranscriptAndFinalScore()
    {
        var config = CreateConfig(seed: 4242, targetScore: 100);
        var simulator = new BonesMatchSimulator();

        var firstRun = simulator.RunMatch(config);
        var secondRun = simulator.RunMatch(config);

        Assert.Equal(firstRun.Winner, secondRun.Winner);
        Assert.Equal(firstRun.Rounds.Length, secondRun.Rounds.Length);
        Assert.Equal(firstRun.Transcript.Length, secondRun.Transcript.Length);

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            Assert.Equal(firstRun.CumulativeScores[playerId], secondRun.CumulativeScores[playerId]);
        }

        for (var index = 0; index < firstRun.Transcript.Length; index++)
        {
            var firstEvent = firstRun.Transcript[index];
            var secondEvent = secondRun.Transcript[index];

            Assert.Equal(firstEvent.TurnIndex, secondEvent.TurnIndex);
            Assert.Equal(firstEvent.PlayerId, secondEvent.PlayerId);
            Assert.Equal(firstEvent.Kind, secondEvent.Kind);
            Assert.Equal(firstEvent.Tile, secondEvent.Tile);
            Assert.Equal(firstEvent.Side, secondEvent.Side);
        }

        for (var roundIndex = 0; roundIndex < firstRun.Rounds.Length; roundIndex++)
        {
            var firstRound = firstRun.Rounds[roundIndex];
            var secondRound = secondRun.Rounds[roundIndex];

            Assert.Equal(firstRound.RoundNumber, secondRound.RoundNumber);
            Assert.Equal(firstRound.Score.Winner, secondRound.Score.Winner);
            Assert.Equal(firstRound.Score.Points, secondRound.Score.Points);
            Assert.Equal(firstRound.Score.Outcome, secondRound.Score.Outcome);
        }

        Assert.True(firstRun.CumulativeScores[firstRun.Winner] >= config.TargetScore);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchSimulator_GivenTargetScore_ExpectedMatchEndsWhenCumulativeScoreReached()
    {
        const int targetScore = 15;
        var config = CreateConfig(seed: 9001, targetScore);
        var simulator = new BonesMatchSimulator();

        var result = simulator.RunMatch(config);

        Assert.NotEmpty(result.Rounds);
        Assert.Equal(targetScore, config.TargetScore);
        Assert.True(result.CumulativeScores[result.Winner] >= targetScore);

        foreach (var (playerId, score) in result.CumulativeScores)
        {
            if (playerId == result.Winner)
                continue;

            Assert.True(score < targetScore);
        }

        var cumulativeFromRounds = new Dictionary<BonesPlayerId, int>
        {
            [new(1)] = 0,
            [new(2)] = 0,
            [new(3)] = 0,
            [new(4)] = 0,
        };

        foreach (var round in result.Rounds)
            cumulativeFromRounds[round.Score.Winner] += round.Score.Points;

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            Assert.Equal(cumulativeFromRounds[playerId], result.CumulativeScores[playerId]);
        }

        var runningScores = new Dictionary<BonesPlayerId, int>
        {
            [new(1)] = 0,
            [new(2)] = 0,
            [new(3)] = 0,
            [new(4)] = 0,
        };

        var targetReachedRoundIndex = -1;
        for (var roundIndex = 0; roundIndex < result.Rounds.Length; roundIndex++)
        {
            var round = result.Rounds[roundIndex];
            runningScores[round.Score.Winner] += round.Score.Points;

            if (runningScores[round.Score.Winner] >= targetScore)
            {
                targetReachedRoundIndex = roundIndex;
                break;
            }
        }

        Assert.True(targetReachedRoundIndex >= 0);
        Assert.Equal(result.Rounds.Length - 1, targetReachedRoundIndex);
    }

    private static BonesMatchConfig CreateConfig(int seed, int targetScore) =>
        new(
            new BonesGameId("match-simulator"),
            seed,
            targetScore,
            CreateFirstLegalMoveSlots());

    private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreateFirstLegalMoveSlots()
    {
        var slot = new BonesFirstLegalMovePlayerSlot();
        return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
        {
            [new(1)] = slot,
            [new(2)] = slot,
            [new(3)] = slot,
            [new(4)] = slot,
        };
    }
}