using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardVisualLayoutExtentTests
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.FineGridExtentMetadata;

    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardVisualLayoutBuilder_GivenEmptyBoard_ExpectedZeroFineGridExtent()
    {
        var layout = _layoutBuilder.BuildLayout(BonesBoard.Empty, []);

        Assert.Equal(0, layout.FineColumnCount);
        Assert.Equal(0, layout.FineRowCount);
        Assert.Equal(0, layout.OccupiedWidthPixels);
        Assert.Equal(0, layout.OccupiedHeightPixels);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardVisualLayoutBuilder_GivenOpeningAndRightNeighbor_ExpectedFineGridExtentFromOccupancy()
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

        Assert.Equal(4, layout.FineColumnCount);
        Assert.Equal(2, layout.FineRowCount);
        Assert.Equal(4 * (int)BonesBoardChainScaleCalculator.TileUnitPixels, layout.OccupiedWidthPixels);
        Assert.Equal(2 * (int)BonesBoardChainScaleCalculator.TileUnitPixels, layout.OccupiedHeightPixels);
        AssertExtentMatchesTileFineCells(layout);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardVisualLayoutBuilder_GivenDoubleInChain_ExpectedVerticalMainLineFineGridExtent()
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

        Assert.Equal(5, layout.FineColumnCount);
        Assert.Equal(2, layout.FineRowCount);
        Assert.Equal(5 * (int)BonesBoardChainScaleCalculator.TileUnitPixels, layout.OccupiedWidthPixels);
        Assert.Equal(2 * (int)BonesBoardChainScaleCalculator.TileUnitPixels, layout.OccupiedHeightPixels);
        AssertExtentMatchesTileFineCells(layout);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardVisualLayoutBuilder_GivenEngineReplayedLongChain_ExpectedOccupiedPixelsMatchFineGridCounts()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);

        Assert.True(layout.FineColumnCount > 0);
        Assert.True(layout.FineRowCount > 0);
        Assert.Equal(
            layout.FineColumnCount * (int)BonesBoardChainScaleCalculator.TileUnitPixels,
            layout.OccupiedWidthPixels);
        Assert.Equal(
            layout.FineRowCount * (int)BonesBoardChainScaleCalculator.TileUnitPixels,
            layout.OccupiedHeightPixels);
        AssertExtentMatchesTileFineCells(layout);
    }

    private static void AssertExtentMatchesTileFineCells(BonesBoardVisualLayout layout)
    {
        var minColumnStart = layout.Tiles.Min(static tile => tile.FineColumnStart);
        var maxColumnEnd = layout.Tiles.Max(static tile => tile.FineColumnEnd);
        var minRowStart = layout.Tiles.Min(static tile => tile.FineRowStart);
        var maxRowEnd = layout.Tiles.Max(static tile => tile.FineRowEnd);

        Assert.Equal(maxColumnEnd - minColumnStart, layout.FineColumnCount);
        Assert.Equal(maxRowEnd - minRowStart, layout.FineRowCount);
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
        var matchId = new BonesGameId($"extent-{seed}");
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
