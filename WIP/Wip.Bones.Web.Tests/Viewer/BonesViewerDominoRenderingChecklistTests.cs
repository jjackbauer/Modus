using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests.Viewer;

public sealed class BonesViewerDominoRenderingChecklistTests
{
    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.ChainFacingCorrectness)]
    public void BonesBoardTileFacingResolver_GivenEngineReplayedBoard_ExpectedFacingHighMatchesNextFacingLow()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);

        for (var index = 0; index < layout.Tiles.Count - 1; index++)
        {
            var left = layout.Tiles[index];
            var right = layout.Tiles[index + 1];

            Assert.Equal(left.FacingHighPip, right.FacingLowPip);
            Assert.Equal(left.FacingHighPip, BonesBoardVisualLayoutBuilder.GetConnectingPip(left, right));
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.BidirectionalChainLeft)]
    public void BonesBoardChainGeometryBuilder_GivenLeftAndRightArms_ExpectedSpatialConnectingPipMatchesAtOpening()
    {
        var leftTile = new BonesTile(new BonesPipCount(1), new BonesPipCount(2));
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var rightTile = new BonesTile(new BonesPipCount(4), new BonesPipCount(5));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([leftTile, opening, rightTile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, leftTile, BonesBoardSide.Left),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, rightTile, BonesBoardSide.Right),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);
        var leftPlacement = layout.Tiles.Single(tile => tile.GridX < 0);
        var rightPlacement = layout.Tiles.Single(tile => tile.GridX > 0);

        Assert.Equal(2, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(leftPlacement, openingPlacement));
        Assert.Equal(4, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(openingPlacement, rightPlacement));
        Assert.True(leftPlacement.GridX < 0);
        Assert.True(rightPlacement.GridX > 0);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.BidirectionalChainLeft)]
    public void BonesBoardChainGeometryBuilder_GivenLeftArmPlay_ExpectedNegativeGridXAndSpatialPipMatchesLeftEnd()
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

        var layout = _layoutBuilder.BuildLayout(board, events);
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);
        var leftPlacement = layout.Tiles.Single(tile => tile.GridX < 0);

        Assert.True(leftPlacement.GridX < 0);
        Assert.Equal(openingPlacement.FineColumnStart, leftPlacement.FineColumnEnd);
        Assert.Equal(2, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(leftPlacement, openingPlacement));
        Assert.Equal(1, leftPlacement.FacingLowPip);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.BidirectionalChainRight)]
    public void BonesBoardChainGeometryBuilder_GivenRightArmPlay_ExpectedPositiveGridXAndMatchingJunctionPips()
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
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);
        var rightPlacement = layout.Tiles.Single(tile => tile.GridX > 0);

        Assert.True(rightPlacement.GridX > 0);
        Assert.Equal(openingPlacement.FacingHighPip, rightPlacement.FacingLowPip);
        Assert.Equal(4, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(openingPlacement, rightPlacement));
        Assert.Equal(openingPlacement.FineColumnEnd, rightPlacement.FineColumnStart);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.DoubleSixRender)]
    public void BonesBoardChainGeometryBuilder_GivenVerticalDoubleSixAtChainEnd_ExpectedFineCellsInsideOccupiedExtent()
    {
        var opening = new BonesTile(new BonesPipCount(1), new BonesPipCount(3));
        var extension = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
        var connector = new BonesTile(new BonesPipCount(5), new BonesPipCount(6));
        var doubleSix = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension, connector, doubleSix]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, connector, BonesBoardSide.Right),
            new BonesEvent(3, new BonesPlayerId(4), BonesEventKind.Play, doubleSix, BonesBoardSide.Right),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var doublePlacement = layout.Tiles.Single(tile => tile.IsDouble && tile.LowPip == 6);

        Assert.Equal(BonesTileOrientation.Vertical, doublePlacement.Orientation);
        Assert.Equal(BonesPipAxis.Vertical, doublePlacement.PipAxis);
        Assert.True(doublePlacement.GridX > 0);

        var minColumnStart = layout.Tiles.Min(static tile => tile.FineColumnStart);
        var maxColumnEnd = layout.Tiles.Max(static tile => tile.FineColumnEnd);
        var minRowStart = layout.Tiles.Min(static tile => tile.FineRowStart);
        var maxRowEnd = layout.Tiles.Max(static tile => tile.FineRowEnd);

        Assert.True(doublePlacement.FineColumnStart >= minColumnStart);
        Assert.True(doublePlacement.FineColumnEnd <= maxColumnEnd);
        Assert.True(doublePlacement.FineRowStart >= minRowStart);
        Assert.True(doublePlacement.FineRowEnd <= maxRowEnd);
        Assert.Equal(maxColumnEnd - minColumnStart, layout.FineColumnCount);
        Assert.Equal(maxRowEnd - minRowStart, layout.FineRowCount);
        Assert.Equal(layout.FineColumnCount * (int)BonesBoardChainScaleCalculator.TileUnitPixels, layout.OccupiedWidthPixels);
        Assert.Equal(layout.FineRowCount * (int)BonesBoardChainScaleCalculator.TileUnitPixels, layout.OccupiedHeightPixels);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.StandardPipGridRender)]
    public void BonesDominoTileMarkup_GivenBoardTileOrientations_ExpectedStandardPipPositionsOnHalves()
    {
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var horizontal = new BonesTile(new BonesPipCount(4), new BonesPipCount(6));
        var doubleTile = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, horizontal, doubleTile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, horizontal, BonesBoardSide.Right),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var colors = BonesPlayerColorPalette.PlayerColorsBySeat;

        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0 && tile.GridY == 0);
        AssertBoardHalfPipPositions(BonesDominoTileMarkup.RenderBoardTile(openingPlacement, colors, layout), openingPlacement.FacingLowPip, openingPlacement.FacingHighPip);
        Assert.Equal(BonesPipAxis.Horizontal, openingPlacement.PipAxis);

        var horizontalPlacement = layout.Tiles.Single(tile => tile.Orientation == BonesTileOrientation.Horizontal);
        AssertBoardHalfPipPositions(BonesDominoTileMarkup.RenderBoardTile(horizontalPlacement, colors, layout), horizontalPlacement.FacingLowPip, horizontalPlacement.FacingHighPip);
        Assert.Equal(BonesPipAxis.Horizontal, horizontalPlacement.PipAxis);

        var chainDouble = layout.Tiles.Single(tile => tile.IsDouble);
        AssertBoardHalfPipPositions(BonesDominoTileMarkup.RenderBoardTile(chainDouble, colors, layout), chainDouble.FacingLowPip, chainDouble.FacingHighPip);
        Assert.Equal(BonesPipAxis.Vertical, chainDouble.PipAxis);
        Assert.Equal(0, chainDouble.GridY);
    }

    private static void AssertBoardHalfPipPositions(string markup, int lowPip, int highPip)
    {
        AssertHalfPipPositions(markup, "domino-half-low", lowPip);
        AssertHalfPipPositions(markup, "domino-half-high", highPip);
    }

    private static void AssertHalfPipPositions(string markup, string halfClass, int pip)
    {
        var classMarker = "class=\"domino-half " + halfClass + "\"";
        var halfStart = markup.IndexOf(classMarker, StringComparison.Ordinal);
        Assert.True(halfStart >= 0, $"Markup must include {halfClass}.");

        var halfEnd = markup.IndexOf("</div>", halfStart, StringComparison.Ordinal);
        var halfMarkup = markup[halfStart..halfEnd];
        var positions = System.Text.RegularExpressions.Regex.Matches(halfMarkup, "data-position=\"(\\d+)\"")
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(BonesStandardDominoPipLayout.GetGridPositions(pip), positions);
    }


    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.ChainFacingCorrectness)]
    public void BonesBoardTileFacingResolver_GivenEngineReplayedBoard_ExpectedEveryDoubleShowsJunctionPipOnBothHalves()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);

        foreach (var doublePlacement in layout.Tiles.Where(tile => tile.IsDouble))
        {
            Assert.Equal(doublePlacement.FacingLowPip, doublePlacement.FacingHighPip);

            if (doublePlacement.ChainIndex == 0 || doublePlacement.ChainIndex == layout.Tiles.Count - 1)
                continue;

            var left = layout.Tiles[doublePlacement.ChainIndex - 1];
            var right = layout.Tiles[doublePlacement.ChainIndex + 1];
            var junctionPip = BonesBoardVisualLayoutBuilder.GetConnectingPip(left, doublePlacement)
                ?? BonesBoardVisualLayoutBuilder.GetConnectingPip(doublePlacement, right);

            Assert.NotNull(junctionPip);
            Assert.Equal(junctionPip, doublePlacement.FacingLowPip);
            Assert.Equal(junctionPip, doublePlacement.FacingHighPip);
        }
    }    private BonesBoardVisualLayout BuildEngineReplayedLayout(int minimumTileCount)
    {
        for (var seed = 1; seed <= 250; seed++)
        {
            var layout = TryBuildLayoutForSeed(seed, targetScore: 5, minimumTileCount);
            if (layout is not null && layout.Tiles.Count >= minimumTileCount)
                return layout;
        }

        throw new InvalidOperationException($"Unable to locate an engine-replayed layout with at least {minimumTileCount} board tiles.");
    }

    private BonesBoardVisualLayout? TryBuildLayoutForSeed(int seed, int targetScore, int minimumTileCount)
    {
        var matchId = new BonesGameId($"facing-{seed}");
        var simulator = new BonesMatchSimulator();
        var config = new BonesMatchConfig(matchId, seed, targetScore, CreateFirstLegalMoveSlots());
        var matchResult = simulator.RunMatch(config);
        if (matchResult.Rounds.Length == 0)
            return null;

        var lastRound = matchResult.Rounds[^1];
        var engine = new BonesGameEngine();
        var roundConfig = new BonesRoundConfig(new BonesGameId($"{matchId.Value}-r{lastRound.RoundNumber}"), HashCode.Combine(seed, lastRound.RoundNumber));
        var state = engine.StartRound(roundConfig);

        foreach (var roundEvent in lastRound.EventLog)
        {
            var move = roundEvent.Kind == BonesEventKind.Pass
                ? BonesMove.Pass(new BonesMoveId($"{roundConfig.GameId.Value}-pass-{roundEvent.TurnIndex}"), roundEvent.PlayerId)
                : BonesMove.Play(new BonesMoveId($"{roundConfig.GameId.Value}-play-{roundEvent.TurnIndex}"), roundEvent.PlayerId, roundEvent.Tile!.Value, roundEvent.Side!.Value);
            state = engine.ApplyMove(state, move);
        }

        if (state.Board.Tiles.Length < minimumTileCount)
            return null;

        var playEvents = lastRound.EventLog.Where(static roundEvent => roundEvent.Kind == BonesEventKind.Play).ToArray();
        return _layoutBuilder.BuildLayout(state.Board, playEvents);
    }

    private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreateFirstLegalMoveSlots()
    {
        var slot = new BonesFirstLegalMovePlayerSlot();
        return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
        {
            [new BonesPlayerId(1)] = slot,
            [new BonesPlayerId(2)] = slot,
            [new BonesPlayerId(3)] = slot,
            [new BonesPlayerId(4)] = slot,
        };
    }
}