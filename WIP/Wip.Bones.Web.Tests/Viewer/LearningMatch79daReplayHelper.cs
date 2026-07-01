using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Tests.Viewer;

internal static class LearningMatch79daReplayHelper
{
    public static (BonesRoundState FinalState, BonesEvent[] PlayEvents) ReplayFinalRound()
    {
        var engine = new BonesGameEngine();
        var state = CreateOpeningState();
        var openingMove = engine.GetLegalMoves(state, new BonesPlayerId(3)).Single();
        state = engine.ApplyMove(state, openingMove);

        var players = PlayerByTile();
        var remaining = FinalChainTiles()
            .Where(tile => tile != state.OpeningTile)
            .ToList();

        if (!TryReplayRemaining(ref state, remaining, players, engine))
            throw new InvalidOperationException("Unable to replay learning match 79da final chain.");

        var playEvents = state.EventLog
            .Where(static e => e.Kind == BonesEventKind.Play)
            .ToArray();

        return (state, playEvents);
    }

    public static BonesMatchResult CreateMatchResult(BonesRoundState finalState, BonesEvent[] playEvents)
    {
        var score = new BonesRoundScore(new BonesPlayerId(1), 0, BonesRoundOutcomeKind.Blocked);
        var round = new BonesMatchRoundRecord(1, score, playEvents.ToImmutableArray());
        var cumulativeScores = ImmutableDictionary.CreateBuilder<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            cumulativeScores[new BonesPlayerId(seat)] = 0;

        return new BonesMatchResult(
            new BonesGameId("learning-79da"),
            new BonesPlayerId(1),
            cumulativeScores.ToImmutable(),
            [round],
            playEvents);
    }

    private static bool TryReplayRemaining(
        ref BonesRoundState state,
        List<BonesTile> remaining,
        IReadOnlyDictionary<BonesTile, BonesPlayerId> players,
        BonesGameEngine engine)
    {
        if (remaining.Count == 0)
            return HasSandwichEdge(state.Board);

        foreach (var tile in remaining.ToArray())
        {
            var playerId = players[tile];
            var prepared = WithCurrentPlayerAndTile(state, playerId, tile);
            var legalMoves = engine.GetLegalMoves(prepared, playerId)
                .Where(move => !move.IsPass && move.Tile == tile)
                .ToArray();

            foreach (var move in legalMoves)
            {
                var nextState = engine.ApplyMove(prepared, move);
                nextState.Board.ValidateChainEdges();

                var nextRemaining = remaining.ToList();
                nextRemaining.Remove(tile);

                if (TryReplayRemaining(ref nextState, nextRemaining, players, engine))
                {
                    state = nextState;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSandwichEdge(BonesBoard board)
    {
        for (var index = 0; index < board.Tiles.Length - 1; index++)
        {
            if (board.Tiles[index].Tile == new BonesTile(new(1), new(3))
                && board.Tiles[index + 1].Tile == new BonesTile(new(1), new(5)))
            {
                return board.Tiles[index].ChainRightPip == board.Tiles[index + 1].ChainLeftPip;
            }
        }

        return false;
    }

    private static Dictionary<BonesTile, BonesPlayerId> PlayerByTile() =>
        ReplayMoves()
            .Where(move => !move.IsPass && move.Tile.HasValue)
            .ToDictionary(move => move.Tile!.Value, move => move.PlayerId);

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

    private static BonesRoundState CreateOpeningState()
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

    private static IEnumerable<ReplayMove> ReplayMoves() =>
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

    private static BonesTile[] FinalChainTiles() =>
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

    private readonly record struct ReplayMove(
        BonesPlayerId PlayerId,
        BonesTile? Tile,
        BonesBoardSide? Side,
        bool IsPass);
}