using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public sealed class BonesGameEngine
{
    public BonesRoundState StartRound(BonesRoundConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var tiles = CreateDoubleSixSet();
        Shuffle(tiles, config.ShuffleSeed);

        var hands = Deal(tiles);
        var opening = ResolveOpeningLead(hands);
        var handsByPlayer = hands.ToImmutableDictionary(static entry => entry.Key, static entry => entry.Value);

        return new BonesRoundState(
            config.GameId,
            BonesBoard.Empty,
            new BonesHands(handsByPlayer),
            opening.PlayerId,
            opening.Tile,
            passStreak: 0,
            eventLog: []);
    }

    public IReadOnlyList<BonesMove> GetLegalMoves(BonesRoundState state, BonesPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Outcome is not null)
            return [];

        if (state.CurrentPlayer != playerId)
            return [];

        var hand = state.Hands.GetHand(playerId);
        var moves = new List<BonesMove>();

        if (state.Board.IsEmpty)
        {
            if (hand.Tiles.Contains(state.OpeningTile))
            {
                moves.Add(BonesMove.Play(
                    CreateMoveId(state, playerId, state.OpeningTile, BonesBoardSide.Left),
                    playerId,
                    state.OpeningTile,
                    BonesBoardSide.Left));
            }

            return moves;
        }

        var leftEnd = state.Board.LeftEnd!.Value.Pip;
        var rightEnd = state.Board.RightEnd!.Value.Pip;

        foreach (var tile in hand.Tiles)
        {
            if (TileMatchesEnd(tile, leftEnd))
            {
                moves.Add(BonesMove.Play(
                    CreateMoveId(state, playerId, tile, BonesBoardSide.Left),
                    playerId,
                    tile,
                    BonesBoardSide.Left));
            }

            if (TileMatchesEnd(tile, rightEnd))
            {
                moves.Add(BonesMove.Play(
                    CreateMoveId(state, playerId, tile, BonesBoardSide.Right),
                    playerId,
                    tile,
                    BonesBoardSide.Right));
            }
        }

        if (moves.Count == 0)
            moves.Add(BonesMove.Pass(CreatePassMoveId(state, playerId), playerId));

        return moves;
    }

    public BonesRoundState ApplyMove(BonesRoundState state, BonesMove move)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(move);

        if (state.Outcome is not null)
            throw new BonesMoveRejectedException("Round is already complete.");

        if (state.CurrentPlayer != move.PlayerId)
            throw new BonesMoveRejectedException("Player is not active.");

        var legalMoves = GetLegalMoves(state, move.PlayerId);
        if (!legalMoves.Any(legal => MovesEquivalent(legal, move)))
            throw new BonesMoveRejectedException("Move is not legal.");

        var turnIndex = state.EventLog.Length;
        if (move.IsPass)
            return ApplyPass(state, move, turnIndex);

        return ApplyPlay(state, move, turnIndex);
    }

    public bool IsRoundComplete(BonesRoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Outcome is not null;
    }

    public BonesRoundScore ScoreRound(BonesRoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Outcome is null || state.Winner is null)
            throw new InvalidOperationException("Round is not complete.");

        var winner = state.Winner.Value;
        var points = 0;

        foreach (var player in AllPlayers())
        {
            if (player == winner)
                continue;

            points += state.Hands.GetHand(player).TotalPips;
        }

        return new BonesRoundScore(winner, points, state.Outcome.Value);
    }

    private static BonesRoundState ApplyPass(BonesRoundState state, BonesMove move, int turnIndex)
    {
        var passStreak = state.PassStreak + 1;
        var eventLog = state.EventLog.Add(new BonesEvent(turnIndex, move.PlayerId, BonesEventKind.Pass, null, null));

        if (passStreak >= BonesTableConfig.FixedPlayerCount)
        {
            var winner = ResolveBlockedWinner(state.Hands);
            return new BonesRoundState(
                state.GameId,
                state.Board,
                state.Hands,
                state.CurrentPlayer,
                state.OpeningTile,
                passStreak,
                eventLog,
                BonesRoundOutcomeKind.Blocked,
                winner);
        }

        return new BonesRoundState(
            state.GameId,
            state.Board,
            state.Hands,
            NextPlayer(move.PlayerId),
            state.OpeningTile,
            passStreak,
            eventLog);
    }

    private static BonesRoundState ApplyPlay(BonesRoundState state, BonesMove move, int turnIndex)
    {
        var tile = move.Tile!.Value;
        var side = move.Side!.Value;
        var hand = state.Hands.GetHand(move.PlayerId);
        var updatedTiles = hand.Tiles.Remove(tile);
        var updatedHands = state.Hands.HandsByPlayer.SetItem(move.PlayerId, new BonesHand(updatedTiles));

        var updatedBoard = state.Board.IsEmpty
            ? new BonesBoard([BonesBoardChainBuilder.OpeningChainTile(tile)])
            : side == BonesBoardSide.Left
                ? PrependTile(state.Board, tile)
                : AppendTile(state.Board, tile);

        var eventLog = state.EventLog.Add(new BonesEvent(turnIndex, move.PlayerId, BonesEventKind.Play, tile, side));

        if (updatedTiles.IsEmpty)
        {
            return new BonesRoundState(
                state.GameId,
                updatedBoard,
                new BonesHands(updatedHands),
                move.PlayerId,
                state.OpeningTile,
                passStreak: 0,
                eventLog,
                BonesRoundOutcomeKind.Dominoes,
                move.PlayerId);
        }

        return new BonesRoundState(
            state.GameId,
            updatedBoard,
            new BonesHands(updatedHands),
            NextPlayer(move.PlayerId),
            state.OpeningTile,
            passStreak: 0,
            eventLog);
    }

    private static BonesBoard PrependTile(BonesBoard board, BonesTile tile)
    {
        var leftEnd = board.LeftEnd!.Value.Pip;
        var chainTile = CreatePrependedChainTile(tile, leftEnd);
        return new BonesBoard([chainTile, ..board.Tiles]);
    }

    private static BonesBoard AppendTile(BonesBoard board, BonesTile tile)
    {
        var rightEnd = board.RightEnd!.Value.Pip;
        var chainTile = CreateAppendedChainTile(tile, rightEnd);
        return new BonesBoard([..board.Tiles, chainTile]);
    }

    private static BonesChainTile CreatePrependedChainTile(BonesTile tile, BonesPipCount openEnd)
    {
        if (tile.LowPip != openEnd && tile.HighPip != openEnd)
            throw new BonesMoveRejectedException("Tile does not match the open end.");

        var chainRightPip = openEnd;
        var chainLeftPip = tile.LowPip == openEnd ? tile.HighPip : tile.LowPip;
        return new BonesChainTile(tile, chainLeftPip, chainRightPip);
    }

    private static BonesChainTile CreateAppendedChainTile(BonesTile tile, BonesPipCount openEnd)
    {
        if (tile.LowPip != openEnd && tile.HighPip != openEnd)
            throw new BonesMoveRejectedException("Tile does not match the open end.");

        var chainLeftPip = openEnd;
        var chainRightPip = tile.LowPip == openEnd ? tile.HighPip : tile.LowPip;
        return new BonesChainTile(tile, chainLeftPip, chainRightPip);
    }

    private static bool TileMatchesEnd(BonesTile tile, BonesPipCount end) =>
        tile.LowPip == end || tile.HighPip == end;

    private static bool MovesEquivalent(BonesMove left, BonesMove right) =>
        left.IsPass && right.IsPass
            ? left.PlayerId == right.PlayerId
            : !left.IsPass
                && !right.IsPass
                && left.PlayerId == right.PlayerId
                && left.Tile == right.Tile
                && left.Side == right.Side;

    private static BonesPlayerId NextPlayer(BonesPlayerId current) =>
        current.Seat == BonesPlayerId.MaxSeat
            ? new BonesPlayerId(BonesPlayerId.MinSeat)
            : new BonesPlayerId(current.Seat + 1);

    private static IEnumerable<BonesPlayerId> AllPlayers()
    {
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            yield return new BonesPlayerId(seat);
    }

    private static (BonesPlayerId PlayerId, BonesTile Tile) ResolveOpeningLead(IReadOnlyDictionary<BonesPlayerId, BonesHand> hands)
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

    private static List<BonesTile> CreateDoubleSixSet()
    {
        var tiles = new List<BonesTile>(28);
        for (var low = 0; low <= BonesPipCount.MaxValue; low++)
        {
            for (var high = low; high <= BonesPipCount.MaxValue; high++)
                tiles.Add(new BonesTile(new BonesPipCount(low), new BonesPipCount(high)));
        }

        return tiles;
    }

    private static void Shuffle(List<BonesTile> tiles, int seed)
    {
        var random = new Random(seed);
        for (var index = tiles.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (tiles[index], tiles[swapIndex]) = (tiles[swapIndex], tiles[index]);
        }
    }

    private static Dictionary<BonesPlayerId, BonesHand> Deal(IReadOnlyList<BonesTile> tiles)
    {
        var hands = new Dictionary<BonesPlayerId, List<BonesTile>>();
        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
            hands[new BonesPlayerId(seat)] = [];

        var playerIds = hands.Keys.OrderBy(static id => id.Seat).ToArray();
        var tileIndex = 0;
        for (var round = 0; round < 7; round++)
        {
            foreach (var playerId in playerIds)
            {
                hands[playerId].Add(tiles[tileIndex]);
                tileIndex++;
            }
        }

        if (tileIndex != tiles.Count)
            throw new InvalidOperationException("Deal did not distribute every tile.");

        return hands.ToDictionary(static entry => entry.Key, static entry => new BonesHand(entry.Value));
    }

    private static BonesPlayerId ResolveBlockedWinner(BonesHands hands)
    {
        var best = (TotalPips: int.MaxValue, Seat: BonesPlayerId.MinSeat);
        foreach (var player in AllPlayers())
        {
            var total = hands.GetHand(player).TotalPips;
            if (total < best.TotalPips || (total == best.TotalPips && player.Seat < best.Seat))
                best = (total, player.Seat);
        }

        return new BonesPlayerId(best.Seat);
    }

    private static BonesMoveId CreateMoveId(BonesRoundState state, BonesPlayerId playerId, BonesTile tile, BonesBoardSide side) =>
        new($"{state.GameId.Value}-t{state.EventLog.Length}-p{playerId.Seat}-{tile.LowPip}-{tile.HighPip}-{side}");

    private static BonesMoveId CreatePassMoveId(BonesRoundState state, BonesPlayerId playerId) =>
        new($"{state.GameId.Value}-t{state.EventLog.Length}-p{playerId.Seat}-pass");
}

public sealed class BonesMoveRejectedException : Exception
{
    public BonesMoveRejectedException(string message)
        : base(message)
    {
    }
}
