using System.Text.Json.Serialization;

namespace Wip.Bones.Web.Viewer;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BonesTileOrientation
{
    Vertical = 1,
    Horizontal = 2,
}

public sealed record BonesBoardTilePlacement
{
    public required int FacingLowPip { get; init; }

    public required int FacingHighPip { get; init; }

    public required int ChainIndex { get; init; }

    public required int LowPip { get; init; }

    public required int HighPip { get; init; }

    public required bool IsDouble { get; init; }

    public required BonesTileOrientation Orientation { get; init; }

    public required int GridX { get; init; }

    public required int GridY { get; init; }

    public required int PlayedBySeat { get; init; }

    public BonesPipAxis PipAxis { get; init; } = BonesPipAxis.Horizontal;

    public int FineColumnStart { get; init; }

    public int FineColumnEnd { get; init; }

    public int FineRowStart { get; init; }

    public int FineRowEnd { get; init; }
}

public sealed record BonesHandTileView
{
    public required int LowPip { get; init; }

    public required int HighPip { get; init; }

    public required int OwnerSeat { get; init; }
}

public sealed record BonesBoardVisualLayout
{
    public required IReadOnlyList<BonesBoardTilePlacement> Tiles { get; init; }

    public int MinGridX { get; init; }

    public int MaxGridX { get; init; }

    public int MinGridY { get; init; }

    public int MaxGridY { get; init; }

    public int ColumnCount { get; init; }

    public int RowCount { get; init; }

    public int FineColumnCount { get; init; }

    public int FineRowCount { get; init; }

    public int OccupiedWidthPixels { get; init; }

    public int OccupiedHeightPixels { get; init; }
}

public static class BonesPlayerColorPalette
{
    public static IReadOnlyDictionary<int, string> PlayerColorsBySeat { get; } =
        new Dictionary<int, string>
        {
            [1] = "#e63946",
            [2] = "#457b9d",
            [3] = "#2a9d8f",
            [4] = "#e9c46a",
        };

    public static string GetColorForSeat(int seat)
    {
        if (!PlayerColorsBySeat.TryGetValue(seat, out var color))
            throw new ArgumentOutOfRangeException(nameof(seat), seat, "Seat must be between 1 and 4.");

        return color;
    }
}
