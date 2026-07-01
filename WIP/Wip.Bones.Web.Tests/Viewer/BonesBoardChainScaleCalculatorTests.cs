using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardChainScaleCalculatorTests
{
    private const string ChecklistItem =
        BonesViewerReplayScalingRequirementsChecklistItems.BoardChainScaleCalculator;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainScaleCalculator_GivenNarrowContainer_ExpectedScaleBelowOneForLongChain()
    {
        var layout = new BonesBoardVisualLayout
        {
            Tiles = [CreatePlacement(0), CreatePlacement(1), CreatePlacement(2), CreatePlacement(3), CreatePlacement(4)],
            MinGridX = 0,
            MaxGridX = 4,
            MinGridY = 0,
            MaxGridY = 0,
            ColumnCount = 5,
            RowCount = 1,
        };

        var scale = BonesBoardChainScaleCalculator.ComputeScale(layout, containerWidth: 200, containerHeight: 160);

        Assert.True(scale < 1.0);
        Assert.True(scale > 0);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainScaleCalculator_GivenShortChain_ExpectedScaleOne()
    {
        var layout = new BonesBoardVisualLayout
        {
            Tiles = [CreatePlacement(0), CreatePlacement(1)],
            MinGridX = 0,
            MaxGridX = 1,
            MinGridY = 0,
            MaxGridY = 0,
            ColumnCount = 2,
            RowCount = 1,
        };

        var scale = BonesBoardChainScaleCalculator.ComputeScale(
            layout,
            BonesBoardChainScaleCalculator.DefaultContainerWidthPixels,
            BonesBoardChainScaleCalculator.DefaultContainerHeightPixels);

        Assert.Equal(1.0, scale);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainScaleCalculator_GivenContainerResize_ExpectedScaleIncreasesWhenContainerWidens()
    {
        var layout = new BonesBoardVisualLayout
        {
            Tiles = [CreatePlacement(0), CreatePlacement(1), CreatePlacement(2), CreatePlacement(3)],
            MinGridX = 0,
            MaxGridX = 3,
            MinGridY = 0,
            MaxGridY = 0,
            ColumnCount = 4,
            RowCount = 1,
        };

        var narrowScale = BonesBoardChainScaleCalculator.ComputeScale(layout, containerWidth: 240, containerHeight: 160);
        var wideScale = BonesBoardChainScaleCalculator.ComputeScale(layout, containerWidth: 480, containerHeight: 160);

        Assert.True(wideScale > narrowScale);
    }

    private static BonesBoardTilePlacement CreatePlacement(int gridX) => new()
    {
        FacingLowPip = 1,
        FacingHighPip = 2,
        ChainIndex = gridX,
        LowPip = 1,
        HighPip = 2,
        IsDouble = false,
        Orientation = BonesTileOrientation.Horizontal,
        GridX = gridX,
        GridY = 0,
        PlayedBySeat = 1,
    };
}
