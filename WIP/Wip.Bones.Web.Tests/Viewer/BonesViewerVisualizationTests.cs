using System.Net;
using System.Net.Http.Json;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerVisualizationServiceTests
{
    private const string ViewerServiceReplayItem = BonesViewerVisualizationRequirementsChecklistItems.ViewerServiceReplay;
    private const string ExtendedViewModelsItem = BonesViewerVisualizationRequirementsChecklistItems.ExtendedViewModels;

    [Fact]
    [Trait("ChecklistItem", ViewerServiceReplayItem)]
    public void BonesMatchViewerService_GivenRoundState_ExpectedSnapshotHandTilesMatchEngineHands()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("visual-hands"), shuffleSeed: 442);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (engine.GetLegalMoves(state, state.CurrentPlayer).Count > 0 && state.EventLog.Length < 4)
            state = engine.ApplyMove(state, engine.GetLegalMoves(state, state.CurrentPlayer)[0]);

        var snapshot = service.BuildSnapshot(state);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            var engineTiles = state.Hands.GetHand(playerId).Tiles;
            var viewTiles = snapshot.HandTilesBySeat[seat];

            Assert.Equal(engineTiles.Length, viewTiles.Count);
            Assert.Equal(engineTiles.Length, snapshot.HandTileCountsBySeat[seat]);

            for (var index = 0; index < engineTiles.Length; index++)
            {
                Assert.Equal(engineTiles[index].LowPip.Value, viewTiles[index].LowPip);
                Assert.Equal(engineTiles[index].HighPip.Value, viewTiles[index].HighPip);
                Assert.Equal(seat, viewTiles[index].OwnerSeat);
            }
        }
    }

    [Fact]
    [Trait("ChecklistItem", ViewerServiceReplayItem)]
    public void BonesMatchViewerService_GivenRoundState_ExpectedSnapshotBoardLayoutMatchesBuilderOutput()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("visual-board"), shuffleSeed: 9001);
        var layoutBuilder = new BonesBoardVisualLayoutBuilder();
        var service = new BonesMatchViewerService(engine, config, layoutBuilder);
        var state = engine.StartRound(config);

        while (engine.GetLegalMoves(state, state.CurrentPlayer).Count > 0 && state.EventLog.Length < 5)
            state = engine.ApplyMove(state, engine.GetLegalMoves(state, state.CurrentPlayer)[0]);

        var snapshot = service.BuildSnapshot(state);
        var expectedLayout = layoutBuilder.BuildLayout(
            state.Board,
            state.EventLog.Where(static e => e.Kind == BonesEventKind.Play).ToArray());

        Assert.Equal(expectedLayout.Tiles.Count, snapshot.BoardLayout.Tiles.Count);
        for (var index = 0; index < expectedLayout.Tiles.Count; index++)
        {
            Assert.Equal(expectedLayout.Tiles[index].LowPip, snapshot.BoardLayout.Tiles[index].LowPip);
            Assert.Equal(expectedLayout.Tiles[index].HighPip, snapshot.BoardLayout.Tiles[index].HighPip);
            Assert.Equal(expectedLayout.Tiles[index].Orientation, snapshot.BoardLayout.Tiles[index].Orientation);
            Assert.Equal(expectedLayout.Tiles[index].GridX, snapshot.BoardLayout.Tiles[index].GridX);
            Assert.Equal(expectedLayout.Tiles[index].PlayedBySeat, snapshot.BoardLayout.Tiles[index].PlayedBySeat);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ViewerServiceReplayItem)]
    public void BonesMatchViewerService_GivenTimelineFrame_ExpectedHandAndBoardReflectPostMoveState()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("visual-frame"), shuffleSeed: 123);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (engine.GetLegalMoves(state, state.CurrentPlayer).Count > 0 && state.EventLog.Length < 3)
            state = engine.ApplyMove(state, engine.GetLegalMoves(state, state.CurrentPlayer)[0]);

        var frames = service.BuildTimeline(state.EventLog);
        var replayState = engine.StartRound(config);
        foreach (var roundEvent in state.EventLog.OrderBy(static e => e.TurnIndex))
        {
            replayState = engine.ApplyMove(
                replayState,
                ToMove(replayState, roundEvent));
        }

        var lastFrame = frames[^1];
        var finalSnapshot = service.BuildSnapshot(replayState);

        Assert.Equal(finalSnapshot.BoardLayout.Tiles.Count, lastFrame.BoardLayout.Tiles.Count);
        Assert.Equal(finalSnapshot.HandTilesBySeat.Count, lastFrame.HandTilesBySeat.Count);
        Assert.Equal(finalSnapshot.ActiveSeat, lastFrame.ActiveSeat);
    }

    [Fact]
    [Trait("ChecklistItem", ExtendedViewModelsItem)]
    public void BonesMatchViewerService_GivenSnapshot_ExpectedPlayerColorsBySeatAreStableAndDistinct()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("visual-colors"), shuffleSeed: 77);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);
        var snapshot = service.BuildSnapshot(state);
        var frames = service.BuildTimeline(state.EventLog);

        Assert.Equal(4, snapshot.PlayerColorsBySeat.Count);
        Assert.Equal(4, snapshot.PlayerColorsBySeat.Values.Distinct(StringComparer.Ordinal).Count());

        foreach (var frame in frames)
        {
            Assert.Equal(snapshot.PlayerColorsBySeat, frame.PlayerColorsBySeat);
        }
    }

    private static BonesMove ToMove(BonesRoundState state, BonesEvent roundEvent)
    {
        if (roundEvent.Kind == BonesEventKind.Pass)
            return BonesMove.Pass(new BonesMoveId($"pass-{roundEvent.TurnIndex}"), roundEvent.PlayerId);

        return BonesMove.Play(
            new BonesMoveId($"play-{roundEvent.TurnIndex}"),
            roundEvent.PlayerId,
            roundEvent.Tile!.Value,
            roundEvent.Side!.Value);
    }
}

public sealed class BonesViewerVisualizationApiTests : IClassFixture<BonesWebApplicationFactory>
{
    private const string JsonApiRoutesItem = BonesViewerVisualizationRequirementsChecklistItems.JsonApiRoutes;

    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerVisualizationApiTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", JsonApiRoutesItem)]
    public async Task BonesWebHost_GivenMatchSnapshotRequest_ExpectedJsonIncludesBoardLayoutAndHandTiles()
    {
        const string sessionId = "session-visual-snapshot";
        const int seed = 442;
        var matchId = new BonesGameId("match-visual-snapshot");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(
            _factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered),
            "Registered match was not found.");
        var expected = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actual = await response.Content.ReadFromJsonAsync<BonesMatchViewModel>(BonesWebHost.JsonOptions);

        Assert.NotNull(actual);
        Assert.Equal(expected.BoardLayout.Tiles.Count, actual!.BoardLayout.Tiles.Count);
        Assert.Equal(expected.HandTilesBySeat.Count, actual.HandTilesBySeat.Count);
        Assert.Equal(expected.PlayerColorsBySeat, actual.PlayerColorsBySeat);
        Assert.Equal(expected.LeftEndPip, actual.LeftEndPip);
        Assert.Equal(expected.HandTileCountsBySeat, actual.HandTileCountsBySeat);
    }

    [Fact]
    [Trait("ChecklistItem", JsonApiRoutesItem)]
    public async Task BonesWebHost_GivenFrameRequest_ExpectedJsonIncludesBoardLayoutAndHandTilesForTurn()
    {
        const string sessionId = "session-visual-frame";
        const int seed = 9001;
        var matchId = new BonesGameId("match-visual-frame");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(
            _factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered),
            "Registered match was not found.");

        var frames = viewer.BuildMatchTimeline(registered);
        var targetFrame = frames.First(frame => frame.EventKind == BonesEventKind.Play);
        var turnIndex = targetFrame.TurnIndex;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{turnIndex}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actual = await response.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);

        Assert.NotNull(actual);
        Assert.Equal(targetFrame.BoardLayout.Tiles.Count, actual!.BoardLayout.Tiles.Count);
        Assert.Equal(targetFrame.HandTilesBySeat.Count, actual.HandTilesBySeat.Count);
        Assert.Equal(targetFrame.ActiveSeat, actual.ActiveSeat);
    }

    [Fact]
    [Trait("ChecklistItem", JsonApiRoutesItem)]
    public async Task BonesWebHost_GivenForeignSessionMatch_ExpectedDeterministicNotFoundWithoutVisualFieldLeakage()
    {
        const string ownerSessionId = "session-visual-owner";
        const string foreignSessionId = "session-visual-foreign";
        var matchId = new BonesGameId("match-visual-isolated");
        var matchResult = CreateSeededMatch(matchId, seed: 123, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(ownerSessionId),
            matchId,
            MatchSeed: 123,
            matchResult));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{foreignSessionId}/matches/{matchId.Value}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("boardLayout", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("handTilesBySeat", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#e63946", body, StringComparison.Ordinal);
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
}

public sealed class BonesViewerVisualizationDomTests : IClassFixture<BonesWebApplicationFactory>
{
    private const string SsrRendererItem = BonesViewerVisualizationRequirementsChecklistItems.SsrRenderer;
    private const string HandTileUiItem = BonesViewerVisualizationRequirementsChecklistItems.HandTileUi;
    private const string SpaReplayScrubItem = BonesViewerVisualizationRequirementsChecklistItems.SpaReplayScrub;
    private const string BoardChainUiItem = BonesViewerVisualizationRequirementsChecklistItems.BoardChainUi;
    private const string TileCssMarkupItem = BonesViewerVisualizationRequirementsChecklistItems.TileCssMarkup;

    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerVisualizationDomTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", SsrRendererItem)]
    public async Task BonesViewerPage_GivenSnapshotPayload_ExpectedRendersBoardChainTilesWithOrientationAttributes()
    {
        const string sessionId = "session-dom-board";
        const int seed = 442;
        var matchId = new BonesGameId("match-dom-board");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(
            _factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered),
            "Registered match was not found.");
        var expected = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardChain = document.GetElementById("board-chain");
        Assert.NotNull(boardChain);

        var dominoTiles = boardChain!.QuerySelectorAll(".domino-tile");
        Assert.Equal(expected.BoardLayout.Tiles.Count, dominoTiles.Length);

        var sortedPlacements = expected.BoardLayout.Tiles.OrderBy(static t => t.ChainIndex).ToArray();
        for (var index = 0; index < sortedPlacements.Length; index++)
        {
            var placement = sortedPlacements[index];
            var element = dominoTiles[index];
            Assert.Equal(placement.Orientation.ToString().ToLowerInvariant(), element.GetAttribute("data-orientation"));
            Assert.Equal(placement.FacingLowPip.ToString(), element.GetAttribute("data-low-pip"));
            Assert.Equal(placement.FacingHighPip.ToString(), element.GetAttribute("data-high-pip"));
            Assert.Equal(placement.PlayedBySeat.ToString(), element.GetAttribute("data-played-by-seat"));
            Assert.NotNull(element.QuerySelector(".domino-divider"));
        }
    }

    [Fact]
    [Trait("ChecklistItem", HandTileUiItem)]
    public async Task BonesViewerPage_GivenSnapshotPayload_ExpectedRendersHandTilesWithOwnerSeatColorMarkers()
    {
        const string sessionId = "session-dom-hands";
        const int seed = 9001;
        var matchId = new BonesGameId("match-dom-hands");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(
            _factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered),
            "Registered match was not found.");
        var expected = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var hand = document.QuerySelector($".hand[data-seat=\"{seat}\"]");
            Assert.NotNull(hand);
            var handTiles = hand!.QuerySelectorAll(".hand-tiles .domino-tile");
            Assert.Equal(expected.HandTilesBySeat[seat].Count, handTiles.Length);

            foreach (var tileElement in handTiles)
            {
                Assert.Equal(seat.ToString(), tileElement.GetAttribute("data-owner-seat"));
                Assert.NotNull(tileElement.QuerySelector(".seat-color-marker, .domino-divider[data-seat-color]"));
                Assert.False(string.IsNullOrWhiteSpace(tileElement.GetAttribute("data-seat-color")));
            }
        }
    }

    [Fact]
    [Trait("ChecklistItem", SpaReplayScrubItem)]
    public async Task BonesViewerPage_GivenReplayScrubTurnIndex_ExpectedBoardAndHandsMatchFrameAtTurn()
    {
        const string sessionId = "session-dom-scrub";
        const int seed = 123;
        var matchId = new BonesGameId("match-dom-scrub");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(
            _factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered),
            "Registered match was not found.");

        var frames = viewer.BuildMatchTimeline(registered);
        var targetFrame = frames[frames.Count / 2];
        var turnIndex = targetFrame.TurnIndex;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view?turnIndex={turnIndex}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardTiles = document.GetElementById("board-chain")!.QuerySelectorAll(".domino-tile");
        Assert.Equal(targetFrame.BoardLayout.Tiles.Count, boardTiles.Length);

        var activeHand = document.QuerySelector($".hand.seat-active[data-seat=\"{targetFrame.ActiveSeat}\"]");
        Assert.NotNull(activeHand);
    }

    [Fact]
    [Trait("ChecklistItem", BoardChainUiItem)]
    public async Task BonesViewerPage_GivenDoubleOnBoard_ExpectedVerticalOrientationInDom()
    {
        const string sessionId = "session-dom-double";
        const int matchSeed = 77;
        var matchId = new BonesGameId("match-dom-double");
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(
            new BonesGameId($"{matchId.Value}-r1"),
            HashCode.Combine(matchSeed, 1));
        var state = engine.StartRound(config);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
            if (legalMoves.Count == 0)
                break;

            var playMove = legalMoves.FirstOrDefault(
                move => !move.IsPass && move.Tile!.Value.IsDouble)
                ?? legalMoves.FirstOrDefault(move => !move.IsPass)
                ?? legalMoves[0];
            state = engine.ApplyMove(state, playMove);

            if (state.Board.Tiles.Any(static chainTile => chainTile.Tile.IsDouble))
                break;
        }

        Assert.Contains(state.Board.Tiles, static chainTile => chainTile.Tile.IsDouble);

        var matchResult = new BonesMatchResult(
            matchId,
            state.CurrentPlayer,
            new Dictionary<BonesPlayerId, int>
            {
                [new BonesPlayerId(1)] = 0,
                [new BonesPlayerId(2)] = 0,
                [new BonesPlayerId(3)] = 0,
                [new BonesPlayerId(4)] = 0,
            },
            [],
            state.EventLog.ToArray());

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            MatchSeed: matchSeed,
            matchResult,
            IsComplete: false));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var doubleDomTile = document
            .GetElementById("board-chain")!
            .QuerySelectorAll(".domino-tile")
            .First(tile => tile.GetAttribute("data-is-double") == "true");

        Assert.Equal("vertical", doubleDomTile.GetAttribute("data-orientation"));
    }

    [Fact]
    [Trait("ChecklistItem", TileCssMarkupItem)]
    public async Task BonesViewerPage_GivenStaticSpaAssets_ExpectedViewerScriptRendersBoardChainAndHandTiles()
    {
        using var client = _factory.CreateClient();

        var indexResponse = await client.GetAsync("/viewer/index.html");
        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"board-chain\"", indexHtml, StringComparison.Ordinal);

        var scriptResponse = await client.GetAsync("/viewer/viewer.js");
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        var script = await scriptResponse.Content.ReadAsStringAsync();
        Assert.Contains("renderBoardChain", script, StringComparison.Ordinal);
        Assert.Contains("renderHands", script, StringComparison.Ordinal);
        Assert.Contains("board-chain", script, StringComparison.Ordinal);
        Assert.Contains("hand-tiles", script, StringComparison.Ordinal);

        var cssResponse = await client.GetAsync("/viewer/viewer.css");
        Assert.Equal(HttpStatusCode.OK, cssResponse.StatusCode);
        var css = await cssResponse.Content.ReadAsStringAsync();
        Assert.Contains(".domino-tile", css, StringComparison.Ordinal);
        Assert.Contains(".seat-color-marker", css, StringComparison.Ordinal);
    }

    private static async Task<IDocument> ParseHtmlAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(request => request.Content(html));
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
}

