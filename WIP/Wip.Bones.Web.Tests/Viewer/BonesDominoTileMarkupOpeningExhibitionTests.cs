using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests.Viewer;

public sealed class BonesDominoTileMarkupOpeningExhibitionTests
{
    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.OpeningDoubleSixExhibitionFix)]
    public void BonesDominoTileMarkup_GivenSixSixOpeningPlacement_ExpectedMarkupUsesReadableHalfDimensions()
    {
        var opening = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var placement = layout.Tiles.Single(tile => tile.GridX == 0 && tile.GridY == 0);

        Assert.True(placement.IsDouble);
        Assert.Equal(BonesTileOrientation.Vertical, placement.Orientation);
        Assert.Equal(BonesPipAxis.Vertical, placement.PipAxis);
        Assert.Equal(6, placement.FacingLowPip);
        Assert.Equal(6, placement.FacingHighPip);

        var colors = BonesPlayerColorPalette.PlayerColorsBySeat;
        var markup = BonesDominoTileMarkup.RenderBoardTile(placement, colors, layout);

        Assert.Contains("data-orientation=\"vertical\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-pip-axis=\"vertical\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-is-double=\"true\"", markup, StringComparison.Ordinal);
        AssertHalfPipCount(markup, "domino-half-low", 6);
        AssertHalfPipCount(markup, "domino-half-high", 6);
        AssertHalfPipPositions(markup, "domino-half-low", 6);
        AssertHalfPipPositions(markup, "domino-half-high", 6);

        var gridStyle = BonesDominoTileMarkup.FormatGridPlacementStyle(placement, layout);
        Assert.Contains("--tile-grid-column-start:", gridStyle, StringComparison.Ordinal);
        Assert.Contains("--tile-grid-row-start:", gridStyle, StringComparison.Ordinal);
        Assert.Equal(1, placement.FineColumnEnd - placement.FineColumnStart);
    }

    [Fact]
    public void BonesBoardChainGeometryBuilder_GivenSixSixOpeningOnly_ExpectedSingleColumnFineGridSpan()
    {
        var opening = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var placement = layout.Tiles.Single(tile => tile.GridX == 0);

        Assert.Equal(BonesPipAxis.Vertical, placement.PipAxis);
        Assert.Equal(1, placement.FineColumnEnd - placement.FineColumnStart);
        Assert.Equal(1, layout.FineColumnCount);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.OpeningDoubleSixExhibitionFix)]
    public async Task BonesViewerCss_GivenOpeningVerticalDoubleWithVerticalPipAxis_ExpectedPortraitTileDimensions()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var css = await (await client.GetAsync("/viewer/viewer.css")).Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                @"\[data-orientation=""vertical""\]\[data-pip-axis=""vertical""\]\s*\{[^}]*flex-direction:\s*column",
                RegexOptions.Singleline),
            css);

        Assert.Matches(
            new Regex(
                @"\[data-orientation=""vertical""\]\[data-pip-axis=""vertical""\]\s*\{[^}]*width:\s*var\(--board-cell-size\)",
                RegexOptions.Singleline),
            css);

        Assert.Matches(
            new Regex(
                @"\[data-orientation=""vertical""\]\[data-pip-axis=""vertical""\]\s*\{[^}]*min-height:\s*calc\(var\(--board-cell-size\)\s*\*\s*2\)",
                RegexOptions.Singleline),
            css);
    }

    private static void AssertHalfPipCount(string markup, string halfClass, int expectedCount)
    {
        var classMarker = "class=\"domino-half " + halfClass + "\"";
        var halfStart = markup.IndexOf(classMarker, StringComparison.Ordinal);
        Assert.True(halfStart >= 0, $"Markup must include {halfClass}.");

        var halfEnd = markup.IndexOf("</div>", halfStart, StringComparison.Ordinal);
        var halfMarkup = markup[halfStart..halfEnd];
        var pipCount = Regex.Matches(halfMarkup, "class=\"pip\"").Count;
        Assert.Equal(expectedCount, pipCount);
    }

    private static void AssertHalfPipPositions(string markup, string halfClass, int pip)
    {
        var classMarker = "class=\"domino-half " + halfClass + "\"";
        var halfStart = markup.IndexOf(classMarker, StringComparison.Ordinal);
        Assert.True(halfStart >= 0, $"Markup must include {halfClass}.");

        var halfEnd = markup.IndexOf("</div>", halfStart, StringComparison.Ordinal);
        var halfMarkup = markup[halfStart..halfEnd];
        var positions = Regex.Matches(halfMarkup, "data-position=\"(\\d+)\"")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(BonesStandardDominoPipLayout.GetGridPositions(pip), positions);
    }
}