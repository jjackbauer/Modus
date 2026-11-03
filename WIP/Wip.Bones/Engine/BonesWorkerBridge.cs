using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Sandbox.Protocol;
using Wip.Sandbox.WorkerClient;

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
        return new BonesWorkerState(
            state.GameId.Value,
            board,
            hands,
            state.CurrentPlayer.Seat,
            new BonesWorkerTile(state.OpeningTile.LowPip.Value, state.OpeningTile.HighPip.Value),
            state.PassStreak);
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
}

internal sealed class WorkerBonesPlayerSlot : IBonesPlayerSlot
{
    private readonly SandboxWorkerClient _client;
    private readonly string _strategyId;

    public WorkerBonesPlayerSlot(SandboxWorkerClient client, string strategyId)
    {
        _client = client;
        _strategyId = strategyId;
    }

    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var move = _client.ChooseBonesMoveAsync(
            _strategyId,
            BonesWorkerBridge.ToState(state),
            legalMoves.Select(BonesWorkerBridge.ToMoveDto).ToArray()).GetAwaiter().GetResult();
        return BonesWorkerBridge.ToMove(move);
    }
}
