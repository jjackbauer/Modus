namespace Wip.Sandbox.Protocol;

public sealed record BonesWorkerTile(int FirstPip, int SecondPip);

public sealed record BonesWorkerChainTile(int FirstPip, int SecondPip, int ChainLeftPip, int ChainRightPip);

public sealed record BonesWorkerHand(int Seat, IReadOnlyList<BonesWorkerTile> Tiles);

public sealed record BonesWorkerState(
    string GameId,
    IReadOnlyList<BonesWorkerChainTile> Board,
    IReadOnlyList<BonesWorkerHand> Hands,
    int CurrentSeat,
    BonesWorkerTile OpeningTile,
    int PassStreak);

public sealed record BonesWorkerMove(string MoveId, int PlayerSeat, BonesWorkerTile? Tile, int? Side);
