using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesBoardChainScaleCalculatorTransformScaleTests
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.TransformScaleOccupiedExtent;

    private readonly BonesBoardVisualLayoutBuilder _layoutBuilder = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainScaleCalculator_GivenLongChainNarrowSlot_ExpectedTransformScaleBelowOne()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);

        Assert.True(layout.OccupiedWidthPixels > 280);
        Assert.True(layout.OccupiedHeightPixels > 0);

        var scale = BonesBoardChainScaleCalculator.ComputeTransformScale(
            layout,
            containerWidth: 280,
            containerHeight: 160);

        Assert.True(scale < 1.0);
        Assert.True(scale >= BonesBoardChainScaleCalculator.MinReadableTilePixels / BonesBoardChainScaleCalculator.TileUnitPixels);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesBoardChainScaleCalculator_GivenLongChainNarrowSlot_ExpectedTileUnitUnchanged()
    {
        var layout = BuildEngineReplayedLayout(minimumTileCount: 12);

        var scale = BonesBoardChainScaleCalculator.ComputeTransformScale(
            layout,
            containerWidth: 280,
            containerHeight: 160);

        Assert.Equal(40, BonesBoardChainScaleCalculator.TileUnitPixels);
        Assert.Equal(28, BonesBoardChainScaleCalculator.MinReadableTilePixels);

        var renderedTilePixels = BonesBoardChainScaleCalculator.TileUnitPixels * scale;
        Assert.True(renderedTilePixels >= BonesBoardChainScaleCalculator.MinReadableTilePixels);

        var rawFitScale = Math.Min(
            280.0 / layout.OccupiedWidthPixels,
            160.0 / layout.OccupiedHeightPixels);
        var minTransformScale = BonesBoardChainScaleCalculator.MinReadableTilePixels / BonesBoardChainScaleCalculator.TileUnitPixels;
        var expectedScale = Math.Min(1.0, Math.Max(rawFitScale, minTransformScale));
        Assert.Equal(expectedScale, scale, precision: 5);
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
        var matchId = new BonesGameId($"transform-scale-{seed}");
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