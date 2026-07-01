using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardChainGeometryBuilderTests
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.ChainGeometryBuilder;

    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();
    private readonly BonesBoardChainGeometryBuilder _geometryBuilder = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenEngineReplayedLongChain_ExpectedNoOverlappingFineCells()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);

        AssertNoOverlappingFineCells(layout.Tiles);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenOpeningAndRightNeighbor_ExpectedContinuousFineColumns()
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

        Assert.Equal(BonesTileOrientation.Vertical, openingPlacement.Orientation);
        Assert.Equal(BonesTileOrientation.Horizontal, rightPlacement.Orientation);
        Assert.Equal(openingPlacement.FineColumnEnd, rightPlacement.FineColumnStart);
        Assert.True(openingPlacement.FineRowStart < rightPlacement.FineRowEnd);
        Assert.True(rightPlacement.FineRowStart < openingPlacement.FineRowEnd);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenDoubleInChain_ExpectedNonOverlappingMainLineFineCells()
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

        var layout = _layoutBuilder.BuildLayout(board, events);

        Assert.All(layout.Tiles, tile => Assert.Equal(0, tile.GridY));
        AssertNoOverlappingFineCells(layout.Tiles);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenLayout_ExpectedAssignFineGridCellsMatchesIntegratedBuilder()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);
        var reassigned = _geometryBuilder.AssignFineGridCells(ClearFineCells(layout));

        Assert.Equal(layout.Tiles.Count, reassigned.Tiles.Count);
        for (var index = 0; index < layout.Tiles.Count; index++)
        {
            AssertFineCellEqual(layout.Tiles[index], reassigned.Tiles[index]);
        }

        Assert.Equal(layout.FineColumnCount, reassigned.FineColumnCount);
        Assert.Equal(layout.FineRowCount, reassigned.FineRowCount);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenRightArmOnly_ExpectedRightTileFineColumnStartEqualsOpeningFineColumnEnd()
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

        Assert.Equal(openingPlacement.FineColumnEnd, rightPlacement.FineColumnStart);
        Assert.Equal(4, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(openingPlacement, rightPlacement));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenGreedyReplayedBothArms_ExpectedNormalizedFineGridWithinOccupiedExtent()
    {
        var layout = BuildEngineReplayedBothArmsLayout();
        AssertNormalizedFineGridWithinOccupiedExtent(layout);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainGeometryBuilder_GivenSandwichedDouble_ExpectedMainLineNeighborsShareEdgeWithDouble()
    {
        var leftTile = new BonesTile(new BonesPipCount(2), new BonesPipCount(3));
        var doubleTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(3));
        var rightTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([leftTile, doubleTile, rightTile]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, leftTile, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
            new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, rightTile, BonesBoardSide.Right),
        };

        var layout = _layoutBuilder.BuildLayout(board, events);
        var chainDouble = layout.Tiles.Single(tile => tile.IsDouble);
        var leftMain = layout.Tiles.Single(tile => tile.GridX < chainDouble.GridX && tile.GridY == 0);
        var rightMain = layout.Tiles.Single(tile => tile.GridX > chainDouble.GridX && tile.GridY == 0);

        Assert.Equal(3, chainDouble.FacingLowPip);
        Assert.Equal(3, chainDouble.FacingHighPip);
        Assert.Equal(leftMain.FineColumnEnd, chainDouble.FineColumnStart);
        Assert.Equal(chainDouble.FineColumnEnd, rightMain.FineColumnStart);
        Assert.Equal(3, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(leftMain, chainDouble));
        Assert.Equal(3, BonesBoardChainGeometryBuilder.GetSpatialConnectingPip(chainDouble, rightMain));
        Assert.NotEqual(leftMain.FineColumnEnd, rightMain.FineColumnStart);
    }

    private static void AssertNoOverlappingFineCells(IReadOnlyList<BonesBoardTilePlacement> tiles)
    {
        for (var leftIndex = 0; leftIndex < tiles.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < tiles.Count; rightIndex++)
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
                    $"Tiles at chain index {left.ChainIndex} and {right.ChainIndex} overlap.");
            }
        }
    }

    private static void AssertNormalizedFineGridWithinOccupiedExtent(BonesBoardVisualLayout layout)
    {
        var minColumnStart = layout.Tiles.Min(static tile => tile.FineColumnStart);
        var maxColumnEnd = layout.Tiles.Max(static tile => tile.FineColumnEnd);
        var minRowStart = layout.Tiles.Min(static tile => tile.FineRowStart);
        var maxRowEnd = layout.Tiles.Max(static tile => tile.FineRowEnd);

        Assert.Equal(1, minColumnStart);
        Assert.Equal(1, minRowStart);
        Assert.Equal(maxColumnEnd - minColumnStart, layout.FineColumnCount);
        Assert.Equal(maxRowEnd - minRowStart, layout.FineRowCount);
        Assert.True(maxColumnEnd <= layout.FineColumnCount + 1);
        Assert.True(maxRowEnd <= layout.FineRowCount + 1);

        foreach (var tile in layout.Tiles)
        {
            Assert.InRange(tile.FineColumnStart, 1, layout.FineColumnCount);
            Assert.InRange(tile.FineColumnEnd, 1, layout.FineColumnCount + 1);
            Assert.InRange(tile.FineRowStart, 1, layout.FineRowCount);
            Assert.InRange(tile.FineRowEnd, 1, layout.FineRowCount + 1);
        }
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

    private static BonesBoardVisualLayout ClearFineCells(BonesBoardVisualLayout layout) =>
        layout with
        {
            Tiles = layout.Tiles
                .Select(tile => tile with
                {
                    FineColumnStart = 0,
                    FineColumnEnd = 0,
                    FineRowStart = 0,
                    FineRowEnd = 0,
                })
                .ToArray(),
            FineColumnCount = 0,
            FineRowCount = 0,
            OccupiedWidthPixels = 0,
            OccupiedHeightPixels = 0,
        };

    private static void AssertFineCellEqual(BonesBoardTilePlacement expected, BonesBoardTilePlacement actual)
    {
        Assert.Equal(expected.FineColumnStart, actual.FineColumnStart);
        Assert.Equal(expected.FineColumnEnd, actual.FineColumnEnd);
        Assert.Equal(expected.FineRowStart, actual.FineRowStart);
        Assert.Equal(expected.FineRowEnd, actual.FineRowEnd);
    }

    private BonesBoardVisualLayout BuildEngineReplayedLayout(int minimumTileCount)
    {
        for (var seed = 1; seed <= 250; seed++)
        {
            var layout = TryBuildLayoutForSeed(seed, targetScore: 5, minimumTileCount);
            if (layout is not null && layout.Tiles.Count >= minimumTileCount)
                return layout;
        }

        throw new InvalidOperationException(
            $"Unable to locate an engine-replayed layout with at least {minimumTileCount} board tiles.");
    }

    private BonesBoardVisualLayout? TryBuildLayoutForSeed(int seed, int targetScore, int minimumTileCount)
    {
        var matchId = new BonesGameId($"geometry-{seed}");
        var simulator = new BonesMatchSimulator();
        var config = new BonesMatchConfig(
            matchId,
            seed,
            targetScore,
            CreateFirstLegalMoveSlots());
        var matchResult = simulator.RunMatch(config);
        if (matchResult.Rounds.Length == 0)
            return null;

        var lastRound = matchResult.Rounds[^1];
        var engine = new BonesGameEngine();
        var roundConfig = new BonesRoundConfig(
            new BonesGameId($"{matchId.Value}-r{lastRound.RoundNumber}"),
            HashCode.Combine(seed, lastRound.RoundNumber));
        var state = engine.StartRound(roundConfig);

        foreach (var roundEvent in lastRound.EventLog)
        {
            var move = roundEvent.Kind == BonesEventKind.Pass
                ? BonesMove.Pass(
                    new BonesMoveId($"{roundConfig.GameId.Value}-pass-{roundEvent.TurnIndex}"),
                    roundEvent.PlayerId)
                : BonesMove.Play(
                    new BonesMoveId($"{roundConfig.GameId.Value}-play-{roundEvent.TurnIndex}"),
                    roundEvent.PlayerId,
                    roundEvent.Tile!.Value,
                    roundEvent.Side!.Value);
            state = engine.ApplyMove(state, move);
        }

        if (state.Board.Tiles.Length < minimumTileCount)
            return null;

        var playEvents = lastRound.EventLog
            .Where(static roundEvent => roundEvent.Kind == BonesEventKind.Play)
            .ToArray();

        return _layoutBuilder.BuildLayout(state.Board, playEvents);
    }

    private BonesBoardVisualLayout BuildEngineReplayedBothArmsLayout()
    {
        var engine = new BonesGameEngine();

        for (var seed = 1; seed <= 5000; seed++)
        {
            var roundConfig = new BonesRoundConfig(
                new BonesGameId($"both-arms-{seed}"),
                HashCode.Combine(seed, 1));
            var state = engine.StartRound(roundConfig);
            var events = new List<BonesEvent>(3);
            var sides = new BonesBoardSide?[] { null, BonesBoardSide.Right, BonesBoardSide.Left };

            for (var stepIndex = 0; stepIndex < sides.Length; stepIndex++)
            {
                var side = sides[stepIndex];
                var player = state.CurrentPlayer;
                var legalMoves = engine.GetLegalMoves(state, player);
                var move = side is null
                    ? legalMoves.FirstOrDefault(candidate => !candidate.IsPass)
                    : legalMoves.FirstOrDefault(candidate => !candidate.IsPass && candidate.Side == side);

                if (move is null)
                    break;

                state = engine.ApplyMove(state, move);
                events.Add(new BonesEvent(
                    stepIndex,
                    player,
                    move.IsPass ? BonesEventKind.Pass : BonesEventKind.Play,
                    move.Tile,
                    move.Side));
            }

            if (events.Count < 3)
                continue;

            var layout = _layoutBuilder.BuildLayout(state.Board, events);
            if (layout.MinGridX < 0 && layout.MaxGridX > 0)
                return layout;
        }

        throw new InvalidOperationException(
            "Unable to locate an engine-replayed layout with both left and right arms.");
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
