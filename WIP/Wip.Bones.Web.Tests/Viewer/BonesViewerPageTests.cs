using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerPageTests : IClassFixture<BonesWebApplicationFactory>
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.BrowserViewerUi;

    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerPageTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenSnapshotPayload_ExpectedRendersBoardEndsAndHandCounts()
    {
        const string sessionId = "session-ui-board";
        const int seed = 442;
        var matchId = new BonesGameId("match-ui-board");
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

        var leftEnd = document.GetElementById("left-end-pip");
        var rightEnd = document.GetElementById("right-end-pip");
        Assert.NotNull(leftEnd);
        Assert.NotNull(rightEnd);
        Assert.Equal(expected.LeftEndPip?.ToString() ?? string.Empty, leftEnd!.GetAttribute("data-pip"));
        Assert.Equal(expected.RightEndPip?.ToString() ?? string.Empty, rightEnd!.GetAttribute("data-pip"));

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var hand = document.QuerySelector($".hand[data-seat=\"{seat}\"]");
            Assert.NotNull(hand);
            var tileCount = hand!.QuerySelector(".tile-count");
            Assert.NotNull(tileCount);
            Assert.Equal(
                expected.HandTileCountsBySeat[seat].ToString(),
                tileCount!.GetAttribute("data-count"));
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenReplayScrub_ExpectedHighlightsActiveSeatAndAppliedMove()
    {
        const string sessionId = "session-ui-replay";
        const int seed = 9001;
        var matchId = new BonesGameId("match-ui-replay");
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
        Assert.NotEmpty(frames);

        var targetFrame = frames.First(frame => frame.EventKind == BonesEventKind.Play);
        var turnIndex = targetFrame.TurnIndex;

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view?turnIndex={turnIndex}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());

        var activeSeat = document.GetElementById("active-seat");
        Assert.NotNull(activeSeat);
        Assert.Equal(targetFrame.ActiveSeat.ToString(), activeSeat!.GetAttribute("data-seat"));

        var activeHand = document.QuerySelector($".hand.seat-active[data-seat=\"{targetFrame.ActiveSeat}\"]");
        Assert.NotNull(activeHand);

        var lastMove = document.GetElementById("last-move");
        Assert.NotNull(lastMove);
        Assert.Equal("play", lastMove!.GetAttribute("data-kind"));
        Assert.Equal(targetFrame.PlayedTileLowPip?.ToString() ?? string.Empty, lastMove.GetAttribute("data-low-pip"));
        Assert.Equal(targetFrame.PlayedTileHighPip?.ToString() ?? string.Empty, lastMove.GetAttribute("data-high-pip"));

        var scrubber = document.GetElementById("replay-scrubber") as IHtmlInputElement;
        Assert.NotNull(scrubber);
        Assert.Equal(turnIndex.ToString(), scrubber!.GetAttribute("data-turn-index"));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenCompletedRound_ExpectedShowsWinnerSeatAndPipScore()
    {
        const string sessionId = "session-ui-complete";
        const int seed = 123;
        var matchId = new BonesGameId("match-ui-complete");
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
        var expectedRoundScore = registered.MatchResult.Rounds[^1].Score.Points;

        Assert.True(expected.IsComplete);
        Assert.NotNull(expected.WinnerSeat);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());

        var winner = document.GetElementById("winner");
        var roundScore = document.GetElementById("round-score");
        Assert.NotNull(winner);
        Assert.NotNull(roundScore);

        Assert.Equal(expected.WinnerSeat!.Value.ToString(), winner!.GetAttribute("data-seat"));
        Assert.Equal(expectedRoundScore.ToString(), roundScore!.GetAttribute("data-pip-score"));
        Assert.Contains($"Winner: Seat {expected.WinnerSeat.Value}", winner.TextContent, StringComparison.Ordinal);
        Assert.Contains(expectedRoundScore.ToString(), roundScore.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenStaticSpaRoute_ExpectedServesViewerAssets()
    {
        using var client = _factory.CreateClient();

        var indexResponse = await client.GetAsync("/viewer/index.html");
        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"bones-viewer\"", indexHtml, StringComparison.Ordinal);
        Assert.Contains("/viewer/viewer.js", indexHtml, StringComparison.Ordinal);

        var scriptResponse = await client.GetAsync("/viewer/viewer.js");
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Contains("replay-scrubber", await scriptResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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