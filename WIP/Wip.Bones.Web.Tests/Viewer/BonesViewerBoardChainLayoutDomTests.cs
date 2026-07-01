using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("BonesViewerPlaywright")]
public sealed class BonesViewerBoardChainLayoutDomTests : IClassFixture<BonesPlaywrightHost>
{
    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerBoardChainLayoutDomTests(BonesPlaywrightHost playwrightHost)
    {
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ReplayJsOnView)]
    public async Task BonesViewerPage_GivenViewRouteWithoutQueryParams_ExpectedInitializesReplayBootstrapFromDataAttributes()
    {
        const string sessionId = "session-view-bootstrap";
        const int seed = 442;
        var matchId = new BonesGameId("match-view-bootstrap");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);

            var state = await BonesViewerScriptTestDriver.ReadBootstrapStateAsync(page);
            Assert.Equal(sessionId, state.SessionId);
            Assert.Equal(matchId.Value, state.MatchId);
            Assert.Equal(snapshot.FrameCount.ToString(CultureInfo.InvariantCulture), state.FrameCount);
            Assert.True(
                int.Parse(state.ScrubberMax!, CultureInfo.InvariantCulture) == snapshot.FrameCount - 1,
                "Replay scrubber max must reflect loaded match frame count.");

            var midTurn = Math.Max(0, snapshot.FrameCount / 2);
            Assert.True(
                await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, midTurn),
                "Scrubber input must fetch frame JSON and update turn chrome.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ReplayJsOnView)]
    public async Task BonesViewerPage_GivenViewRouteWithoutDataAttributes_ExpectedResolvesSessionMatchFromPathSegments()
    {
        const string sessionId = "session-view-path-only";
        const int seed = 9001;
        var matchId = new BonesGameId("match-view-path-only");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        _ = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value,
            stripSessionMatchDataAttributes: true);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);

            var state = await BonesViewerScriptTestDriver.ReadBootstrapStateAsync(page);
            Assert.Equal(sessionId, state.SessionId);
            Assert.Equal(matchId.Value, state.MatchId);

            Assert.True(
                await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, 0),
                "Path-segment bootstrap must wire replay scrubber to frame API at turn 0.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ReplayJsOnView)]
    public async Task BonesViewerPage_GivenViewRouteWithoutQueryParams_ExpectedExposesSessionMatchDataAttributesInHtml()
    {
        const string sessionId = "session-view-ssr-attrs";
        const int seed = 123;
        var matchId = new BonesGameId("match-view-ssr-attrs");
        RegisterSeededMatch(sessionId, matchId, seed);

        using var client = _playwrightHost.CreateListeningClient();
        var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var document = await ParseHtmlAsync(html);
        var root = document.GetElementById("bones-viewer");
        Assert.NotNull(root);
        Assert.Equal(sessionId, root!.GetAttribute("data-session-id"));
        Assert.Equal(matchId.Value, root.GetAttribute("data-match-id"));
        Assert.Contains("/viewer/viewer.js", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.BootstrapInitialTurnIndex)]
    public async Task BonesViewerPage_GivenViewRouteWithTurnIndexZero_ExpectedBootstrapLoadsFrameZeroNotFinalSnapshot()
    {
        const string sessionId = "session-bootstrap-turn-zero";
        const int seed = 442;
        var matchId = new BonesGameId("match-bootstrap-turn-zero");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var frames = viewer.BuildMatchTimeline(registered);
        var frameZero = frames[0];
        var finalFrame = frames[^1];

        Assert.True(
            finalFrame.BoardLayout.Tiles.Count > frameZero.BoardLayout.Tiles.Count,
            "Completed match must have more board tiles at final turn than at turn 0.");

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value,
            turnIndex: 0);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, 0);

            var boardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page);
            Assert.Equal(frameZero.BoardLayout.Tiles.Count, boardTileCount);
            Assert.True(boardTileCount < finalFrame.BoardLayout.Tiles.Count);

            var scrubberValue = await page.Locator("#replay-scrubber").InputValueAsync();
            Assert.Equal("0", scrubberValue);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.BootstrapInitialTurnIndex)]
    public async Task BonesViewerPage_GivenViewRouteWithoutTurnIndex_ExpectedBootstrapLoadsFinalTurnSnapshot()
    {
        const string sessionId = "session-bootstrap-final-turn";
        const int seed = 442;
        var matchId = new BonesGameId("match-bootstrap-final-turn");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var frames = viewer.BuildMatchTimeline(registered);
        var finalFrame = frames[^1];
        var finalTurnIndex = snapshot.FrameCount - 1;

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, finalTurnIndex);

            var boardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page);
            Assert.Equal(finalFrame.BoardLayout.Tiles.Count, boardTileCount);

            var scrubberValue = await page.Locator("#replay-scrubber").InputValueAsync();
            Assert.Equal(finalTurnIndex.ToString(CultureInfo.InvariantCulture), scrubberValue);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.TurnZeroReplayBehaviorProof)]
    public async Task BonesViewerPage_GivenCompletedMatchTurnZero_ExpectedFewerBoardTilesThanFinalSnapshot()
    {
        const string sessionId = "session-turn-zero-board-count";
        const int seed = 442;
        var matchId = new BonesGameId("match-turn-zero-board-count");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var frames = viewer.BuildMatchTimeline(registered);
        var finalTurnIndex = snapshot.FrameCount - 1;
        var finalFrame = frames[^1];

        Assert.True(snapshot.IsComplete, "Seeded match must be completed.");
        Assert.True(
            finalFrame.BoardLayout.Tiles.Count > frames[0].BoardLayout.Tiles.Count,
            "Completed match must have more board tiles at final turn than at turn 0.");

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();

        var turnZeroPage = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value,
            turnIndex: 0);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(turnZeroPage);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(turnZeroPage, 0);

            var turnZeroBoardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(turnZeroPage);
            Assert.Equal(frames[0].BoardLayout.Tiles.Count, turnZeroBoardTileCount);
            Assert.True(turnZeroBoardTileCount > 0, "Turn 0 must render at least one board-chain tile.");

            var finalPage = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
                browser,
                _playwrightHost.ListeningUri,
                sessionId,
                matchId.Value);
            try
            {
                await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(finalPage);
                await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(finalPage, finalTurnIndex);

                var finalBoardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(finalPage);
                Assert.Equal(finalFrame.BoardLayout.Tiles.Count, finalBoardTileCount);
                Assert.True(
                    turnZeroBoardTileCount < finalBoardTileCount,
                    $"Turn 0 board-chain tile count ({turnZeroBoardTileCount}) must be strictly less than final snapshot ({finalBoardTileCount}).");
            }
            finally
            {
                await finalPage.CloseAsync();
            }
        }
        finally
        {
            await turnZeroPage.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.TurnZeroReplayBehaviorProof)]
    public async Task BonesViewerPage_GivenCompletedMatchTurnZero_ExpectedHandTilesMatchFrameApi()
    {
        const string sessionId = "session-turn-zero-hand-tiles";
        const int seed = 9001;
        var matchId = new BonesGameId("match-turn-zero-hand-tiles");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var frames = viewer.BuildMatchTimeline(registered);
        var frameZero = frames[0];
        var finalFrame = frames[^1];

        Assert.True(snapshot.IsComplete, "Seeded match must be completed.");

        using var client = _playwrightHost.CreateListeningClient();
        var frameResponse = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/0");
        Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);
        var frameApi = await frameResponse.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);
        Assert.NotNull(frameApi);
        Assert.Equal(frameZero.TurnIndex, frameApi!.TurnIndex);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value,
            turnIndex: 0);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, 0);

            var domHands = await BonesViewerScriptTestDriver.GetHandDomStateBySeatAsync(page);
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                Assert.True(domHands.TryGetValue(seat, out var domHand), $"Seat {seat} hand must be present in DOM.");
                Assert.True(
                    frameApi.HandTileCountsBySeat.TryGetValue(seat, out var expectedCount),
                    $"Frame API must include hand tile count for seat {seat}.");
                Assert.Equal(expectedCount, domHand!.TileCount);
                Assert.Equal(expectedCount, domHand.Tiles.Count);

                Assert.True(
                    frameApi.HandTilesBySeat.TryGetValue(seat, out var expectedTiles),
                    $"Frame API must include hand tiles for seat {seat}.");
                Assert.Equal(expectedTiles!.Count, domHand.Tiles.Count);

                for (var tileIndex = 0; tileIndex < expectedTiles.Count; tileIndex++)
                {
                    var expected = expectedTiles[tileIndex];
                    var actual = domHand.Tiles[tileIndex];
                    Assert.Equal(expected.LowPip, actual.LowPip);
                    Assert.Equal(expected.HighPip, actual.HighPip);
                    Assert.Equal(expected.OwnerSeat, actual.OwnerSeat);
                }
            }

            var handsDifferFromFinal = false;
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                var turnZeroCount = frameZero.HandTileCountsBySeat[seat];
                var finalCount = finalFrame.HandTileCountsBySeat[seat];
                if (turnZeroCount != finalCount)
                {
                    handsDifferFromFinal = true;
                    break;
                }
            }

            Assert.True(
                handsDifferFromFinal,
                "Turn 0 and final frame hand tile counts must differ to prove DOM is not showing final snapshot.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ScrubberInputFrameLoad)]
    public async Task BonesViewerScript_GivenScrubberInput_ExpectedTurnLabelMatchesScrubberValueImmediately()
    {
        const string sessionId = "session-scrubber-label-sync";
        const int seed = 442;
        var matchId = new BonesGameId("match-scrubber-label-sync");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var targetTurn = Math.Max(0, snapshot.FrameCount / 3);

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

            var frameGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync(
                $"**/frames/{targetTurn}",
                async route =>
                {
                    await frameGate.Task;
                    await route.ContinueAsync();
                });

            await BonesViewerScriptTestDriver.DispatchScrubberInputAsync(page, targetTurn);

            var labelTurn = await BonesViewerScriptTestDriver.GetTurnLabelTurnIndexAsync(page);
            Assert.Equal(targetTurn.ToString(CultureInfo.InvariantCulture), labelTurn);

            var scrubberTurn = await page.Locator("#replay-scrubber").GetAttributeAsync("data-turn-index");
            Assert.Equal(targetTurn.ToString(CultureInfo.InvariantCulture), scrubberTurn);

            frameGate.SetResult();
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ScrubberInputFrameLoad)]
    public async Task BonesViewerScript_GivenLoadFrameFailure_ExpectedDoesNotLeaveFinalBoardWithEarlyTurnLabel()
    {
        const string sessionId = "session-frame-load-failure";
        const int seed = 442;
        var matchId = new BonesGameId("match-frame-load-failure");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var frames = viewer.BuildMatchTimeline(registered);
        var finalFrame = frames[^1];
        var finalTurnIndex = snapshot.FrameCount - 1;

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, finalTurnIndex);

            var finalBoardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page);
            Assert.Equal(finalFrame.BoardLayout.Tiles.Count, finalBoardTileCount);
            Assert.True(finalBoardTileCount > 0, "Final frame must have board tiles to prove stale state is cleared.");

            await page.RouteAsync(
                "**/frames/0",
                route => route.FulfillAsync(new RouteFulfillOptions { Status = 500 }));

            await BonesViewerScriptTestDriver.DispatchScrubberInputAsync(page, 0);

            await page.WaitForFunctionAsync(
                @"() => document.getElementById('bones-viewer')?.dataset?.frameError === 'true'",
                new PageWaitForFunctionOptions { Timeout = 10_000 });

            var labelTurn = await BonesViewerScriptTestDriver.GetTurnLabelTurnIndexAsync(page);
            Assert.Equal("0", labelTurn);

            var boardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page);
            Assert.Equal(0, boardTileCount);
            Assert.True(boardTileCount < finalBoardTileCount);

            Assert.True(await BonesViewerScriptTestDriver.HasFrameLoadErrorAsync(page));
            Assert.Equal("error", await BonesViewerScriptTestDriver.GetLastMoveKindAsync(page));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private void RegisterSeededMatch(string sessionId, BonesGameId matchId, int seed)
    {
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);
        _playwrightHost.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));
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

[CollectionDefinition("BonesViewerPlaywright", DisableParallelization = true)]
public sealed class BonesViewerPlaywrightCollection;
