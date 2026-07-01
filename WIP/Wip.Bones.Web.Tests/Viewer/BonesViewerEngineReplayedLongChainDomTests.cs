using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("BonesViewerPlaywright")]
public sealed class BonesViewerEngineReplayedLongChainDomTests : IClassFixture<BonesPlaywrightHost>
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.EngineReplayedLongChainDomProof;

    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerEngineReplayedLongChainDomTests(BonesPlaywrightHost playwrightHost)
    {
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenEngineReplayedLongChain_ExpectedTileDimensionsAboveReadableFloor()
    {
        const string sessionId = "session-long-chain-readable";
        var matchId = new BonesGameId("match-long-chain-readable");
        var snapshot = RegisterLongChainMatch(sessionId, matchId);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            var finalTurn = snapshot.FrameCount - 1;
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, finalTurn);
            await page.SetViewportSizeAsync(640, 480);
            Assert.True(
                await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, finalTurn),
                "Scrubber must reload the final frame after narrowing viewport for transform-scale proof.");

            var metrics = await ReadLongChainLayoutMetricsAsync(page);
            Assert.True(metrics.BoardTileCount >= 12, "Long-chain proof requires at least 12 board tiles.");
            Assert.True(metrics.TransformScale > 0 && metrics.TransformScale < 1.0, "Long chain must use transform scale below 1 to fit slot.");
            Assert.True(metrics.CellSizePixels >= BonesBoardChainScaleCalculator.TileUnitPixels * 0.9,
                "Cell size must remain at full tile unit (no cell-shrink scaling).");

            foreach (var dimension in metrics.TileDimensions)
            {
                Assert.True(
                    dimension.Width >= BonesBoardChainScaleCalculator.MinReadableTilePixels - 0.5
                    || dimension.Height >= BonesBoardChainScaleCalculator.MinReadableTilePixels - 0.5,
                    $"Tile rendered dimension must stay at or above readable floor ({BonesBoardChainScaleCalculator.MinReadableTilePixels}px).");
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenEngineReplayedLongChain_ExpectedTransformScaleOnInnerWrapperNotCellShrink()
    {
        const string sessionId = "session-long-chain-transform";
        var matchId = new BonesGameId("match-long-chain-transform");
        var snapshot = RegisterLongChainMatch(sessionId, matchId);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            var finalTurn = snapshot.FrameCount - 1;
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, finalTurn);
            await page.SetViewportSizeAsync(640, 480);
            Assert.True(
                await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, finalTurn),
                "Scrubber must reload the final frame after narrowing viewport for transform-scale proof.");

            var metrics = await ReadLongChainLayoutMetricsAsync(page);
            Assert.True(metrics.BoardTileCount >= 12);
            Assert.True(metrics.TransformScale > 0 && metrics.TransformScale < 1.0);
            Assert.True(
                metrics.InnerTransform.Contains("matrix", StringComparison.OrdinalIgnoreCase)
                || metrics.InnerTransform.Contains("scale", StringComparison.OrdinalIgnoreCase),
                "Inner wrapper must apply transform scaling.");
            Assert.True(
                metrics.CellSizePixels >= BonesBoardChainScaleCalculator.MinReadableTilePixels,
                "Unscaled cell size must not shrink below readable floor.");
            Assert.True(
                metrics.RenderedTileWidth >= BonesBoardChainScaleCalculator.MinReadableTilePixels - 0.5,
                "Rendered tile width after transform must remain legible.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerScript_GivenLongChainLayout_ExpectedTransformScaleNotCellShrink()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("MIN_READABLE_TILE_PIXELS", script, StringComparison.Ordinal);
        Assert.Contains("board-chain-inner", script, StringComparison.Ordinal);
        Assert.Contains("--board-chain-scale", script, StringComparison.Ordinal);
        Assert.DoesNotContain("board-cell-size", script, StringComparison.Ordinal);
    }

    private BonesMatchViewModel RegisterLongChainMatch(string sessionId, BonesGameId matchId)
    {
        var (seed, matchResult) = CreateLongChainSeededMatch(matchId, minimumTileCount: 12);
        _playwrightHost.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        Assert.True(snapshot.BoardLayout.Tiles.Count >= 12);
        return snapshot;
    }

    private static async Task<LongChainLayoutMetrics> ReadLongChainLayoutMetricsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
            const boardChain = document.getElementById('board-chain');
            const inner = boardChain?.querySelector('.board-chain-inner');
            const tiles = inner
                ? Array.from(inner.querySelectorAll('.board-chain-tile'))
                : [];
            const innerStyle = inner ? getComputedStyle(inner) : null;
            const sampleTile = tiles[0] ?? null;
            const sampleStyle = sampleTile ? getComputedStyle(sampleTile) : null;
            const tileUnit = sampleStyle
                ? parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--board-tile-unit')) || 40
                : 40;
            const cellSize = sampleStyle ? parseFloat(sampleStyle.width) : 0;
            const tileRects = tiles.map(tile => {
                const rect = tile.getBoundingClientRect();
                return { width: rect.width, height: rect.height };
            });
            const sampleRect = sampleTile ? sampleTile.getBoundingClientRect() : { width: 0, height: 0 };
            const scaleText = boardChain?.dataset?.boardChainScale ?? boardChain?.style?.getPropertyValue('--board-chain-scale') ?? '1';
            return {
                boardTileCount: tiles.length,
                transformScale: Number(scaleText),
                innerTransform: innerStyle?.transform ?? '',
                cellSizePixels: cellSize || tileUnit,
                renderedTileWidth: sampleRect.width,
                tileDimensions: tileRects,
            };
        }");

        var tileDimensions = new List<TileDimension>();
        if (json.TryGetProperty("tileDimensions", out var tilesJson))
        {
            foreach (var tileJson in tilesJson.EnumerateArray())
            {
                tileDimensions.Add(new TileDimension(
                    tileJson.GetProperty("width").GetDouble(),
                    tileJson.GetProperty("height").GetDouble()));
            }
        }

        return new LongChainLayoutMetrics(
            json.GetProperty("boardTileCount").GetInt32(),
            json.GetProperty("transformScale").GetDouble(),
            json.GetProperty("innerTransform").GetString() ?? string.Empty,
            json.GetProperty("cellSizePixels").GetDouble(),
            json.GetProperty("renderedTileWidth").GetDouble(),
            tileDimensions);
    }

    private static (int Seed, BonesMatchResult MatchResult) CreateLongChainSeededMatch(
        BonesGameId matchId,
        int minimumTileCount)
    {
        for (var seed = 1; seed <= 250; seed++)
        {
            var matchResult = CreateSeededMatch(matchId, seed, targetScore: 5);
            if (matchResult.Rounds.Length == 0)
                continue;

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

            if (state.Board.Tiles.Length >= minimumTileCount)
                return (seed, matchResult);
        }

        throw new InvalidOperationException(
            $"Unable to locate a seeded match with at least {minimumTileCount.ToString(CultureInfo.InvariantCulture)} board tiles.");
    }

    private static BonesMatchResult CreateSeededMatch(BonesGameId matchId, int seed, int targetScore)
    {
        var simulator = new BonesMatchSimulator();
        var config = new BonesMatchConfig(
            matchId,
            seed,
            targetScore,
            CreateFirstLegalMoveSlots());
        return simulator.RunMatch(config);
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

    private sealed record TileDimension(double Width, double Height);

    private sealed record LongChainLayoutMetrics(
        int BoardTileCount,
        double TransformScale,
        string InnerTransform,
        double CellSizePixels,
        double RenderedTileWidth,
        IReadOnlyList<TileDimension> TileDimensions);
}
