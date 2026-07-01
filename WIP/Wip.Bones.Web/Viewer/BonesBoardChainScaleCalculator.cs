namespace Wip.Bones.Web.Viewer;

public static class BonesBoardChainScaleCalculator
{
    public const double TileUnitPixels = 40;

    public const double MinReadableTilePixels = 28;

    public const double DefaultContainerWidthPixels = 560;

    public const double DefaultChainSlotWidthPixels = 400;

    public const double DefaultContainerHeightPixels = 160;

    public static double ComputeTransformScale(
        BonesBoardVisualLayout layout,
        double containerWidth,
        double containerHeight)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.Tiles.Count == 0 || containerWidth <= 0 || containerHeight <= 0)
            return 1.0;

        var chainWidth = ResolveOccupiedWidthPixels(layout);
        var chainHeight = ResolveOccupiedHeightPixels(layout);

        if (chainWidth <= containerWidth && chainHeight <= containerHeight)
            return 1.0;

        var scaleX = containerWidth / chainWidth;
        var scaleY = containerHeight / chainHeight;
        var fitScale = Math.Min(scaleX, scaleY);
        var minTransformScale = MinReadableTilePixels / TileUnitPixels;

        return Math.Min(1.0, Math.Max(fitScale, minTransformScale));
    }

    public static double ComputeScale(
        BonesBoardVisualLayout layout,
        double containerWidth,
        double containerHeight) =>
        ComputeTransformScale(layout, containerWidth, containerHeight);

    private static double ResolveOccupiedWidthPixels(BonesBoardVisualLayout layout)
    {
        if (layout.OccupiedWidthPixels > 0)
            return layout.OccupiedWidthPixels;

        if (layout.FineColumnCount > 0)
            return layout.FineColumnCount * TileUnitPixels;

        return layout.ColumnCount * TileUnitPixels * 2;
    }

    private static double ResolveOccupiedHeightPixels(BonesBoardVisualLayout layout)
    {
        if (layout.OccupiedHeightPixels > 0)
            return layout.OccupiedHeightPixels;

        if (layout.FineRowCount > 0)
            return layout.FineRowCount * TileUnitPixels;

        return Math.Max(layout.RowCount, 1) * TileUnitPixels * 2;
    }
}