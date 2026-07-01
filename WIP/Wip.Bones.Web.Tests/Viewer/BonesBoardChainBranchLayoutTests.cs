using System.Collections.Immutable;
using AngleSharp;
using AngleSharp.Dom;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardChainBranchLayoutTests
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.BranchDoubleLayout;

    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenDoubleInChain_ExpectedNonOverlappingMainLineFineCells()
    {
        var layout = BuildDoubleBranchLayout();
        var tiles = layout.Tiles.ToArray();

        Assert.All(tiles, tile => Assert.Equal(0, tile.GridY));

        for (var leftIndex = 0; leftIndex < tiles.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < tiles.Length; rightIndex++)
            {
                var left = tiles[leftIndex];
                var right = tiles[rightIndex];
                Assert.False(
                    FineCellsOverlap(
                        left.FineColumnStart,
                        left.FineColumnEnd,
                        left.FineRowStart,
                        left.FineRowEnd,
                        right.FineColumnStart,
                        right.FineColumnEnd,
                        right.FineRowStart,
                        right.FineRowEnd),
                    $"Tiles at grid ({left.GridX},{left.GridY}) and ({right.GridX},{right.GridY}) overlap.");
            }
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenDoubleInChain_ExpectedNonOverlappingGridPlacementStyles()
    {
        var layout = BuildDoubleBranchLayout();
        var snapshot = CreateSnapshot(layout);
        var html = BonesViewerPageRenderer.Render(snapshot, null, null);
        var document = await ParseHtmlAsync(html);

        var tiles = document.GetElementById("board-chain")!
            .QuerySelectorAll(".board-chain-tile")
            .Select(ParseTilePlacement)
            .ToArray();

        Assert.All(tiles, tile => Assert.Equal(0, tile.GridY));

        for (var leftIndex = 0; leftIndex < tiles.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < tiles.Length; rightIndex++)
            {
                var left = tiles[leftIndex];
                var right = tiles[rightIndex];
                Assert.False(
                    FineCellsOverlap(
                        left.ColumnStart,
                        left.ColumnEnd,
                        left.RowStart,
                        left.RowEnd,
                        right.ColumnStart,
                        right.ColumnEnd,
                        right.RowStart,
                        right.RowEnd),
                    $"Rendered placements overlap at grid ({left.GridX},{left.GridY}) and ({right.GridX},{right.GridY}).");
            }
        }
    }


    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenSandwichedDoubleBranch_ExpectedJunctionPipsMatchOnBothHalvesAndNeighbors()
    {
        var layout = BuildSandwichedDoubleBranchLayout();
        var chainDouble = layout.Tiles.Single(tile => tile.IsDouble);
        var leftMain = layout.Tiles.Single(tile => tile.GridX < chainDouble.GridX && tile.GridY == 0);
        var rightMain = layout.Tiles.Single(tile => tile.GridX > chainDouble.GridX && tile.GridY == 0);
        var junctionPip = chainDouble.FacingLowPip;

        var snapshot = CreateSnapshot(layout);
        var html = BonesViewerPageRenderer.Render(snapshot, null, null);
        var document = await ParseHtmlAsync(html);
        var domTiles = document.GetElementById("board-chain")!
            .QuerySelectorAll(".board-chain-tile")
            .Select(ParseDomTilePips)
            .ToDictionary(tile => (tile.GridX, tile.GridY));

        Assert.True(domTiles.TryGetValue((chainDouble.GridX, chainDouble.GridY), out var domDouble));
        Assert.Equal(junctionPip, domDouble.LowHalfPip);
        Assert.Equal(junctionPip, domDouble.HighHalfPip);
        Assert.True(domTiles.TryGetValue((leftMain.GridX, leftMain.GridY), out var domLeft));
        Assert.True(domTiles.TryGetValue((rightMain.GridX, rightMain.GridY), out var domRight));
        Assert.Contains(junctionPip, new[] { domLeft.LowHalfPip, domLeft.HighHalfPip });
        Assert.Contains(junctionPip, new[] { domRight.LowHalfPip, domRight.HighHalfPip });
    }

    private static BonesBoardVisualLayout BuildSandwichedDoubleBranchLayout()
    {
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(3));
        var doubleTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(3));
        var extension = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, doubleTile, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        return new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
    }

    private static (int GridX, int GridY, int LowHalfPip, int HighHalfPip) ParseDomTilePips(IElement element)
    {
        var gridX = int.Parse(element.GetAttribute("data-grid-x")!);
        var gridY = int.Parse(element.GetAttribute("data-grid-y")!);
        var lowHalf = element.QuerySelector(".domino-half-low")!;
        var highHalf = element.QuerySelector(".domino-half-high")!;
        var lowPip = int.Parse(lowHalf.GetAttribute("data-pip")!);
        var highPip = int.Parse(highHalf.GetAttribute("data-pip")!);
        return (gridX, gridY, lowPip, highPip);
    }

    private static BonesBoardVisualLayout BuildDoubleBranchLayout()
    {
        var opening = new BonesTile(new BonesPipCount(1), new BonesPipCount(3));
        var doubleTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(3));
        var extension = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, doubleTile, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        return new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
    }

    private static bool FineCellsOverlap(
        int leftColumnStart,
        int leftColumnEnd,
        int leftRowStart,
        int leftRowEnd,
        int rightColumnStart,
        int rightColumnEnd,
        int rightRowStart,
        int rightRowEnd) =>
        leftColumnStart < rightColumnEnd
        && rightColumnStart < leftColumnEnd
        && leftRowStart < rightRowEnd
        && rightRowStart < leftRowEnd;

    private static TilePlacement ParseTilePlacement(IElement element)
    {
        var style = element.GetAttribute("style") ?? string.Empty;
        return new TilePlacement(
            int.Parse(element.GetAttribute("data-grid-x")!),
            int.Parse(element.GetAttribute("data-grid-y")!),
            ParseStyleInt(style, "--tile-grid-column-start"),
            ParseStyleInt(style, "--tile-grid-column-end"),
            ParseStyleInt(style, "--tile-grid-row-start"),
            ParseStyleInt(style, "--tile-grid-row-end"));
    }

    private static int ParseStyleInt(string style, string propertyName)
    {
        var token = propertyName + ":";
        var start = style.IndexOf(token, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {propertyName} in style '{style}'.");
        start += token.Length;
        var end = style.IndexOf(';', start);
        if (end < 0)
            end = style.Length;

        return int.Parse(style[start..end].Trim());
    }

    private static BonesMatchViewModel CreateSnapshot(BonesBoardVisualLayout layout) => new()
    {
        SessionId = "session-branch",
        MatchId = "match-branch",
        FrameCount = 3,
        Revision = 1,
        LeftEndPip = 1,
        RightEndPip = 5,
        ActiveSeat = 1,
        HandTileCountsBySeat = new Dictionary<int, int> { [1] = 5, [2] = 5, [3] = 5, [4] = 5 },
        HandTilesBySeat = new Dictionary<int, IReadOnlyList<BonesHandTileView>>(),
        BoardLayout = layout,
        PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
        CumulativeScoresBySeat = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0 },
        IsComplete = false,
    };

    private static async Task<IDocument> ParseHtmlAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(request => request.Content(html));
    }

    private readonly record struct TilePlacement(
        int GridX,
        int GridY,
        int ColumnStart,
        int ColumnEnd,
        int RowStart,
        int RowEnd);
}