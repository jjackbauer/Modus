using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesDominoTileMarkupGridPlacementTests
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.GridColumnStartEndPlacement;

    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDominoTileMarkup_GivenGeometryFields_ExpectedGridColumnStartEndInStyle()
    {
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var extension = new BonesTile(new BonesPipCount(4), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var placement = layout.Tiles.Single(tile => tile.GridX == 1);

        var style = BonesDominoTileMarkup.FormatGridPlacementStyle(placement, layout);

        Assert.Contains(
            $"--tile-grid-column-start: {placement.FineColumnStart.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            style,
            StringComparison.Ordinal);
        Assert.Contains(
            $"--tile-grid-column-end: {placement.FineColumnEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            style,
            StringComparison.Ordinal);
        Assert.Contains(
            $"--tile-grid-row-start: {placement.FineRowStart.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            style,
            StringComparison.Ordinal);
        Assert.Contains(
            $"--tile-grid-row-end: {placement.FineRowEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            style,
            StringComparison.Ordinal);
        Assert.DoesNotContain("--tile-grid-column:", style, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerCss_GivenBoardChainTiles_ExpectedGridColumnStartEndSelectors()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var css = await (await client.GetAsync("/viewer/viewer.css")).Content.ReadAsStringAsync();

        Assert.Contains("grid-column: var(--tile-grid-column-start) / var(--tile-grid-column-end)", css, StringComparison.Ordinal);
        Assert.Contains("grid-row: var(--tile-grid-row-start) / var(--tile-grid-row-end)", css, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerScript_GivenCreateBoardTile_ExpectedGridColumnStartEndCustomProperties()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("--tile-grid-column-start", script, StringComparison.Ordinal);
        Assert.Contains("--tile-grid-column-end", script, StringComparison.Ordinal);
        Assert.Contains("--tile-grid-row-start", script, StringComparison.Ordinal);
        Assert.Contains("--tile-grid-row-end", script, StringComparison.Ordinal);
        Assert.Contains("fineColumnStart", script, StringComparison.Ordinal);
    }
}