using Wip.Bones.Domain;

namespace Wip.Bones.Web.Viewer;

public sealed class BonesBoardChainGeometryBuilder
{
    private const int MainLineUpperRowStart = 1;
    private const int MainLineLowerRowStart = 2;
    private const int MainLineVerticalRowStart = 1;
    private const int MainLineVerticalRowEnd = 3;
    private const int BranchRowStart = 3;
    private const int BranchRowEnd = 6;
    private const int BranchColumnSpan = 1;

    public BonesBoardVisualLayout AssignFineGridCells(BonesBoardVisualLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.Tiles.Count == 0)
            return layout;

        var mainLineTiles = layout.Tiles
            .Where(static tile => tile.GridY == 0)
            .OrderBy(static tile => tile.GridX)
            .ToArray();
        var mainLineByGridX = mainLineTiles.ToDictionary(static tile => tile.GridX);
        var branchTilesByGridX = layout.Tiles
            .Where(static tile => tile.GridY != 0)
            .GroupBy(static tile => tile.GridX)
            .ToDictionary(static group => group.Key, static group => group.ToArray());

        var fineByChainIndex = new Dictionary<int, FineCell>(layout.Tiles.Count);
        var columnCursor = 1;
        var mainLineRowStartByGridX = new Dictionary<int, int>(mainLineTiles.Length);
        foreach (var gridX in layout.Tiles.Select(static tile => tile.GridX).Distinct().OrderBy(static gridX => gridX))
        {
            if (branchTilesByGridX.TryGetValue(gridX, out var branchTiles))
            {
                foreach (var branchTile in branchTiles)
                {
                    fineByChainIndex[branchTile.ChainIndex] = new FineCell(
                        columnCursor,
                        columnCursor + BranchColumnSpan,
                        BranchRowStart,
                        BranchRowEnd);
                }

                columnCursor += BranchColumnSpan;
            }

            if (!mainLineByGridX.TryGetValue(gridX, out var mainLineTile))
                continue;

            FineCell fineCell;
            if (mainLineTile.Orientation == BonesTileOrientation.Vertical)
            {
                var columnSpan = ResolveVerticalMainLineColumnSpan(mainLineTile);
                fineCell = new FineCell(
                    columnCursor,
                    columnCursor + columnSpan,
                    MainLineVerticalRowStart,
                    MainLineVerticalRowEnd);
                columnCursor += columnSpan;
            }
            else
            {
                var rowStart = ResolveHorizontalMainLineRowStart(mainLineTile, mainLineTiles, mainLineRowStartByGridX);
                fineCell = new FineCell(
                    columnCursor,
                    columnCursor + 2,
                    rowStart,
                    rowStart + 1);
                columnCursor += 2;
            }

            mainLineRowStartByGridX[gridX] = fineCell.RowStart;
            fineByChainIndex[mainLineTile.ChainIndex] = fineCell;
        }

        var enrichedTiles = layout.Tiles
            .Select(tile => ApplyFineCell(tile, fineByChainIndex[tile.ChainIndex]))
            .ToArray();

        ValidateSpatialConnectivity(enrichedTiles);
        var normalized = NormalizeFineGridCoordinates(enrichedTiles);

        return layout with
        {
            Tiles = normalized.Tiles,
            FineColumnCount = normalized.Extent.FineColumnCount,
            FineRowCount = normalized.Extent.FineRowCount,
            OccupiedWidthPixels = normalized.Extent.OccupiedWidthPixels,
            OccupiedHeightPixels = normalized.Extent.OccupiedHeightPixels,
        };
    }

    public static void ValidateSpatialConnectivity(IReadOnlyList<BonesBoardTilePlacement> tiles)
    {
        var chainTiles = tiles
            .OrderBy(static tile => tile.ChainIndex)
            .ToArray();

        for (var index = 0; index < chainTiles.Length - 1; index++)
        {
            var left = chainTiles[index];
            var right = chainTiles[index + 1];
            if (!TilesShareConnectionEdge(left, right))
            {
                throw new InvalidOperationException(
                    $"Spatial connectivity invariant violated between chain index {left.ChainIndex} and {right.ChainIndex}: " +
                    "touching faces do not share a fine-grid edge.");
            }

            if (GetSpatialConnectingPip(left, right) is null)
            {
                throw new InvalidOperationException(
                    $"Spatial connectivity invariant violated between chain index {left.ChainIndex} and {right.ChainIndex}: " +
                    "touching faces do not expose matching pips.");
            }
        }
    }

    public static int? GetSpatialConnectingPip(BonesBoardTilePlacement left, BonesBoardTilePlacement right)
    {
        if (!TilesShareConnectionEdge(left, right))
            return null;

        var leftExitPip = ResolveExitFacingPip(left, right);
        var rightEntryPip = ResolveEntryFacingPip(left, right);
        if (leftExitPip == rightEntryPip)
            return leftExitPip;

        var leftTile = new BonesTile(new BonesPipCount(left.LowPip), new BonesPipCount(left.HighPip));
        var rightTile = new BonesTile(new BonesPipCount(right.LowPip), new BonesPipCount(right.HighPip));
        return BonesBoardTileFacingResolver.GetSharedPip(leftTile, rightTile);
    }

    internal static int ResolveExitFacingPip(
        BonesBoardTilePlacement tile,
        BonesBoardTilePlacement neighborToTheRight)
    {
        if (neighborToTheRight.FineColumnStart <= tile.FineColumnStart)
            throw new ArgumentException("Neighbor must be to the right of the tile.", nameof(neighborToTheRight));

        if (tile.Orientation == BonesTileOrientation.Horizontal)
            return tile.FacingHighPip;

        if (tile.PipAxis == BonesPipAxis.Horizontal)
            return tile.FacingHighPip;

        return neighborToTheRight.FineRowStart >= tile.FineRowEnd - 1
            ? tile.FacingHighPip
            : tile.FacingLowPip;
    }

    internal static int ResolveEntryFacingPip(
        BonesBoardTilePlacement tileToTheLeft,
        BonesBoardTilePlacement tile)
    {
        if (tile.FineColumnStart <= tileToTheLeft.FineColumnStart)
            throw new ArgumentException("Left tile must be to the left of the entry tile.", nameof(tileToTheLeft));

        if (tile.Orientation == BonesTileOrientation.Horizontal)
            return tile.FacingLowPip;

        if (tile.PipAxis == BonesPipAxis.Horizontal)
            return tile.FacingLowPip;

        return tileToTheLeft.FineRowStart >= tile.FineRowEnd - 1
            ? tile.FacingLowPip
            : tile.FacingHighPip;
    }

    private static bool TilesShareConnectionEdge(
        BonesBoardTilePlacement left,
        BonesBoardTilePlacement right)
    {
        if (TilesShareFineGridEdge(left, right))
            return true;

        if (left.FineColumnEnd != right.FineColumnStart)
            return false;

        return left.GridY != right.GridY && Math.Abs(left.GridX - right.GridX) == 1;
    }

    private static bool TilesShareFineGridEdge(
        BonesBoardTilePlacement left,
        BonesBoardTilePlacement right)
    {
        var columnsTouch = left.FineColumnEnd == right.FineColumnStart;
        var rowsOverlap = left.FineRowStart < right.FineRowEnd && right.FineRowStart < left.FineRowEnd;
        return columnsTouch && rowsOverlap;
    }

    private static int ResolveVerticalMainLineColumnSpan(BonesBoardTilePlacement tile) =>
        tile.PipAxis == BonesPipAxis.Horizontal ? 2 : 1;

    private static int ResolveHorizontalMainLineRowStart(
        BonesBoardTilePlacement tile,
        IReadOnlyList<BonesBoardTilePlacement> mainLineTiles,
        IReadOnlyDictionary<int, int> mainLineRowStartByGridX)
    {
        var leftNeighbor = mainLineTiles.LastOrDefault(neighbor => neighbor.GridX < tile.GridX);
        if (leftNeighbor is null)
            return MainLineUpperRowStart;

        if (leftNeighbor.Orientation == BonesTileOrientation.Vertical)
            return MainLineLowerRowStart;

        return mainLineRowStartByGridX.TryGetValue(leftNeighbor.GridX, out var inheritedRowStart)
            ? inheritedRowStart
            : MainLineUpperRowStart;
    }

    private static BonesBoardTilePlacement ApplyFineCell(BonesBoardTilePlacement tile, FineCell fineCell) =>
        tile with
        {
            FineColumnStart = fineCell.ColumnStart,
            FineColumnEnd = fineCell.ColumnEnd,
            FineRowStart = fineCell.RowStart,
            FineRowEnd = fineCell.RowEnd,
        };

    private static NormalizedFineGrid NormalizeFineGridCoordinates(IReadOnlyList<BonesBoardTilePlacement> tiles)
    {
        if (tiles.Count == 0)
            return new NormalizedFineGrid(tiles, default);

        var minColumnStart = tiles.Min(static tile => tile.FineColumnStart);
        var minRowStart = tiles.Min(static tile => tile.FineRowStart);
        var columnOffset = minColumnStart - 1;
        var rowOffset = minRowStart - 1;

        if (columnOffset == 0 && rowOffset == 0)
            return new NormalizedFineGrid(tiles, ComputeFineGridExtent(tiles));

        var normalizedTiles = tiles
            .Select(tile => tile with
            {
                FineColumnStart = tile.FineColumnStart - columnOffset,
                FineColumnEnd = tile.FineColumnEnd - columnOffset,
                FineRowStart = tile.FineRowStart - rowOffset,
                FineRowEnd = tile.FineRowEnd - rowOffset,
            })
            .ToArray();

        return new NormalizedFineGrid(normalizedTiles, ComputeFineGridExtent(normalizedTiles));
    }

    private static FineGridExtent ComputeFineGridExtent(IReadOnlyList<BonesBoardTilePlacement> tiles)
    {
        if (tiles.Count == 0)
            return default;

        var minColumnStart = tiles.Min(static tile => tile.FineColumnStart);
        var maxColumnEnd = tiles.Max(static tile => tile.FineColumnEnd);
        var minRowStart = tiles.Min(static tile => tile.FineRowStart);
        var maxRowEnd = tiles.Max(static tile => tile.FineRowEnd);

        var fineColumnCount = maxColumnEnd - minColumnStart;
        var fineRowCount = maxRowEnd - minRowStart;
        var tileUnitPixels = (int)BonesBoardChainScaleCalculator.TileUnitPixels;

        return new FineGridExtent(
            fineColumnCount,
            fineRowCount,
            fineColumnCount * tileUnitPixels,
            fineRowCount * tileUnitPixels);
    }

    private readonly record struct FineCell(int ColumnStart, int ColumnEnd, int RowStart, int RowEnd);

    private readonly record struct FineGridExtent(
        int FineColumnCount,
        int FineRowCount,
        int OccupiedWidthPixels,
        int OccupiedHeightPixels);

    private readonly record struct NormalizedFineGrid(
        IReadOnlyList<BonesBoardTilePlacement> Tiles,
        FineGridExtent Extent);
}