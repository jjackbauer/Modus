using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using AngleSharp;
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
public sealed class BonesViewerBoardChainGeometryDomTests : IClassFixture<BonesPlaywrightHost>
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.GeometryBehaviorProof;

    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerBoardChainGeometryDomTests(BonesPlaywrightHost playwrightHost)
    {
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenEngineReplayedLongChain_ExpectedNoOverlappingTileBoundingBoxes()
    {
        const string sessionId = "session-geometry-overlap";
        var matchId = new BonesGameId("match-geometry-overlap");
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
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, snapshot.FrameCount - 1);

            var boxes = await ReadTileBoundingBoxesAsync(page);
            Assert.True(boxes.Count >= 12);

            for (var leftIndex = 0; leftIndex < boxes.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < boxes.Count; rightIndex++)
                {
                    Assert.False(
                        BoundingBoxesOverlap(boxes[leftIndex], boxes[rightIndex]),
                        $"Tiles {leftIndex} and {rightIndex} must not overlap.");
                }
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenEngineReplayedLongChain_ExpectedTileDimensionsAtOrAboveReadableFloor()
    {
        const string sessionId = "session-geometry-floor";
        var matchId = new BonesGameId("match-geometry-floor");
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
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, snapshot.FrameCount - 1);

            var boxes = await ReadTileBoundingBoxesAsync(page);
            Assert.True(boxes.Count >= 12);

            foreach (var box in boxes)
            {
                Assert.True(
                    box.Width >= BonesBoardChainScaleCalculator.MinReadableTilePixels - 0.5
                    || box.Height >= BonesBoardChainScaleCalculator.MinReadableTilePixels - 0.5,
                    "Each tile must render at or above the readable dimension floor.");
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenLongChain_ExpectedChainWidthWithinBoardChainSlot()
    {
        const string sessionId = "session-geometry-slot";
        var matchId = new BonesGameId("match-geometry-slot");
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
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, snapshot.FrameCount - 1);

            var fit = await ReadChainSlotFitAsync(page);
            Assert.True(fit.BoardTileCount >= 12);
            Assert.True(fit.ScaledChainWidth <= fit.SlotWidth + 1.0,
                "Scaled chain width must fit within the board-chain slot.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenOpeningAndRightNeighbor_ExpectedOpeningFlushToNeighbor()
    {
        var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
        var extension = new BonesTile(new BonesPipCount(4), new BonesPipCount(6));
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension]);
        var events = new[]
        {
            new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
            new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
        };

        var layoutBuilder = new BonesBoardVisualLayoutBuilder();
        var geometryBuilder = new BonesBoardChainGeometryBuilder();
        var coarseLayout = layoutBuilder.BuildLayout(board, events);
        var layout = geometryBuilder.AssignFineGridCells(coarseLayout);

        var openingPlacement = layout.Tiles.Single(tile => tile.GridX == 0);
        var neighborPlacement = layout.Tiles.Single(tile => tile.GridX == 1);
        Assert.Equal(BonesTileOrientation.Vertical, openingPlacement.Orientation);
        Assert.Equal(openingPlacement.FineColumnEnd, neighborPlacement.FineColumnStart);

        var html = BonesViewerPageRenderer.Render(CreateSnapshot(layout), null, null);
        var document = await ParseHtmlAsync(html);
        var tiles = document.GetElementById("board-chain")!
            .QuerySelector(".board-chain-inner")!
            .QuerySelectorAll(".board-chain-tile");

        var openingElement = tiles.First(tile => tile.GetAttribute("data-grid-x") == "0");
        var neighborElement = tiles.First(tile => tile.GetAttribute("data-grid-x") == "1");
        var openingRect = ParseTileFineGridRect(openingElement, openingPlacement);
        var neighborRect = ParseTileFineGridRect(neighborElement, neighborPlacement);

        Assert.True(Math.Abs(openingRect.Right - neighborRect.Left) < 0.01);
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

    private static async Task<IReadOnlyList<TileBoundingBox>> ReadTileBoundingBoxesAsync(IPage page)
    {
        var json = await page.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
            const tiles = Array.from(document.querySelectorAll('#board-chain .board-chain-tile'));
            return tiles.map(tile => {
                const rect = tile.getBoundingClientRect();
                return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
            });
        }");

        var boxes = new List<TileBoundingBox>();
        foreach (var entry in json.EnumerateArray())
        {
            boxes.Add(new TileBoundingBox(
                entry.GetProperty("left").GetDouble(),
                entry.GetProperty("top").GetDouble(),
                entry.GetProperty("width").GetDouble(),
                entry.GetProperty("height").GetDouble()));
        }

        return boxes;
    }

    private static async Task<ChainSlotFitMetrics> ReadChainSlotFitAsync(IPage page)
    {
        var json = await page.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
            const boardChain = document.getElementById('board-chain');
            const inner = boardChain ? boardChain.querySelector('.board-chain-inner') : null;
            const slotRect = boardChain ? boardChain.getBoundingClientRect() : { width: 0 };
            const innerRect = inner ? inner.getBoundingClientRect() : { width: 0 };
            const tiles = inner ? inner.querySelectorAll('.board-chain-tile').length : 0;
            return {
                boardTileCount: tiles,
                slotWidth: slotRect.width,
                scaledChainWidth: innerRect.width,
            };
        }");

        return new ChainSlotFitMetrics(
            json.GetProperty("boardTileCount").GetInt32(),
            json.GetProperty("slotWidth").GetDouble(),
            json.GetProperty("scaledChainWidth").GetDouble());
    }

    private static bool BoundingBoxesOverlap(TileBoundingBox left, TileBoundingBox right)
    {
        const double tolerance = 0.5;
        var leftRight = left.Left + left.Width;
        var leftBottom = left.Top + left.Height;
        var rightRight = right.Left + right.Width;
        var rightBottom = right.Top + right.Height;

        var separated =
            leftRight <= right.Left + tolerance
            || rightRight <= left.Left + tolerance
            || leftBottom <= right.Top + tolerance
            || rightBottom <= left.Top + tolerance;

        return !separated;
    }

    private static TileFineGridRect ParseTileFineGridRect(AngleSharp.Dom.IElement element, BonesBoardTilePlacement placement)
    {
        var style = element.GetAttribute("style") ?? string.Empty;
        var columnStart = ReadCssCustomPropertyInt(style, "--tile-grid-column-start", placement.FineColumnStart);
        var columnEnd = ReadCssCustomPropertyInt(style, "--tile-grid-column-end", placement.FineColumnEnd);
        var rowStart = ReadCssCustomPropertyInt(style, "--tile-grid-row-start", placement.FineRowStart);
        var rowEnd = ReadCssCustomPropertyInt(style, "--tile-grid-row-end", placement.FineRowEnd);

        var unit = BonesBoardChainScaleCalculator.TileUnitPixels;
        return new TileFineGridRect(
            (columnStart - 1) * unit,
            (rowStart - 1) * unit,
            (columnEnd - columnStart) * unit,
            (rowEnd - rowStart) * unit);
    }

    private static int ReadCssCustomPropertyInt(string style, string propertyName, int fallback)
    {
        var marker = propertyName + ":";
        var start = style.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return fallback;

        start += marker.Length;
        var end = style.IndexOf(';', start);
        if (end < 0)
            end = style.Length;

        return int.Parse(style[start..end].Trim(), CultureInfo.InvariantCulture);
    }

    private static async Task<AngleSharp.Dom.IDocument> ParseHtmlAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(request => request.Content(html));
    }

    private static BonesMatchViewModel CreateSnapshot(BonesBoardVisualLayout layout) => new()
    {
        SessionId = "session-opening-flush",
        MatchId = "match-opening-flush",
        LeftEndPip = 2,
        RightEndPip = 6,
        HandTileCountsBySeat = new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1, [4] = 1 },
        BoardLayout = layout,
        HandTilesBySeat = new Dictionary<int, IReadOnlyList<BonesHandTileView>>(),
        PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
        ActiveSeat = 1,
        CumulativeScoresBySeat = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0 },
        FrameCount = 2,
        Revision = 1,
        IsComplete = false,
    };

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

    private sealed record TileBoundingBox(double Left, double Top, double Width, double Height);

    private sealed record ChainSlotFitMetrics(int BoardTileCount, double SlotWidth, double ScaledChainWidth);

    private sealed record TileFineGridRect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
    }
}