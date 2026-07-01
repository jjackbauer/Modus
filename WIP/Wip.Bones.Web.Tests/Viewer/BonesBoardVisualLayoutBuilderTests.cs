using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardVisualLayoutBuilderTests
{
    private const string LayoutBuilderItem = BonesViewerVisualizationRequirementsChecklistItems.LayoutBuilder;
    private const string ViewerDtosItem = BonesViewerVisualizationRequirementsChecklistItems.ViewerDtos;

    private readonly BonesBoardVisualLayoutBuilder _builder = new();

    [Fact]
    [Trait("ChecklistItem", LayoutBuilderItem)]
    public void BonesBoardVisualLayoutBuilder_GivenOpeningPlay_ExpectedFirstTileVerticalAtCenter()
    {
        var tile = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([tile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, tile, null),
        };

        var layout = _builder.BuildLayout(board, events);

        var placement = Assert.Single(layout.Tiles);
        Assert.Equal(0, placement.GridX);
        Assert.Equal(0, placement.GridY);
        Assert.Equal(BonesTileOrientation.Vertical, placement.Orientation);
        Assert.Equal(3, placement.LowPip);
        Assert.Equal(5, placement.HighPip);
        Assert.False(placement.IsDouble);
        Assert.Equal(1, placement.PlayedBySeat);
    }

    [Fact]
    [Trait("ChecklistItem", LayoutBuilderItem)]
    public void BonesBoardVisualLayoutBuilder_GivenNonDoubleExtension_ExpectedHorizontalOrientationAndShiftedGridX()
    {
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var extension = new BonesTile(new BonesPipCount(4), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        var layout = _builder.BuildLayout(board, events);

        Assert.Equal(2, layout.Tiles.Count);
        var rightPlacement = layout.Tiles.Single(tile => tile.GridX > 0);
        Assert.Equal(1, rightPlacement.GridX);
        Assert.Equal(BonesTileOrientation.Horizontal, rightPlacement.Orientation);
        Assert.Equal(4, rightPlacement.LowPip);
        Assert.Equal(6, rightPlacement.HighPip);
    }

    [Fact]
    [Trait("ChecklistItem", LayoutBuilderItem)]
    public void BonesBoardVisualLayoutBuilder_GivenDoubleInChain_ExpectedVerticalOrientationOnMainLine()
    {
        var opening = new BonesTile(new BonesPipCount(1), new BonesPipCount(3));
        var doubleTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(3));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, doubleTile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
        };

        var layout = _builder.BuildLayout(board, events);

        var doublePlacement = layout.Tiles.Single(tile => tile.IsDouble);
        Assert.Equal(BonesTileOrientation.Vertical, doublePlacement.Orientation);
        Assert.Equal(0, doublePlacement.GridY);
        Assert.Equal(1, doublePlacement.GridX);
    }

    [Fact]
    [Trait("ChecklistItem", LayoutBuilderItem)]
    public void BonesBoardVisualLayoutBuilder_GivenLeftAndRightPlays_ExpectedChainSymmetricAboutOpening()
    {
        var leftTile = new BonesTile(new BonesPipCount(1), new BonesPipCount(2));
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var rightTile = new BonesTile(new BonesPipCount(4), new BonesPipCount(5));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([leftTile, opening, rightTile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, rightTile, BonesBoardSide.Right),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, leftTile, BonesBoardSide.Left),
        };

        var layout = _builder.BuildLayout(board, events);

        Assert.Equal(3, layout.Tiles.Count);
        Assert.Contains(layout.Tiles, tile => tile.GridX == 0);
        Assert.Contains(layout.Tiles, tile => tile.GridX == -1);
        Assert.Contains(layout.Tiles, tile => tile.GridX == 1);

        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);
        var leftPlacement = layout.Tiles.Single(tile => tile.GridX == -1);
        var rightPlacement = layout.Tiles.Single(tile => tile.GridX == 1);
        Assert.Equal(2, leftPlacement.FacingHighPip);
        Assert.Equal(2, openingPlacement.FacingLowPip);
        Assert.Equal(4, openingPlacement.FacingHighPip);
        Assert.Equal(4, rightPlacement.FacingLowPip);
        Assert.Equal(2, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(leftPlacement, openingPlacement));
        Assert.Equal(4, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(openingPlacement, rightPlacement));
    }

    [Fact]
    [Trait("ChecklistItem", LayoutBuilderItem)]
    public void BonesBoardVisualLayoutBuilder_GivenPlayEvents_ExpectedPlayedBySeatMatchesEventPlayerId()
    {
        var opening = new BonesTile(new BonesPipCount(0), new BonesPipCount(1));
        var extension = new BonesTile(new BonesPipCount(1), new BonesPipCount(2));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(4), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        var layout = _builder.BuildLayout(board, events);

        Assert.Equal(4, layout.Tiles[0].PlayedBySeat);
        Assert.Equal(2, layout.Tiles[1].PlayedBySeat);
    }

    [Fact]
    [Trait("ChecklistItem", ViewerDtosItem)]
    public void BonesPlayerColorPalette_GivenSeatsOneThroughFour_ExpectedDistinctStableColorTokens()
    {
        var colors = BonesPlayerColorPalette.PlayerColorsBySeat;

        Assert.Equal(4, colors.Count);
        Assert.Equal(
            4,
            colors.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            Enumerable.Range(BonesPlayerId.MinSeat, BonesPlayerId.MaxSeat),
            seat => Assert.False(string.IsNullOrWhiteSpace(colors[seat])));
    }
}