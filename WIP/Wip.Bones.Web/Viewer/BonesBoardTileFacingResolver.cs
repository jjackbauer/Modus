using System.Text.Json.Serialization;
using Wip.Bones.Domain;

namespace Wip.Bones.Web.Viewer;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BonesPipAxis
{
    Horizontal = 1,
    Vertical = 2,
}

public static class BonesBoardTileFacingResolver
{
    public static (int FacingLowPip, int FacingHighPip) ResolveChainFacing(
        IReadOnlyList<BonesChainTile> chainTiles,
        int chainIndex)
    {
        ArgumentNullException.ThrowIfNull(chainTiles);
        if (chainIndex < 0 || chainIndex >= chainTiles.Count)
            throw new ArgumentOutOfRangeException(nameof(chainIndex));

        var chainTile = chainTiles[chainIndex];
        return (chainTile.ChainLeftPip.Value, chainTile.ChainRightPip.Value);
    }

    public static BonesBoardTilePlacement AssignFacingPips(BonesBoardTilePlacement placement)
    {
        if (placement.Orientation == BonesTileOrientation.Horizontal)
            return placement with { PipAxis = BonesPipAxis.Horizontal };

        if (placement.IsDouble)
            return placement with { PipAxis = BonesPipAxis.Vertical };

        return placement with { PipAxis = BonesPipAxis.Horizontal };
    }

    internal static int? GetSharedPip(BonesTile left, BonesTile right)
    {
        if (left.HighPip == right.LowPip)
            return left.HighPip.Value;

        if (left.LowPip == right.HighPip)
            return left.LowPip.Value;

        if (left.HighPip == right.HighPip)
            return left.HighPip.Value;

        if (left.LowPip == right.LowPip)
            return left.LowPip.Value;

        return null;
    }
}