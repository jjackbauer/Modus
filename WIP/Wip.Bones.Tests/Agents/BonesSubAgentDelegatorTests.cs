using Wip.Bones.Agents.Delegation;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesSubAgentDelegatorTests
{
    private const string ChecklistItem = "T3.4: Sub-Agent Delegation";

    private static BonesRoundState CreateSimpleGameState(BonesPlayerId? currentPlayer = null)
    {
        var player = currentPlayer ?? new BonesPlayerId(1);
        var gameId = new BonesGameId("test-game");

        var hands = new Dictionary<BonesPlayerId, BonesHand>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var pid = new BonesPlayerId(seat);
            hands[pid] = new BonesHand(Array.Empty<BonesTile>());
        }

        return new BonesRoundState(
            gameId,
            BonesBoard.Empty,
            new BonesHands(hands),
            player,
            new BonesTile(new BonesPipCount(6), new BonesPipCount(6)),
            passStreak: 0,
            eventLog: Array.Empty<BonesEvent>());
    }

    private static IReadOnlyList<BonesMove> CreateCandidateMoves(BonesPlayerId playerId)
    {
        return new List<BonesMove>
        {
            BonesMove.Play(new BonesMoveId("move-1"), playerId, new BonesTile(new BonesPipCount(6), new BonesPipCount(5)), BonesBoardSide.Left),
            BonesMove.Play(new BonesMoveId("move-2"), playerId, new BonesTile(new BonesPipCount(6), new BonesPipCount(4)), BonesBoardSide.Right),
        };
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DelegateMoveDecision_GivenUncertainState_InvokesSubAgentAndReturnsResult()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var gameState = CreateSimpleGameState(playerId);
        var candidates = CreateCandidateMoves(playerId);
        var expectedMove = candidates[1];

        var subAgent = new TestSubAgent(async (request, ct) =>
        {
            await Task.CompletedTask;
            return new BonesSubAgentResult(
                expectedMove,
                "Chose the right-side play to open the board for follow-up plays",
                TimeSpan.FromMilliseconds(42));
        });

        var delegator = new BonesSubAgentDelegator(subAgent, TimeSpan.FromSeconds(5));
        var request = new BonesDelegationRequest(gameState, candidates, "Two candidates with similar heuristic scores");
        var fallbackMove = candidates[0];

        // Act
        var result = await delegator.DelegateAsync(request, fallbackMove);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedMove, result.ChosenMove);
        Assert.Equal("Chose the right-side play to open the board for follow-up plays", result.Reasoning);
        Assert.True(result.Latency >= TimeSpan.Zero);
        Assert.Equal(expectedMove.MoveId, result.ChosenMove.MoveId);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DelegateMoveDecision_GivenSubAgentTimeout_FallsBackToHeuristic()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var gameState = CreateSimpleGameState(playerId);
        var candidates = CreateCandidateMoves(playerId);
        var fallbackMove = candidates[0];

        // Sub-agent that takes forever (delays longer than timeout)
        var slowSubAgent = new TestSubAgent(async (request, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new BonesSubAgentResult(
                candidates[1],
                "Should never reach here",
                TimeSpan.FromSeconds(30));
        });

        var delegator = new BonesSubAgentDelegator(slowSubAgent, TimeSpan.FromMilliseconds(100));
        var request = new BonesDelegationRequest(gameState, candidates, "Uncertain: candidates have close scores");

        // Act
        var result = await delegator.DelegateAsync(request, fallbackMove);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fallbackMove, result.ChosenMove);
        Assert.Equal(fallbackMove.MoveId, result.ChosenMove.MoveId);
        Assert.Contains("Fallback to heuristic", result.Reasoning);
        Assert.Contains("timed out", result.Reasoning);
        Assert.True(result.Latency >= TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DelegateMoveDecision_GivenSubAgentThrows_FallsBackToHeuristic()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var gameState = CreateSimpleGameState(playerId);
        var candidates = CreateCandidateMoves(playerId);
        var fallbackMove = candidates[0];

        var failingSubAgent = new TestSubAgent((request, ct) =>
            throw new InvalidOperationException("Sub-agent failure"));

        var delegator = new BonesSubAgentDelegator(failingSubAgent, TimeSpan.FromSeconds(5));
        var request = new BonesDelegationRequest(gameState, candidates, "Test failure scenario");

        // Act
        var result = await delegator.DelegateAsync(request, fallbackMove);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fallbackMove, result.ChosenMove);
        Assert.Contains("Fallback to heuristic", result.Reasoning);
        Assert.Contains("failed", result.Reasoning);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DelegateMoveDecision_GivenExternalCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var gameState = CreateSimpleGameState(playerId);
        var candidates = CreateCandidateMoves(playerId);

        var subAgent = new TestSubAgent(async (request, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new BonesSubAgentResult(candidates[0], "delayed", TimeSpan.FromSeconds(10));
        });

        var delegator = new BonesSubAgentDelegator(subAgent, TimeSpan.FromSeconds(30));
        var request = new BonesDelegationRequest(gameState, candidates, "Will be externally cancelled");
        var fallbackMove = candidates[0];

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => delegator.DelegateAsync(request, fallbackMove, cts.Token));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DelegateMoveDecision_GivenSubAgentReturnsFast_RecordsAccurateLatency()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var gameState = CreateSimpleGameState(playerId);
        var candidates = CreateCandidateMoves(playerId);

        var fastSubAgent = new TestSubAgent(async (request, ct) =>
        {
            await Task.Delay(50, ct);
            return new BonesSubAgentResult(candidates[0], "fast choice", TimeSpan.FromMilliseconds(50));
        });

        var delegator = new BonesSubAgentDelegator(fastSubAgent, TimeSpan.FromSeconds(5));
        var request = new BonesDelegationRequest(gameState, candidates, "Quick decision");
        var fallbackMove = candidates[1];

        // Act
        var result = await delegator.DelegateAsync(request, fallbackMove);

        // Assert
        Assert.True(result.Latency >= TimeSpan.FromMilliseconds(50));
        Assert.True(result.Latency < TimeSpan.FromSeconds(1));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task DelegateMoveDecision_GivenSubAgentPicksCandidate_ReturnedMoveIsFromCandidateSet()
    {
        // Arrange
        var playerId = new BonesPlayerId(1);
        var gameState = CreateSimpleGameState(playerId);
        var candidates = CreateCandidateMoves(playerId);

        var subAgent = new TestSubAgent(async (request, ct) =>
        {
            await Task.CompletedTask;
            // Pick the second candidate
            return new BonesSubAgentResult(
                candidates[1],
                "Selected second candidate after deeper analysis",
                TimeSpan.FromMilliseconds(10));
        });

        var delegator = new BonesSubAgentDelegator(subAgent, TimeSpan.FromSeconds(5));
        var request = new BonesDelegationRequest(gameState, candidates, "Pick one of the candidates");
        var fallbackMove = candidates[0];

        // Act
        var result = await delegator.DelegateAsync(request, fallbackMove);

        // Assert
        Assert.NotNull(result);
        var candidateIds = candidates.Select(m => m.MoveId).ToHashSet();
        Assert.Contains(result.ChosenMove.MoveId, candidateIds);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void Constructor_GivenNegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var subAgent = new TestSubAgent((_, _) =>
            Task.FromResult(new BonesSubAgentResult(
                BonesMove.Pass(new BonesMoveId("p"), new BonesPlayerId(1)),
                "dummy",
                TimeSpan.Zero)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BonesSubAgentDelegator(subAgent, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void Constructor_GivenNullSubAgent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BonesSubAgentDelegator(null!, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDelegationRequest_GivenEmptyCandidates_ThrowsArgumentException()
    {
        var playerId = new BonesPlayerId(1);
        Assert.Throws<ArgumentException>(
            () => new BonesDelegationRequest(
                CreateSimpleGameState(playerId),
                Array.Empty<BonesMove>(),
                "context"));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesSubAgentResult_GivenNullMove_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BonesSubAgentResult(null!, "reason", TimeSpan.Zero));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesSubAgentResult_GivenNullReasoning_ThrowsArgumentNullException()
    {
        var move = BonesMove.Pass(new BonesMoveId("p"), new BonesPlayerId(1));
        Assert.Throws<ArgumentNullException>(
            () => new BonesSubAgentResult(move, null!, TimeSpan.Zero));
    }

    /// <summary>
    /// Test sub-agent that delegates to a provided function, useful for
    /// controlling sub-agent behavior in tests without real LLM calls.
    /// </summary>
    private sealed class TestSubAgent : IBonesSubAgent
    {
        private readonly Func<BonesDelegationRequest, CancellationToken, Task<BonesSubAgentResult>> _decide;

        public TestSubAgent(
            Func<BonesDelegationRequest, CancellationToken, Task<BonesSubAgentResult>> decide)
        {
            _decide = decide ?? throw new ArgumentNullException(nameof(decide));
        }

        public Task<BonesSubAgentResult> DecideAsync(
            BonesDelegationRequest request,
            CancellationToken cancellationToken)
        {
            return _decide(request, cancellationToken);
        }
    }
}
