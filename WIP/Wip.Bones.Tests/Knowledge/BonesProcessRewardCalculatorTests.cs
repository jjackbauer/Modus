using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesProcessRewardCalculatorTests
{
    private const string ChecklistItem = "T2.3: Process rewards — score delta, compilation, diversity/novelty";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenLossWithScoreImprovement_ReturnsPositiveScoreDeltaReward()
    {
        // Arrange: candidate loses but has higher average score than incumbent
        var candidateId = new BonesStrategyId("candidate-v1");
        var incumbentId = new BonesStrategyId("incumbent-v1");
        var playerId = new BonesPlayerId(1);

        // Two matches: candidate loses both but averages higher score
        // Match 1: candidate scores 8, incumbent scores 7 → winner is someone else
        // Match 2: candidate scores 12, incumbent scores 9 → winner is someone else
        var matchResults = new[]
        {
            CreateMatchWithScores(
                new BonesGameId("game-1"),
                winner: new BonesPlayerId(2),
                (candidatePlayer: playerId, candidateScore: 8),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 12),
                (candidatePlayer: null, candidateScore: 5),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 3)),
            CreateMatchWithScores(
                new BonesGameId("game-2"),
                winner: new BonesPlayerId(3),
                (candidatePlayer: playerId, candidateScore: 12),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 7),
                (candidatePlayer: new BonesPlayerId(3), candidateScore: 14),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 4)),
        };

        double incumbentBaselineAverageScore = 8.0; // incumbent historically averaged 8

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: incumbentBaselineAverageScore,
            isCompilable: false,
            previouslyRejectedSources: null);

        // Assert: candidate average = (8 + 12) / 2 = 10, baseline = 8, delta = +2
        Assert.True(rewards.ScoreDeltaReward > 0,
            $"Expected positive score delta reward, got {rewards.ScoreDeltaReward}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenCompilableScript_ReturnsCompilationSuccessReward()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var candidateId = new BonesStrategyId("compile-test-v1");

        var matchResults = new[]
        {
            CreateMatchWithScores(
                new BonesGameId("game-compile-1"),
                winner: new BonesPlayerId(2),
                (candidatePlayer: playerId, candidateScore: 3),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 14),
                (candidatePlayer: new BonesPlayerId(3), candidateScore: 10),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 6)),
        };

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: 10.0,
            isCompilable: true,
            previouslyRejectedSources: null);

        // Assert
        Assert.True(rewards.CompilationSuccessReward > 0,
            $"Expected positive compilation reward, got {rewards.CompilationSuccessReward}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenNoCompilation_ReturnsZeroCompilationReward()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var candidateId = new BonesStrategyId("no-compile-v1");

        var matchResults = new[]
        {
            CreateMatchWithScores(
                new BonesGameId("game-nc-1"),
                winner: playerId,
                (candidatePlayer: playerId, candidateScore: 15),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 8),
                (candidatePlayer: new BonesPlayerId(3), candidateScore: 6),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 4)),
        };

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: 5.0,
            isCompilable: false,
            previouslyRejectedSources: null);

        // Assert
        Assert.Equal(0, rewards.CompilationSuccessReward);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenNearDuplicateOfRejectedStrategy_ReturnsNegativeDiversityPenalty()
    {
        // Arrange: candidate source is highly similar to a previously rejected source
        var playerId = new BonesPlayerId(1);
        var candidateId = new BonesStrategyId("near-dup-v1");

        var matchResults = Array.Empty<BonesMatchResult>();

        var rejectedSource = @"
public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves[0];
    }
}";

        var candidateSource = @"
public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves[1];
    }
}";

        var previouslyRejectedSources = new[] { rejectedSource };

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: 0,
            isCompilable: false,
            candidateSource: candidateSource,
            previouslyRejectedSources: previouslyRejectedSources);

        // Assert: near-duplicate should produce negative diversity reward
        Assert.True(rewards.DiversityNoveltyReward < 0,
            $"Expected negative diversity penalty for near-duplicate, got {rewards.DiversityNoveltyReward}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenCompletelyNovelStrategy_ReturnsZeroDiversityReward()
    {
        // Arrange: candidate source is very different from any rejected source
        var playerId = new BonesPlayerId(1);
        var candidateId = new BonesStrategyId("novel-v1");

        var matchResults = Array.Empty<BonesMatchResult>();

        var rejectedSource = @"
public sealed class OldStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves[0];
    }
}";

        var candidateSource = @"
// A completely different strategy using tile scoring
public sealed class NewStrategy : IBonesPlayerSlot
{
    private static readonly Dictionary<int, int> TileScores = new()
    {
        [0] = 0, [1] = 1, [2] = 2, [3] = 3,
        [4] = 4, [5] = 5, [6] = 6, [7] = 7,
        [8] = 8, [9] = 9, [10] = 10, [11] = 11,
        [12] = 12, [13] = 13, [14] = 14, [15] = 15,
    };

    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        BonesMove? best = null;
        int bestScore = int.MinValue;
        foreach (var move in legalMoves)
        {
            int score = TileScores[move.Tile.Value];
            if (score > bestScore)
            {
                bestScore = score;
                best = move;
            }
        }
        return best!;
    }
}";

        var previouslyRejectedSources = new[] { rejectedSource };

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: 0,
            isCompilable: false,
            candidateSource: candidateSource,
            previouslyRejectedSources: previouslyRejectedSources);

        // Assert: completely different strategy, no penalty
        Assert.Equal(0, rewards.DiversityNoveltyReward);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenMultiComponentScenario_ReturnsAggregateReward()
    {
        // Arrange: win with compilation and no duplicates
        var playerId = new BonesPlayerId(1);
        var candidateId = new BonesStrategyId("multi-v1");

        var matchResults = new[]
        {
            CreateMatchWithScores(
                new BonesGameId("game-multi-1"),
                winner: playerId,
                (candidatePlayer: playerId, candidateScore: 12),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 7),
                (candidatePlayer: new BonesPlayerId(3), candidateScore: 5),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 3)),
        };

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: 6.0,
            isCompilable: true,
            previouslyRejectedSources: null);

        // Assert: all components contribute to aggregate
        Assert.True(rewards.ScoreDeltaReward > 0,
            $"Score delta positive expected, got {rewards.ScoreDeltaReward}");
        Assert.True(rewards.CompilationSuccessReward > 0,
            $"Compilation reward positive expected, got {rewards.CompilationSuccessReward}");
        Assert.True(rewards.AggregateReward > rewards.ScoreDeltaReward,
            $"Aggregate {rewards.AggregateReward} should exceed score delta {rewards.ScoreDeltaReward}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ComputeRewards_GivenScoreRegressionInLoss_ReturnsNegativeScoreDeltaReward()
    {
        // Arrange: candidate loses AND scores lower on average than baseline
        var playerId = new BonesPlayerId(1);
        var candidateId = new BonesStrategyId("regression-v1");

        var matchResults = new[]
        {
            CreateMatchWithScores(
                new BonesGameId("game-reg-1"),
                winner: new BonesPlayerId(2),
                (candidatePlayer: playerId, candidateScore: 4),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 15),
                (candidatePlayer: new BonesPlayerId(3), candidateScore: 10),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 7)),
            CreateMatchWithScores(
                new BonesGameId("game-reg-2"),
                winner: new BonesPlayerId(3),
                (candidatePlayer: playerId, candidateScore: 3),
                (candidatePlayer: new BonesPlayerId(2), candidateScore: 9),
                (candidatePlayer: new BonesPlayerId(3), candidateScore: 14),
                (candidatePlayer: new BonesPlayerId(4), candidateScore: 8)),
        };

        double incumbentBaselineAverageScore = 8.0; // incumbent averages 8, candidate averages 3.5

        var calculator = new BonesProcessRewardCalculator();

        // Act
        var rewards = calculator.ComputeRewards(
            learningPlayerId: playerId,
            candidateStrategyId: candidateId,
            matchResults: matchResults,
            incumbentBaselineAverageScore: incumbentBaselineAverageScore,
            isCompilable: false,
            previouslyRejectedSources: null);

        // Assert: candidate avg = (4+3)/2 = 3.5 < 8.0 → negative delta reward
        Assert.True(rewards.ScoreDeltaReward < 0,
            $"Expected negative score delta reward for regression, got {rewards.ScoreDeltaReward}");
    }

    private static BonesMatchResult CreateMatchWithScores(
        BonesGameId gameId,
        BonesPlayerId winner,
        params (BonesPlayerId? candidatePlayer, int candidateScore)[] scorePairs)
    {
        var scores = new Dictionary<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var pid = new BonesPlayerId(seat);
            var pair = scorePairs.FirstOrDefault(p => p.candidatePlayer?.Seat == seat);
            scores[pid] = pair.candidatePlayer.HasValue ? pair.candidateScore : 0;
        }

        var round = new BonesMatchRoundRecord(
            1,
            new BonesRoundScore(
                new BonesPlayerId(scorePairs.FirstOrDefault(p => p.candidatePlayer.HasValue).candidatePlayer?.Seat ?? 1),
                scorePairs.FirstOrDefault(p => p.candidatePlayer.HasValue).candidateScore,
                BonesRoundOutcomeKind.Dominoes),
            Array.Empty<BonesEvent>());

        return new BonesMatchResult(
            gameId,
            winner,
            scores,
            new[] { round },
            Array.Empty<BonesEvent>());
    }
}
