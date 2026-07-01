using System.Collections.Immutable;

namespace Wip.Bones.Domain;

public static class BonesBoardChainBuilder
{
    public static BonesChainTile OpeningChainTile(BonesTile tile) =>
        tile.IsDouble
            ? new BonesChainTile(tile, tile.LowPip, tile.LowPip)
            : new BonesChainTile(tile, tile.LowPip, tile.HighPip);

    public static BonesBoard FromLegacyCanonicalChain(IReadOnlyList<BonesTile> tiles)
    {
        if (tiles.Count == 0)
            return BonesBoard.Empty;

        if (tiles.Count == 1)
            return new BonesBoard([OpeningChainTile(tiles[0])]);

        var chainTiles = new List<BonesChainTile>(tiles.Count);
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            BonesPipCount chainLeftPip;
            BonesPipCount chainRightPip;

            if (index == 0)
            {
                var sharedWithNext = FindSharedPip(tile, tiles[index + 1]);
                chainLeftPip = OtherPip(tile, sharedWithNext);
                chainRightPip = sharedWithNext;
            }
            else if (index == tiles.Count - 1)
            {
                chainLeftPip = chainTiles[^1].ChainRightPip;
                chainRightPip = OtherPip(tile, chainLeftPip);
            }
            else
            {
                chainLeftPip = chainTiles[^1].ChainRightPip;
                var sharedWithNext = FindSharedPip(tile, tiles[index + 1]);
                chainRightPip = sharedWithNext == chainLeftPip && !tile.IsDouble
                    ? OtherPip(tile, chainLeftPip)
                    : sharedWithNext;
            }

            chainTiles.Add(new BonesChainTile(tile, chainLeftPip, chainRightPip));
        }

        return new BonesBoard(chainTiles.ToImmutableArray());
    }

    private static BonesPipCount FindSharedPip(BonesTile left, BonesTile right)
    {
        if (left.LowPip == right.LowPip || left.LowPip == right.HighPip)
            return left.LowPip;

        if (left.HighPip == right.LowPip || left.HighPip == right.HighPip)
            return left.HighPip;

        throw new ArgumentException("Tiles do not share a pip.");
    }

    private static BonesPipCount OtherPip(BonesTile tile, BonesPipCount pip) =>
        tile.LowPip == pip ? tile.HighPip : tile.LowPip;
}