using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardChainSlotScaleMeasurementTests
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.ChainSlotScaleMeasurement;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainScaleCalculator_GivenLongChain_ExpectedChainSlotWidthYieldsLowerScaleThanFullBoard()
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
            FineColumnCount = 9,
            FineRowCount = 2,
            OccupiedWidthPixels = 500,
            OccupiedHeightPixels = 80,
        };

        var chainSlotScale = BonesBoardChainScaleCalculator.ComputeTransformScale(
            layout,
            BonesBoardChainScaleCalculator.DefaultChainSlotWidthPixels,
            BonesBoardChainScaleCalculator.DefaultContainerHeightPixels);
        var boardScale = BonesBoardChainScaleCalculator.ComputeTransformScale(
            layout,
            BonesBoardChainScaleCalculator.DefaultContainerWidthPixels,
            BonesBoardChainScaleCalculator.DefaultContainerHeightPixels);

        Assert.True(chainSlotScale < boardScale);
        Assert.True(chainSlotScale < 1.0);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerScript_GivenApplyBoardChainLayout_ExpectedMeasuresBoardChainSlot()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("boardChain.clientWidth", script, StringComparison.Ordinal);
        Assert.Contains("boardChain.clientHeight", script, StringComparison.Ordinal);
        Assert.Contains("DEFAULT_CHAIN_SLOT_WIDTH_PIXELS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("boardSection.clientWidth", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesViewerPageRenderer_GivenLongChain_ExpectedUsesDefaultChainSlotWidthForSsrScale()
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
            FineColumnCount = 9,
            FineRowCount = 2,
            OccupiedWidthPixels = 500,
            OccupiedHeightPixels = 80,
        };

        var expectedScale = BonesBoardChainScaleCalculator.ComputeTransformScale(
            layout,
            BonesBoardChainScaleCalculator.DefaultChainSlotWidthPixels,
            BonesBoardChainScaleCalculator.DefaultContainerHeightPixels);

        var html = BonesViewerPageRenderer.Render(CreateSnapshot(layout), null, null);
        Assert.Contains(
            $"data-board-chain-scale=\"{expectedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"",
            html,
            StringComparison.Ordinal);
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
        FineColumnStart = gridX * 2 + 1,
        FineColumnEnd = gridX * 2 + 3,
        FineRowStart = 1,
        FineRowEnd = 2,
    };

    private static BonesMatchViewModel CreateSnapshot(BonesBoardVisualLayout layout) => new()
    {
        SessionId = "session-chain-slot",
        MatchId = "match-chain-slot",
        FrameCount = 5,
        Revision = 1,
        LeftEndPip = 1,
        RightEndPip = 2,
        ActiveSeat = 1,
        HandTileCountsBySeat = new Dictionary<int, int> { [1] = 5, [2] = 5, [3] = 5, [4] = 5 },
        HandTilesBySeat = new Dictionary<int, IReadOnlyList<BonesHandTileView>>(),
        BoardLayout = layout,
        PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
        CumulativeScoresBySeat = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0 },
        IsComplete = false,
    };
}