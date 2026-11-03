using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Sandbox.Protocol;

namespace Wip.Bones.Engine;

internal static class BonesWorkerBridge
{
    public static BonesWorkerState ToState(BonesRoundState state)
    {
        var board = state.Board.Tiles
            .Select(tile => new BonesWorkerChainTile(
                tile.Tile.LowPip.Value,
                tile.Tile.HighPip.Value,
                tile.ChainLeftPip.Value,
                tile.ChainRightPip.Value))
            .ToArray();
        var hands = state.Hands.HandsByPlayer
            .Select(pair => new BonesWorkerHand(
                pair.Key.Seat,
                pair.Value.Tiles.Select(tile => new BonesWorkerTile(tile.LowPip.Value, tile.HighPip.Value)).ToArray()))
            .ToArray();
        var events = state.EventLog
            .Select(item => new BonesWorkerEvent(
                item.TurnIndex,
                item.PlayerId.Seat,
                (int)item.Kind,
                item.Tile is { } tile ? new BonesWorkerTile(tile.LowPip.Value, tile.HighPip.Value) : null,
                item.Side is null ? null : (int)item.Side.Value))
            .ToArray();
        return new BonesWorkerState(
            state.GameId.Value,
            board,
            hands,
            state.CurrentPlayer.Seat,
            new BonesWorkerTile(state.OpeningTile.LowPip.Value, state.OpeningTile.HighPip.Value),
            state.PassStreak,
            events,
            state.Outcome is null ? null : (int)state.Outcome.Value,
            state.Winner?.Seat);
    }

    public static BonesWorkerMove ToMoveDto(BonesMove move)
    {
        var tile = move.Tile is { } value ? new BonesWorkerTile(value.LowPip.Value, value.HighPip.Value) : null;
        return new BonesWorkerMove(move.MoveId.Value, move.PlayerId.Seat, tile, move.Side is null ? null : (int)move.Side.Value);
    }

    public static BonesMove ToMove(BonesWorkerMove move) =>
        new(new BonesMoveId(move.MoveId),
            new BonesPlayerId(move.PlayerSeat),
            move.Tile is null ? null : new BonesTile(new BonesPipCount(move.Tile.FirstPip), new BonesPipCount(move.Tile.SecondPip)),
            move.Side is null ? null : (BonesBoardSide)move.Side.Value);

    public static BonesRoundState ToState(BonesWorkerState dto)
    {
        var board = dto.Board.Select(t => new BonesChainTile(
            new BonesTile(new BonesPipCount(t.FirstPip), new BonesPipCount(t.SecondPip)),
            new BonesPipCount(t.ChainLeftPip),
            new BonesPipCount(t.ChainRightPip))).ToImmutableArray();
        var hands = dto.Hands.ToDictionary(
            hand => new BonesPlayerId(hand.Seat),
            hand => new BonesHand(hand.Tiles.Select(tile => new BonesTile(new BonesPipCount(tile.FirstPip), new BonesPipCount(tile.SecondPip))).ToArray()));
        var events = dto.EventLog.Select(item => new BonesEvent(
            item.TurnIndex,
            new BonesPlayerId(item.PlayerSeat),
            (BonesEventKind)item.Kind,
            item.Tile is null ? null : new BonesTile(new BonesPipCount(item.Tile.FirstPip), new BonesPipCount(item.Tile.SecondPip)),
            item.Side is null ? null : (BonesBoardSide)item.Side.Value)).ToArray();
        return new BonesRoundState(
            new BonesGameId(dto.GameId),
            new BonesBoard(board),
            new BonesHands(hands),
            new BonesPlayerId(dto.CurrentSeat),
            new BonesTile(new BonesPipCount(dto.OpeningTile.FirstPip), new BonesPipCount(dto.OpeningTile.SecondPip)),
            dto.PassStreak,
            events,
            dto.Outcome is null ? null : (BonesRoundOutcomeKind)dto.Outcome.Value,
            dto.Winner is null ? null : new BonesPlayerId(dto.Winner.Value));
    }
}
