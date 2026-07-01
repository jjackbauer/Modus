using Wip.Bones.Domain;

namespace Wip.Bones.Web.Viewer;

public sealed record BonesMatchViewModel
{
    public required string SessionId { get; init; }

    public required string MatchId { get; init; }

    public int? LeftEndPip { get; init; }

    public int? RightEndPip { get; init; }

    public required IReadOnlyDictionary<int, int> HandTileCountsBySeat { get; init; }

    public required BonesBoardVisualLayout BoardLayout { get; init; }

    public required IReadOnlyDictionary<int, IReadOnlyList<BonesHandTileView>> HandTilesBySeat { get; init; }

    public required IReadOnlyDictionary<int, string> PlayerColorsBySeat { get; init; }

    public required int ActiveSeat { get; init; }

    public required IReadOnlyDictionary<int, int> CumulativeScoresBySeat { get; init; }

    public required int FrameCount { get; init; }

    public required long Revision { get; init; }

    public int? WinnerSeat { get; init; }

    public int? RoundPipScore { get; init; }

    public bool IsComplete { get; init; }
}

public sealed record BonesMatchFrame
{
    public required int TurnIndex { get; init; }

    public required long Revision { get; init; }

    public required int ActiveSeat { get; init; }

    public int? LeftEndPip { get; init; }

    public int? RightEndPip { get; init; }

    public required IReadOnlyDictionary<int, int> HandTileCountsBySeat { get; init; }

    public required BonesBoardVisualLayout BoardLayout { get; init; }

    public required IReadOnlyDictionary<int, IReadOnlyList<BonesHandTileView>> HandTilesBySeat { get; init; }

    public required IReadOnlyDictionary<int, string> PlayerColorsBySeat { get; init; }

    public required BonesEventKind EventKind { get; init; }

    public int? PlayedTileLowPip { get; init; }

    public int? PlayedTileHighPip { get; init; }

    public BonesBoardSide? BoardSide { get; init; }

    public bool IsRoundComplete { get; init; }

    public int? WinnerSeat { get; init; }

    public int? RoundPipScore { get; init; }
}
