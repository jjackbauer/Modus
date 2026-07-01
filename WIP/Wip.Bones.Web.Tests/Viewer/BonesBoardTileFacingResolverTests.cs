using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests.Viewer;

public sealed class BonesBoardTileFacingResolverTests
{
    [Fact]
    public void BonesBoardVisualLayoutBuilder_GivenVerticalOpeningWithHorizontalNeighbors_ExpectedHorizontalPipAxisAndChainFacingPips()
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

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);

        Assert.Equal(BonesPipAxis.Horizontal, openingPlacement.PipAxis);
        Assert.Equal(2, openingPlacement.FacingLowPip);
        Assert.Equal(4, openingPlacement.FacingHighPip);
        Assert.Equal(2, layout.Tiles[0].FacingHighPip);
        Assert.Equal(4, layout.Tiles[2].FacingLowPip);
    }

    [Fact]
    public void BonesBoardVisualLayoutBuilder_GivenSixSixOpening_ExpectedVerticalPipAxis()
    {
        var opening = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
        };

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);

        Assert.True(openingPlacement.IsDouble);
        Assert.Equal(BonesTileOrientation.Vertical, openingPlacement.Orientation);
        Assert.Equal(BonesPipAxis.Vertical, openingPlacement.PipAxis);
    }

    [Fact]
    public void BonesBoardVisualLayoutBuilder_GivenBranchDouble_ExpectedVerticalPipAxis()
    {
        var opening = new BonesTile(new BonesPipCount(1), new BonesPipCount(3));
        var doubleTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(3));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, doubleTile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
        };

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
        var branchDouble = layout.Tiles.Single(tile => tile.IsDouble);

        Assert.Equal(BonesPipAxis.Vertical, branchDouble.PipAxis);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.LayoutConnectivity)]
    public void BonesBoardVisualLayoutBuilder_GivenAdjacentTiles_ExpectedRightFaceMatchesLeftFace()
    {
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var extension = new BonesTile(new BonesPipCount(4), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);

        Assert.Equal(4, BonesBoardVisualLayoutBuilder.GetConnectingPip(layout.Tiles[0], layout.Tiles[1]));
        Assert.Equal(layout.Tiles[0].FacingHighPip, layout.Tiles[1].FacingLowPip);
    }

    [Fact]
    public void BonesBoardVisualLayoutBuilder_GivenLeftPlayWithFlippedOrientation_ExpectedResolvesPlayEventAndBothEndsConnect()
    {
        var opening = new BonesTile(new BonesPipCount(0), new BonesPipCount(0));
        var leftExtension = new BonesTile(new BonesPipCount(0), new BonesPipCount(4));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain(
        [
            new BonesTile(new BonesPipCount(4), new BonesPipCount(0)),
            opening,
        ]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, leftExtension, BonesBoardSide.Left),
        };

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
        var leftPlacement = layout.Tiles.Single(tile => tile.GridX < 0);
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);

        Assert.Equal(4, leftPlacement.FacingLowPip);
        Assert.Equal(0, leftPlacement.FacingHighPip);
        Assert.Equal(0, openingPlacement.FacingLowPip);
        Assert.Equal(2, leftPlacement.PlayedBySeat);
        Assert.Equal(0, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(leftPlacement, openingPlacement));
    }

    [Fact]
    public void BonesBoardVisualLayoutBuilder_GivenLeftAndRightArms_ExpectedBothArmsSpatiallyMatchOpening()
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

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);
        var leftPlacement = layout.Tiles.Single(tile => tile.GridX < 0);
        var rightPlacement = layout.Tiles.Single(tile => tile.GridX > 0);

        Assert.Equal(2, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(leftPlacement, openingPlacement));
        Assert.Equal(4, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(openingPlacement, rightPlacement));
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.DeleteFalsePositiveTest)]
    public void ResolveChainFacing_GivenEngineReplayedLeftArmZeroPipChain_ExpectedFacingsMatchChainStorage()
    {
        var board = new BonesBoard(
        [
            new BonesChainTile(new BonesTile(new BonesPipCount(4), new BonesPipCount(0)), new BonesPipCount(4), new BonesPipCount(0)),
            new BonesChainTile(new BonesTile(new BonesPipCount(0), new BonesPipCount(0)), new BonesPipCount(0), new BonesPipCount(0)),
        ]);

        var (lowFacing, highFacing) = BonesBoardTileFacingResolver.ResolveChainFacing(board.Tiles, 0);

        Assert.Equal(board.Tiles[0].ChainLeftPip.Value, lowFacing);
        Assert.Equal(board.Tiles[0].ChainRightPip.Value, highFacing);
        Assert.Equal(4, lowFacing);
        Assert.Equal(0, highFacing);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.FacingFromStorage)]
    public void ResolveChainFacing_GivenSimpleTwoTileChain_ExpectedFacingsMatchChainStorage()
    {
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain(
        [
            new BonesTile(new BonesPipCount(2), new BonesPipCount(4)),
            new BonesTile(new BonesPipCount(4), new BonesPipCount(6)),
        ]);

        for (var index = 0; index < board.Tiles.Length; index++)
        {
            var (lowFacing, highFacing) = BonesBoardTileFacingResolver.ResolveChainFacing(board.Tiles, index);
            Assert.Equal(board.Tiles[index].ChainLeftPip.Value, lowFacing);
            Assert.Equal(board.Tiles[index].ChainRightPip.Value, highFacing);
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.FacingFromStorage)]
    public void ResolveChainFacing_GivenSandwichedDouble_ExpectedBothFacingsEqualSharedPip()
    {
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain(
        [
            new BonesTile(new BonesPipCount(2), new BonesPipCount(3)),
            new BonesTile(new BonesPipCount(3), new BonesPipCount(3)),
            new BonesTile(new BonesPipCount(3), new BonesPipCount(5)),
        ]);

        var (lowFacing, highFacing) = BonesBoardTileFacingResolver.ResolveChainFacing(board.Tiles, 1);

        Assert.Equal(3, lowFacing);
        Assert.Equal(3, highFacing);
    }

    /// <summary>
    /// Reproduces learning-79da18443e60429bbc00508278b6f8be board chain indices 8-11:
    /// 1-2, 1-1, 1-3, 1-5 — the bogus 3-touching-1 junction the viewer draws.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.SandwichSliceRegression)]
    public void ResolveChainFacing_GivenLearningMatchSandwichSlice_ExpectedFacingHighMatchesNextFacingLow()
    {
        var (state, _) = LearningMatch79daReplayHelper.ReplayFinalRound();
        var board = state.Board;

        for (var index = 0; index < board.Tiles.Length - 1; index++)
        {
            var (_, leftFacingHigh) = BonesBoardTileFacingResolver.ResolveChainFacing(board.Tiles, index);
            var (rightFacingLow, _) = BonesBoardTileFacingResolver.ResolveChainFacing(board.Tiles, index + 1);

            Assert.Equal(leftFacingHigh, rightFacingLow);
        }

        var oneThreeIndex = Array.FindIndex(
            board.Tiles.ToArray(),
            chainTile => chainTile.Tile == new BonesTile(new BonesPipCount(1), new BonesPipCount(3)));
        Assert.True(oneThreeIndex >= 0);
        Assert.True(oneThreeIndex < board.Tiles.Length - 1);
        Assert.Equal(1, board.Tiles[oneThreeIndex].ChainRightPip.Value);
        Assert.Equal(1, board.Tiles[oneThreeIndex + 1].ChainLeftPip.Value);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.LayoutConnectivity)]
    public void BonesBoardVisualLayoutBuilder_GivenLearningMatchSandwichSlice_ExpectedFacingHighMatchesNextFacingLow()
    {
        var (state, playEvents) = LearningMatch79daReplayHelper.ReplayFinalRound();

        var layout = new BonesBoardVisualLayoutBuilder().BuildLayout(state.Board, playEvents);
        var ordered = layout.Tiles.OrderBy(static tile => tile.ChainIndex).ToArray();

        for (var index = 0; index < ordered.Length - 1; index++)
        {
            Assert.Equal(ordered[index].FacingHighPip, ordered[index + 1].FacingLowPip);
        }

        var oneThreePlacement = ordered.Single(tile => tile.LowPip == 1 && tile.HighPip == 3);
        var oneFivePlacement = ordered.Single(tile => tile.LowPip == 1 && tile.HighPip == 5);
        Assert.Equal(1, oneThreePlacement.FacingHighPip);
        Assert.Equal(1, oneFivePlacement.FacingLowPip);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.LayoutConnectivity)]
    public void BonesMatchViewerService_GivenEngineReplayedMatch_ExpectedFrameBoardLayoutMatchesBoardChainFacings()
    {
        var (finalState, _) = LearningMatch79daReplayHelper.ReplayFinalRound();
        var viewer = new BonesMatchViewerService(new BonesGameEngine());
        var snapshot = viewer.BuildSnapshot(finalState);

        Assert.Equal(finalState.Board.Tiles.Length, snapshot.BoardLayout.Tiles.Count);

        for (var index = 0; index < finalState.Board.Tiles.Length; index++)
        {
            var chainTile = finalState.Board.Tiles[index];
            var placement = snapshot.BoardLayout.Tiles.Single(tile => tile.ChainIndex == index);

            Assert.Equal(chainTile.ChainLeftPip.Value, placement.FacingLowPip);
            Assert.Equal(chainTile.ChainRightPip.Value, placement.FacingHighPip);
        }

        for (var index = 0; index < snapshot.BoardLayout.Tiles.Count - 1; index++)
        {
            var left = snapshot.BoardLayout.Tiles[index];
            var right = snapshot.BoardLayout.Tiles[index + 1];
            Assert.Equal(left.FacingHighPip, right.FacingLowPip);
        }
    }
}