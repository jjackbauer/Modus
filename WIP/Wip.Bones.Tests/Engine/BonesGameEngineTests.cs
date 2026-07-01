using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesGameEngineTests
{
    private const string GameEngineChecklistItem = BonesRequirementsChecklistItems.GameEngine;
    private const string LegalMovesApiChecklistItem = BonesRequirementsChecklistItems.LegalMovesApi;

    private readonly BonesGameEngine _engine = new();

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenNewRound_DealsSevenTilesToEachOfFourPlayersWithEmptyBoneyard()
    {
        var state = _engine.StartRound(CreateConfig(seed: 42));

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var hand = state.Hands.GetHand(new BonesPlayerId(seat));
            Assert.Equal(7, hand.Tiles.Length);
        }

        var dealtTiles = state.Hands.HandsByPlayer.Values
            .SelectMany(static hand => hand.Tiles)
            .ToArray();

        Assert.Equal(28, dealtTiles.Length);
        Assert.Equal(28, dealtTiles.Distinct().Count());
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenOpeningState_ResolvesLeaderByHighestDoubleOrHighestPipTotalAmongFourHands()
    {
        var doubleSixState = CreateOpeningState(
            new BonesPlayerId(1), [new BonesTile(new(0), new(1))],
            new BonesPlayerId(2), [new BonesTile(new(6), new(6)), new BonesTile(new(0), new(2))],
            new BonesPlayerId(3), [new BonesTile(new(5), new(5))],
            new BonesPlayerId(4), [new BonesTile(new(4), new(4))]);

        Assert.Equal(new BonesPlayerId(2), doubleSixState.CurrentPlayer);
        Assert.Equal(new BonesTile(new(6), new(6)), doubleSixState.OpeningTile);

        var highestPipState = CreateOpeningState(
            new BonesPlayerId(1), [new BonesTile(new(2), new(3))],
            new BonesPlayerId(2), [new BonesTile(new(5), new(6))],
            new BonesPlayerId(3), [new BonesTile(new(4), new(5))],
            new BonesPlayerId(4), [new BonesTile(new(0), new(2))]);

        Assert.Equal(new BonesPlayerId(2), highestPipState.CurrentPlayer);
        Assert.Equal(new BonesTile(new(5), new(6)), highestPipState.OpeningTile);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenLegalMoveOnOpenEnd_ExpectedBoardAndHandUpdateWithEventLogged()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(3), new(5))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var legalMoves = _engine.GetLegalMoves(state, new BonesPlayerId(2));
        var playMove = legalMoves.First(move => move.Tile == new BonesTile(new(3), new(4)) && move.Side == BonesBoardSide.Right);

        var updated = _engine.ApplyMove(state, playMove);

        Assert.Equal(2, updated.Board.Tiles.Length);
        Assert.Equal(new BonesPipCount(4), updated.Board.RightEnd!.Value.Pip);
        Assert.Equal(1, updated.Hands.GetHand(new BonesPlayerId(2)).Tiles.Length);
        Assert.Equal(new BonesPlayerId(3), updated.CurrentPlayer);
        Assert.Single(updated.EventLog);
        Assert.Equal(BonesEventKind.Play, updated.EventLog[0].Kind);
        Assert.Equal(new BonesTile(new(3), new(4)), updated.EventLog[0].Tile);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenIllegalMoveNotInHand_ExpectedDeterministicRejectWithoutStateMutation()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(1),
            boardTiles: [new BonesTile(new(2), new(2))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(2), new(3))]),
                [new(2)] = new([new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(4))]),
                [new(4)] = new([new BonesTile(new(5), new(6))]),
            });

        var illegalMove = BonesMove.Play(
            new BonesMoveId("illegal-not-in-hand"),
            new BonesPlayerId(1),
            new BonesTile(new(2), new(5)),
            BonesBoardSide.Right);

        var exception = Assert.Throws<BonesMoveRejectedException>(() => _engine.ApplyMove(state, illegalMove));

        Assert.Contains("not legal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(state, state);
        Assert.Empty(state.EventLog);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenIllegalMoveWrongPip_ExpectedDeterministicRejectWithoutStateMutation()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(1),
            boardTiles: [new BonesTile(new(2), new(2))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(2), new(3)), new BonesTile(new(4), new(6))]),
                [new(2)] = new([new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(4))]),
                [new(4)] = new([new BonesTile(new(5), new(6))]),
            });

        var illegalMove = BonesMove.Play(
            new BonesMoveId("illegal-wrong-pip"),
            new BonesPlayerId(1),
            new BonesTile(new(4), new(6)),
            BonesBoardSide.Right);

        var exception = Assert.Throws<BonesMoveRejectedException>(() => _engine.ApplyMove(state, illegalMove));

        Assert.Contains("not legal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(state.EventLog);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenNoLegalMoves_ExpectedPassAdvancesTurnToNextSeatWithoutBoardChange()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(1), new(1))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(2))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(5), new(6))]),
                [new(3)] = new([new BonesTile(new(0), new(3))]),
                [new(4)] = new([new BonesTile(new(2), new(5))]),
            });

        var legalMoves = _engine.GetLegalMoves(state, new BonesPlayerId(2));
        var passMove = Assert.Single(legalMoves);
        Assert.True(passMove.IsPass);

        var updated = _engine.ApplyMove(state, passMove);

        Assert.Equal(state.Board, updated.Board);
        Assert.Equal(new BonesPlayerId(3), updated.CurrentPlayer);
        Assert.Equal(1, updated.PassStreak);
        Assert.Single(updated.EventLog);
        Assert.Equal(BonesEventKind.Pass, updated.EventLog[0].Kind);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenFourConsecutivePasses_ExpectedRoundCompleteWithBlockedOutcome()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(1),
            boardTiles: [new BonesTile(new(0), new(0))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(1), new(2))]),
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(3)] = new([new BonesTile(new(5), new(6))]),
                [new(4)] = new([new BonesTile(new(1), new(3))]),
            },
            passStreak: 3);

        var passMove = Assert.Single(_engine.GetLegalMoves(state, new BonesPlayerId(1)));
        var updated = _engine.ApplyMove(state, passMove);

        Assert.True(_engine.IsRoundComplete(updated));
        Assert.Equal(BonesRoundOutcomeKind.Blocked, updated.Outcome);
        Assert.Equal(new BonesPlayerId(1), updated.Winner);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenEmptyHand_ExpectedRoundCompleteWithDominoesOutcome()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(4),
            boardTiles: [new BonesTile(new(2), new(4))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(1), new(3))]),
                [new(3)] = new([new BonesTile(new(3), new(5))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var playMove = Assert.Single(_engine.GetLegalMoves(state, new BonesPlayerId(4)));
        var updated = _engine.ApplyMove(state, playMove);

        Assert.True(_engine.IsRoundComplete(updated));
        Assert.Equal(BonesRoundOutcomeKind.Dominoes, updated.Outcome);
        Assert.Equal(new BonesPlayerId(4), updated.Winner);
        Assert.Empty(updated.Hands.GetHand(new BonesPlayerId(4)).Tiles);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenCompletedRound_ExpectedPipScoreEqualsSumOfThreeNonWinnersRemainingTiles()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(1), new(1))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(2))]),
                [new(2)] = new([]),
                [new(3)] = new([new BonesTile(new(3), new(3))]),
                [new(4)] = new([new BonesTile(new(4), new(5)), new BonesTile(new(0), new(0))]),
            },
            outcome: BonesRoundOutcomeKind.Dominoes,
            winner: new BonesPlayerId(2));

        var score = _engine.ScoreRound(state);

        Assert.Equal(new BonesPlayerId(2), score.Winner);
        Assert.Equal(BonesRoundOutcomeKind.Dominoes, score.Outcome);
        Assert.Equal(2 + 6 + 9, score.Points);
    }

    [Fact]
    [Trait("ChecklistItem", GameEngineChecklistItem)]
    public void BonesGameEngine_GivenFixedSeedOpening_ExpectedCurrentPlayerHoldsOpeningTile()
    {
        var state = _engine.StartRound(CreateConfig(seed: 1234));
        var openerHand = state.Hands.GetHand(state.CurrentPlayer);

        Assert.Contains(state.OpeningTile, openerHand.Tiles);

        var openingMoves = _engine.GetLegalMoves(state, state.CurrentPlayer);
        Assert.Single(openingMoves);
        Assert.Equal(state.OpeningTile, openingMoves[0].Tile);
    }

    [Fact]
    [Trait("ChecklistItem", LegalMovesApiChecklistItem)]
    public void GetLegalMoves_GivenMidgameBoard_ExpectedReturnsOnlyMatchingOpenEndTilesFromCurrentHand()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(3), new(5))]),
                [new(2)] = new([
                    new BonesTile(new(3), new(4)),
                    new BonesTile(new(0), new(1)),
                    new BonesTile(new(5), new(6)),
                ]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var legalMoves = _engine.GetLegalMoves(state, new BonesPlayerId(2));

        Assert.All(legalMoves, move => Assert.False(move.IsPass));
        Assert.All(legalMoves, move => Assert.Equal(new BonesPlayerId(2), move.PlayerId));
        Assert.All(legalMoves, move => Assert.Contains(move.Tile!.Value, state.Hands.GetHand(new BonesPlayerId(2)).Tiles));

        var leftEnd = state.Board.LeftEnd!.Value.Pip;
        var rightEnd = state.Board.RightEnd!.Value.Pip;

        foreach (var move in legalMoves)
        {
            var tile = move.Tile!.Value;
            var openEnd = move.Side == BonesBoardSide.Left ? leftEnd : rightEnd;
            Assert.True(tile.LowPip == openEnd || tile.HighPip == openEnd);
        }

        var matchingTile = new BonesTile(new(3), new(4));
        Assert.Contains(legalMoves, move => move.Tile == matchingTile && move.Side == BonesBoardSide.Left);
        Assert.Contains(legalMoves, move => move.Tile == matchingTile && move.Side == BonesBoardSide.Right);
        Assert.DoesNotContain(legalMoves, move => move.Tile == new BonesTile(new(0), new(1)));
        Assert.DoesNotContain(legalMoves, move => move.Tile == new BonesTile(new(5), new(6)));
    }

    [Fact]
    [Trait("ChecklistItem", LegalMovesApiChecklistItem)]
    public void GetLegalMoves_GivenEveryReturnedMove_ExpectedApplyMoveAcceptsWithoutReject()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(1),
            boardTiles: [new BonesTile(new(2), new(2)), new BonesTile(new(2), new(5))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([
                    new BonesTile(new(5), new(5)),
                    new BonesTile(new(5), new(6)),
                    new BonesTile(new(0), new(3)),
                ]),
                [new(2)] = new([new BonesTile(new(1), new(4))]),
                [new(3)] = new([new BonesTile(new(0), new(6))]),
                [new(4)] = new([new BonesTile(new(3), new(4))]),
            });

        var legalMoves = _engine.GetLegalMoves(state, new BonesPlayerId(1));
        Assert.NotEmpty(legalMoves);

        foreach (var move in legalMoves)
        {
            var updated = _engine.ApplyMove(state, move);
            Assert.True(updated.EventLog.Length > state.EventLog.Length);
        }
    }

    [Fact]
    [Trait("ChecklistItem", LegalMovesApiChecklistItem)]
    public void BonesEventLog_GivenSequentialPlays_ExpectedAppendOnlyWithMonotonicTurnIndexAndFourSeatRotation()
    {
        var state = _engine.StartRound(new BonesRoundConfig(new BonesGameId("event-log"), 90210));
        var seatsSeen = new HashSet<int>();

        for (var turn = 0; turn < 5 && state.Outcome is null; turn++)
        {
            var priorLog = state.EventLog;
            var activePlayer = state.CurrentPlayer;
            var move = _engine.GetLegalMoves(state, activePlayer).First();

            state = _engine.ApplyMove(state, move);

            Assert.Equal(priorLog.Length + 1, state.EventLog.Length);
            for (var index = 0; index < priorLog.Length; index++)
                Assert.Equal(priorLog[index], state.EventLog[index]);

            var logged = state.EventLog[^1];
            Assert.Equal(turn, logged.TurnIndex);
            Assert.Equal(activePlayer, logged.PlayerId);
            Assert.True(logged.TurnIndex > 0 || turn == 0);
            if (turn > 0)
                Assert.Equal(state.EventLog[turn - 1].TurnIndex + 1, logged.TurnIndex);

            seatsSeen.Add(activePlayer.Seat);

            if (activePlayer.Seat == BonesPlayerId.MaxSeat)
                Assert.Equal(new BonesPlayerId(BonesPlayerId.MinSeat), state.CurrentPlayer);
        }

        Assert.Equal(BonesTableConfig.FixedPlayerCount, seatsSeen.Count);
    }

    [Fact]
    [Trait("ChecklistItem", LegalMovesApiChecklistItem)]
    public void ApplyMove_GivenSeatFourPlay_ExpectedWrapsTurnToSeatOne()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(4),
            boardTiles: [new BonesTile(new(2), new(2))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(3)] = new([new BonesTile(new(5), new(6))]),
                [new(4)] = new([new BonesTile(new(2), new(3)), new BonesTile(new(1), new(5))]),
            });

        var playMove = _engine.GetLegalMoves(state, new BonesPlayerId(4)).First(move => !move.IsPass);
        var updated = _engine.ApplyMove(state, playMove);

        Assert.Equal(new BonesPlayerId(1), updated.CurrentPlayer);
        Assert.Single(updated.EventLog);
        Assert.Equal(new BonesPlayerId(4), updated.EventLog[0].PlayerId);
    }

    [Fact]
    [Trait("ChecklistItem", LegalMovesApiChecklistItem)]
    public void ApplyMove_GivenOutOfTurnMove_ExpectedDeterministicRejectWithoutStateMutation()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(3), new(5))]),
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        Assert.Empty(_engine.GetLegalMoves(state, new BonesPlayerId(1)));

        var outOfTurnMove = BonesMove.Play(
            new BonesMoveId("out-of-turn"),
            new BonesPlayerId(1),
            new BonesTile(new(3), new(5)),
            BonesBoardSide.Left);

        var exception = Assert.Throws<BonesMoveRejectedException>(() => _engine.ApplyMove(state, outOfTurnMove));

        Assert.Contains("not active", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new BonesPlayerId(2), state.CurrentPlayer);
        Assert.Empty(state.EventLog);
    }

    [Fact]
    [Trait("ChecklistItem", LegalMovesApiChecklistItem)]
    public void GetLegalMoves_GivenNonActivePlayer_ExpectedReturnsEmpty()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(3),
            boardTiles: [new BonesTile(new(4), new(4))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(4), new(5))]),
                [new(2)] = new([new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(4), new(6))]),
                [new(4)] = new([new BonesTile(new(2), new(3))]),
            });

        Assert.Empty(_engine.GetLegalMoves(state, new BonesPlayerId(1)));
        Assert.Empty(_engine.GetLegalMoves(state, new BonesPlayerId(2)));
        Assert.NotEmpty(_engine.GetLegalMoves(state, new BonesPlayerId(3)));
        Assert.Empty(_engine.GetLegalMoves(state, new BonesPlayerId(4)));
    }

    private static BonesRoundConfig CreateConfig(int seed) =>
        new(new BonesGameId("game-1"), seed);

    private BonesRoundState CreateOpeningState(
        BonesPlayerId playerOne,
        IReadOnlyList<BonesTile> handOne,
        BonesPlayerId playerTwo,
        IReadOnlyList<BonesTile> handTwo,
        BonesPlayerId playerThree,
        IReadOnlyList<BonesTile> handThree,
        BonesPlayerId playerFour,
        IReadOnlyList<BonesTile> handFour)
    {
        var config = CreateConfig(seed: 99);
        var baseState = _engine.StartRound(config);
        var hands = new Dictionary<BonesPlayerId, BonesHand>
        {
            [playerOne] = new(handOne),
            [playerTwo] = new(handTwo),
            [playerThree] = new(handThree),
            [playerFour] = new(handFour),
        };

        var opening = ResolveOpeningForTest(hands);
        return new BonesRoundState(
            baseState.GameId,
            BonesBoard.Empty,
            new BonesHands(hands),
            opening.PlayerId,
            opening.Tile,
            passStreak: 0,
            eventLog: []);
    }

    private static BonesRoundState CreateMidgameState(
        BonesPlayerId currentPlayer,
        IReadOnlyList<BonesTile> boardTiles,
        IReadOnlyDictionary<BonesPlayerId, BonesHand> hands,
        int passStreak = 0,
        BonesRoundOutcomeKind? outcome = null,
        BonesPlayerId? winner = null) =>
        new(
            new BonesGameId("midgame"),
            BonesBoardChainBuilder.FromLegacyCanonicalChain(boardTiles),
            new BonesHands(hands),
            currentPlayer,
            new BonesTile(new(0), new(0)),
            passStreak,
            [],
            outcome,
            winner);

    private static (BonesPlayerId PlayerId, BonesTile Tile) ResolveOpeningForTest(
        IReadOnlyDictionary<BonesPlayerId, BonesHand> hands)
    {
        var bestDouble = (-1, BonesPlayerId.MinSeat, default(BonesTile));
        foreach (var (playerId, hand) in hands)
        {
            foreach (var tile in hand.Tiles)
            {
                if (!tile.IsDouble)
                    continue;

                var pip = tile.LowPip.Value;
                if (pip > bestDouble.Item1 || (pip == bestDouble.Item1 && playerId.Seat < bestDouble.Item2))
                    bestDouble = (pip, playerId.Seat, tile);
            }
        }

        if (bestDouble.Item1 >= 0)
            return (new BonesPlayerId(bestDouble.Item2), bestDouble.Item3);

        var bestTile = (-1, -1, BonesPlayerId.MinSeat, default(BonesTile));
        foreach (var (playerId, hand) in hands)
        {
            foreach (var tile in hand.Tiles)
            {
                var total = tile.TotalPips;
                var high = tile.HighPip.Value;
                if (total > bestTile.Item1
                    || (total == bestTile.Item1 && high > bestTile.Item2)
                    || (total == bestTile.Item1 && high == bestTile.Item2 && playerId.Seat < bestTile.Item3))
                {
                    bestTile = (total, high, playerId.Seat, tile);
                }
            }
        }

        return (new BonesPlayerId(bestTile.Item3), bestTile.Item4);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.PerMoveInvariant)]
    public void BonesGameEngine_GivenGreedyRound_ExpectedChainEdgeInvariantAfterEveryPlay()
    {
        const int seed = 4242;
        var slot = new BonesFirstLegalMovePlayerSlot();
        var roundConfig = new BonesRoundConfig(
            new BonesGameId("greedy-round-edge-invariant"),
            HashCode.Combine(seed, 1));
        var state = _engine.StartRound(roundConfig);

        while (!_engine.IsRoundComplete(state))
        {
            var activePlayer = state.CurrentPlayer;
            var move = slot.ChooseMove(state, _engine.GetLegalMoves(state, activePlayer));
            state = _engine.ApplyMove(state, move);

            if (!move.IsPass)
                state.Board.ValidateChainEdges();
        }
    }

    /// <summary>
    /// Replays learning-79da18443e60429bbc00508278b6f8be round 1 through the engine.
    /// Play order is derived from the oriented final chain; seat assignment follows the host transcript.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.LearningMatchRegression)]
    public void BonesBoard_GivenLearningMatch79daFinalChainReplayed_ExpectedLinearChainEdgesMatch()
    {
        var state = CreateLearningMatch79daOpeningState();
        var openingMove = _engine.GetLegalMoves(state, new BonesPlayerId(3)).Single();
        state = _engine.ApplyMove(state, openingMove);
        state.Board.ValidateChainEdges();

        state = ReplayLearningMatch79daTilesThroughEngine(state);

        Assert.Equal(14, state.Board.Tiles.Length);
        state.Board.ValidateChainEdges();

        var expectedTiles = LearningMatch79daFinalChainTiles();
        Assert.Equal(
            expectedTiles
                .OrderBy(static tile => tile.LowPip.Value)
                .ThenBy(static tile => tile.HighPip.Value)
                .ToArray(),
            state.Board.Tiles.Select(static chainTile => chainTile.Tile)
                .OrderBy(static tile => tile.LowPip.Value)
                .ThenBy(static tile => tile.HighPip.Value)
                .ToArray());

        var oneThreeIndex = Array.FindIndex(
            state.Board.Tiles.ToArray(),
            chainTile => chainTile.Tile == new BonesTile(new(1), new(3)));
        var oneFiveIndex = oneThreeIndex + 1;

        Assert.True(TryGetLearningMatchSandwichEdge(state.Board, out var sandwichPip));
        Assert.Equal(new BonesPipCount(1), sandwichPip);
        Assert.Equal(
            state.Board.Tiles[oneThreeIndex].ChainRightPip,
            state.Board.Tiles[oneFiveIndex].ChainLeftPip);
    }

    private static bool TryGetLearningMatchSandwichEdge(BonesBoard board, out BonesPipCount sharedPip)
    {
        for (var index = 0; index < board.Tiles.Length - 1; index++)
        {
            if (board.Tiles[index].Tile == new BonesTile(new(1), new(3))
                && board.Tiles[index + 1].Tile == new BonesTile(new(1), new(5)))
            {
                sharedPip = board.Tiles[index].ChainRightPip;
                return true;
            }
        }

        sharedPip = default;
        return false;
    }

    private BonesRoundState ReplayLearningMatch79daTilesThroughEngine(BonesRoundState state)
    {
        var players = LearningMatch79daPlayerByTile();
        var remaining = LearningMatch79daFinalChainTiles()
            .Where(tile => tile != state.OpeningTile)
            .ToList();

        Assert.True(TryReplayRemainingTiles(ref state, remaining, players));
        return state;
    }

    private bool TryReplayRemainingTiles(
        ref BonesRoundState state,
        List<BonesTile> remaining,
        IReadOnlyDictionary<BonesTile, BonesPlayerId> players)
    {
        if (remaining.Count == 0)
            return TryGetLearningMatchSandwichEdge(state.Board, out _);

        foreach (var tile in remaining.ToArray())
        {
            var playerId = players[tile];
            var prepared = WithCurrentPlayerAndTile(state, playerId, tile);
            var legalMoves = _engine.GetLegalMoves(prepared, playerId)
                .Where(move => !move.IsPass && move.Tile == tile)
                .ToArray();

            foreach (var move in legalMoves)
            {
                var nextState = _engine.ApplyMove(prepared, move);
                nextState.Board.ValidateChainEdges();

                var nextRemaining = remaining.ToList();
                nextRemaining.Remove(tile);

                if (TryReplayRemainingTiles(ref nextState, nextRemaining, players))
                {
                    state = nextState;
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<BonesTile, BonesPlayerId> LearningMatch79daPlayerByTile() =>
        LearningMatch79daReplayMoves()
            .Where(replayMove => !replayMove.IsPass && replayMove.Tile.HasValue)
            .ToDictionary(replayMove => replayMove.Tile!.Value, replayMove => replayMove.PlayerId);

    private static BonesRoundState WithCurrentPlayerAndTile(
        BonesRoundState state,
        BonesPlayerId playerId,
        BonesTile tile)
    {
        var hand = state.Hands.GetHand(playerId);
        var tiles = hand.Tiles.Contains(tile) ? hand.Tiles : hand.Tiles.Add(tile);
        var hands = state.Hands.HandsByPlayer.SetItem(playerId, new BonesHand(tiles));

        return new BonesRoundState(
            state.GameId,
            state.Board,
            new BonesHands(hands),
            playerId,
            state.OpeningTile,
            passStreak: 0,
            state.EventLog,
            state.Outcome,
            state.Winner);
    }

    private static BonesRoundState CreateLearningMatch79daOpeningState()
    {
        var openingTile = new BonesTile(new(6), new(6));
        var hands = new Dictionary<BonesPlayerId, BonesHand>
        {
            [new(1)] = new(
            [
                new BonesTile(new(1), new(5)),
                new BonesTile(new(1), new(4)),
                new BonesTile(new(0), new(0)),
                new BonesTile(new(0), new(6)),
                new BonesTile(new(3), new(4)),
                new BonesTile(new(3), new(5)),
                new BonesTile(new(3), new(6)),
            ]),
            [new(2)] = new(
            [
                new BonesTile(new(1), new(3)),
                new BonesTile(new(0), new(1)),
                new BonesTile(new(4), new(4)),
                new BonesTile(new(4), new(5)),
                new BonesTile(new(4), new(6)),
                new BonesTile(new(5), new(5)),
                new BonesTile(new(1), new(6)),
            ]),
            [new(3)] = new(
            [
                openingTile,
                new BonesTile(new(1), new(1)),
                new BonesTile(new(0), new(4)),
                new BonesTile(new(0), new(5)),
                new BonesTile(new(2), new(2)),
                new BonesTile(new(2), new(3)),
                new BonesTile(new(2), new(4)),
            ]),
            [new(4)] = new(
            [
                new BonesTile(new(5), new(6)),
                new BonesTile(new(1), new(2)),
                new BonesTile(new(0), new(2)),
                new BonesTile(new(0), new(3)),
                new BonesTile(new(2), new(5)),
                new BonesTile(new(2), new(6)),
                new BonesTile(new(3), new(3)),
            ]),
        };

        return new BonesRoundState(
            new BonesGameId("learning-79da"),
            BonesBoard.Empty,
            new BonesHands(hands),
            new BonesPlayerId(3),
            openingTile,
            passStreak: 0,
            eventLog: []);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.BidirectionalArmsStorage)]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.OpenerOnlyDoubleJunction)]
    public void BonesGameEngine_GivenOpeningDoubleAndLeftThenRightArm_ExpectedOpenerFacingsUnchangedAndArmsMatchOpenerPips()
    {
        var opener = new BonesTile(new(3), new(3));
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [opener],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(2)] = new([new BonesTile(new(3), new(5)), new BonesTile(new(3), new(4))]),
                [new(1)] = new([new BonesTile(new(1), new(2))]),
                [new(3)] = new([new BonesTile(new(2), new(2))]),
                [new(4)] = new([new BonesTile(new(4), new(4))]),
            });

        var openerChainTile = state.Board.Tiles[0];
        Assert.Equal(openerChainTile.ChainLeftPip, openerChainTile.ChainRightPip);
        Assert.Equal(new BonesPipCount(3), openerChainTile.ChainLeftPip);
        Assert.Equal(new BonesPipCount(3), state.Board.LeftEnd!.Value.Pip);
        Assert.Equal(new BonesPipCount(3), state.Board.RightEnd!.Value.Pip);

        var leftArmMove = _engine.GetLegalMoves(state, new BonesPlayerId(2))
            .First(move => move.Tile == new BonesTile(new(3), new(5)) && move.Side == BonesBoardSide.Left);
        state = _engine.ApplyMove(state, leftArmMove);
        state.Board.ValidateChainEdges();

        var openerAfterLeft = state.Board.Tiles[1];
        Assert.Equal(openerChainTile.ChainLeftPip, openerAfterLeft.ChainLeftPip);
        Assert.Equal(openerChainTile.ChainRightPip, openerAfterLeft.ChainRightPip);
        Assert.Equal(new BonesPipCount(5), state.Board.LeftEnd!.Value.Pip);
        Assert.Equal(new BonesPipCount(3), state.Board.RightEnd!.Value.Pip);
        Assert.Equal(new BonesPipCount(3), state.Board.Tiles[0].ChainRightPip);
        Assert.Equal(new BonesPipCount(3), openerAfterLeft.ChainLeftPip);

        var handsForRightArm = state.Hands.HandsByPlayer.SetItem(
            new BonesPlayerId(2),
            new BonesHand([new BonesTile(new(3), new(4))]));
        state = new BonesRoundState(
            state.GameId,
            state.Board,
            new BonesHands(handsForRightArm),
            new BonesPlayerId(2),
            opener,
            passStreak: 0,
            state.EventLog);

        var rightArmMove = _engine.GetLegalMoves(state, new BonesPlayerId(2))
            .Single(move => move.Tile == new BonesTile(new(3), new(4)) && move.Side == BonesBoardSide.Right);
        state = _engine.ApplyMove(state, rightArmMove);
        state.Board.ValidateChainEdges();

        var openerAfterBoth = state.Board.Tiles[1];
        Assert.Equal(openerChainTile.ChainLeftPip, openerAfterBoth.ChainLeftPip);
        Assert.Equal(openerChainTile.ChainRightPip, openerAfterBoth.ChainRightPip);
        Assert.Equal(new BonesPipCount(5), state.Board.LeftEnd!.Value.Pip);
        Assert.Equal(new BonesPipCount(4), state.Board.RightEnd!.Value.Pip);
        Assert.Equal(new BonesPipCount(3), state.Board.Tiles[0].ChainRightPip);
        Assert.Equal(new BonesPipCount(3), state.Board.Tiles[2].ChainLeftPip);
        Assert.Equal(new BonesPipCount(4), state.Board.Tiles[2].ChainRightPip);
    }

    private readonly record struct LearningMatch79daReplayMove(
        BonesPlayerId PlayerId,
        BonesTile? Tile,
        BonesBoardSide? Side,
        bool IsPass);

    private static IEnumerable<LearningMatch79daReplayMove> LearningMatch79daReplayMoves() =>
    [
        new(new(3), new BonesTile(new(6), new(6)), BonesBoardSide.Left, false),
        new(new(4), new BonesTile(new(5), new(6)), BonesBoardSide.Left, false),
        new(new(1), new BonesTile(new(1), new(5)), BonesBoardSide.Left, false),
        new(new(3), new BonesTile(new(1), new(1)), BonesBoardSide.Left, false),
        new(new(2), new BonesTile(new(1), new(3)), BonesBoardSide.Left, false),
        new(new(4), new BonesTile(new(1), new(2)), BonesBoardSide.Left, false),
        new(new(1), new BonesTile(new(1), new(4)), BonesBoardSide.Left, false),
        new(new(2), new BonesTile(new(0), new(1)), BonesBoardSide.Left, false),
        new(new(3), new BonesTile(new(0), new(4)), BonesBoardSide.Left, false),
        new(new(4), new BonesTile(new(0), new(2)), BonesBoardSide.Left, false),
        new(new(1), new BonesTile(new(0), new(0)), BonesBoardSide.Left, false),
        new(new(2), null, null, true),
        new(new(3), new BonesTile(new(0), new(5)), BonesBoardSide.Left, false),
        new(new(4), new BonesTile(new(0), new(3)), BonesBoardSide.Left, false),
        new(new(1), new BonesTile(new(0), new(6)), BonesBoardSide.Left, false),
        new(new(2), null, null, true),
        new(new(3), null, null, true),
        new(new(4), null, null, true),
        new(new(1), null, null, true),
    ];

    private static BonesTile[] LearningMatch79daFinalChainTiles() =>
    [
        new(new BonesPipCount(0), new BonesPipCount(6)),
        new(new BonesPipCount(0), new BonesPipCount(3)),
        new(new BonesPipCount(0), new BonesPipCount(5)),
        new(new BonesPipCount(0), new BonesPipCount(0)),
        new(new BonesPipCount(0), new BonesPipCount(2)),
        new(new BonesPipCount(0), new BonesPipCount(4)),
        new(new BonesPipCount(0), new BonesPipCount(1)),
        new(new BonesPipCount(1), new BonesPipCount(4)),
        new(new BonesPipCount(1), new BonesPipCount(2)),
        new(new BonesPipCount(1), new BonesPipCount(1)),
        new(new BonesPipCount(1), new BonesPipCount(3)),
        new(new BonesPipCount(1), new BonesPipCount(5)),
        new(new BonesPipCount(5), new BonesPipCount(6)),
        new(new BonesPipCount(6), new BonesPipCount(6)),
    ];
}
