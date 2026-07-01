using System.Collections.Immutable;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerReplayScalingDomTests : IClassFixture<BonesWebApplicationFactory>
{
    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerReplayScalingDomTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.CssGridChainLayout)]
    public async Task BonesViewerPage_GivenBoardChain_ExpectedTilesHaveGridPositionAttributes()
    {
        var (sessionId, matchId, expected) = await RegisterSeededMatchAsync("session-grid-pos", 442, "match-grid-pos");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardChain = document.GetElementById("board-chain");
        Assert.NotNull(boardChain);
        Assert.Equal("board-chain", boardChain!.ClassName);

        var dominoTiles = boardChain.QuerySelectorAll(".board-chain-tile");
        Assert.Equal(expected.BoardLayout.Tiles.Count, dominoTiles.Length);

        foreach (var placement in expected.BoardLayout.Tiles)
        {
            var element = dominoTiles.First(tile =>
                tile.GetAttribute("data-grid-x") == placement.GridX.ToString() &&
                tile.GetAttribute("data-grid-y") == placement.GridY.ToString());
            Assert.NotNull(element);
            Assert.False(string.IsNullOrWhiteSpace(element!.GetAttribute("style")));
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.CssGridChainLayout)]
    public async Task BonesViewerPage_GivenDoubleInChain_ExpectedMainLineGridYInDom()
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
        Assert.Contains(layout.Tiles, static tile => tile.IsDouble && tile.GridY == 0);

        var html = BonesViewerPageRenderer.Render(
            CreateSnapshot(layout),
            null,
            null);

        var document = await ParseHtmlAsync(html);
        var doubleDomTile = document.GetElementById("board-chain")!
            .QuerySelectorAll(".board-chain-tile")
            .First(tile => tile.GetAttribute("data-is-double") == "true");

        Assert.Equal("0", doubleDomTile.GetAttribute("data-grid-y"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.CssGridChainLayout)]
    public async Task BonesViewerPage_GivenAdjacentBoardTiles_ExpectedConnectingPipAttributesMatch()
    {
        var (_, matchId, expected) = await RegisterSeededMatchAsync("session-grid-pips", 9001, "match-grid-pips");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{expected.SessionId}/matches/{matchId.Value}/view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var tiles = document.GetElementById("board-chain")!
            .QuerySelectorAll(".board-chain-tile")
            .OrderBy(tile => int.Parse(tile.GetAttribute("data-grid-x")!))
            .ToArray();

        Assert.True(tiles.Length >= 2);

        for (var index = 0; index < expected.BoardLayout.Tiles.Count - 1; index++)
        {
            var leftPlacement = expected.BoardLayout.Tiles[index];
            var rightPlacement = expected.BoardLayout.Tiles[index + 1];
            var connectingPip = BonesBoardVisualLayoutBuilder.GetConnectingPip(leftPlacement, rightPlacement);
            Assert.NotNull(connectingPip);

            var leftElement = tiles.First(tile => tile.GetAttribute("data-grid-x") == leftPlacement.GridX.ToString()
                && tile.GetAttribute("data-grid-y") == leftPlacement.GridY.ToString());
            var rightElement = tiles.First(tile => tile.GetAttribute("data-grid-x") == rightPlacement.GridX.ToString()
                && tile.GetAttribute("data-grid-y") == rightPlacement.GridY.ToString());

            var leftFaces = new[] { leftElement.GetAttribute("data-facing-low-pip"), leftElement.GetAttribute("data-facing-high-pip") };
            var rightFaces = new[] { rightElement.GetAttribute("data-facing-low-pip"), rightElement.GetAttribute("data-facing-high-pip") };
            Assert.Contains(connectingPip.Value.ToString(), leftFaces);
            Assert.Contains(connectingPip.Value.ToString(), rightFaces);
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.CssGridChainLayout)]
    public async Task BonesViewerPage_GivenAdjacentBoardTiles_ExpectedNonOverlappingFineGridPlacementFromLayout()
    {
        var (_, matchId, expected) = await RegisterSeededMatchAsync("session-grid-overlap", 442, "match-grid-overlap");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{expected.SessionId}/matches/{matchId.Value}/view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var geometryBuilder = new BonesBoardChainGeometryBuilder();
        var layout = geometryBuilder.AssignFineGridCells(expected.BoardLayout);
        AssertNoOverlappingFineCells(layout.Tiles);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var domTiles = document.GetElementById("board-chain")!
            .QuerySelector(".board-chain-inner")!
            .QuerySelectorAll(".board-chain-tile");
        Assert.Equal(layout.Tiles.Count, domTiles.Length);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.StandardTileStyling)]
    public async Task BonesViewerPage_GivenBoardTile_ExpectedSeatColorOnDividerOrCenterDisc()
    {
        var (_, matchId, expected) = await RegisterSeededMatchAsync("session-tile-style", 442, "match-tile-style");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{expected.SessionId}/matches/{matchId.Value}/view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardTile = document.GetElementById("board-chain")!.QuerySelector(".board-chain-tile");
        Assert.NotNull(boardTile);
        Assert.Equal("divider", boardTile!.GetAttribute("data-seat-marker-mode"));
        var divider = boardTile.QuerySelector(".domino-divider");
        Assert.NotNull(divider);
        Assert.False(string.IsNullOrWhiteSpace(divider!.GetAttribute("data-seat-color")));

        var handTile = document.QuerySelector(".hand-tiles .domino-tile");
        Assert.NotNull(handTile);
        Assert.Equal("divider", handTile!.GetAttribute("data-seat-marker-mode"));
        Assert.NotNull(handTile.QuerySelector(".domino-divider"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.StandardTileStyling)]
    public async Task BonesViewerPage_GivenBoardTile_ExpectedFacingPipsMatchDataAttributes()
    {
        var (_, matchId, expected) = await RegisterSeededMatchAsync("session-facing-pips", 123, "match-facing-pips");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{expected.SessionId}/matches/{matchId.Value}/view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardTiles = document.GetElementById("board-chain")!.QuerySelectorAll(".board-chain-tile");

        foreach (var placement in expected.BoardLayout.Tiles)
        {
            var element = boardTiles.First(tile => tile.GetAttribute("data-grid-x") == placement.GridX.ToString());
            Assert.Equal(placement.FacingLowPip.ToString(), element.GetAttribute("data-facing-low-pip"));
            Assert.Equal(placement.FacingHighPip.ToString(), element.GetAttribute("data-facing-high-pip"));
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.StandardTileStyling)]
    public async Task BonesViewerScript_GivenRenderBoardTile_ExpectedDividerFallbackWhenConfigured()
    {
        using var client = _factory.CreateClient();
        var scriptResponse = await client.GetAsync("/viewer/viewer.js");
        var script = await scriptResponse.Content.ReadAsStringAsync();

        Assert.Contains("domino-divider", script, StringComparison.Ordinal);
        Assert.Contains("seatMarkerMode", script, StringComparison.Ordinal);
        Assert.Contains("useDividerFallback", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SsrGridScaleMarkup)]
    public async Task BonesViewerPage_GivenBoardChain_ExpectedExtentAndScaleAttributesOnBoardChain()
    {
        var (_, matchId, expected) = await RegisterSeededMatchAsync("session-ssr-scale", 442, "match-ssr-scale");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{expected.SessionId}/matches/{matchId.Value}/view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardChain = document.GetElementById("board-chain");
        Assert.NotNull(boardChain);
        Assert.Equal(expected.BoardLayout.MinGridX.ToString(), boardChain!.GetAttribute("data-min-grid-x"));
        Assert.Equal(expected.BoardLayout.ColumnCount.ToString(), boardChain.GetAttribute("data-column-count"));
        Assert.False(string.IsNullOrWhiteSpace(boardChain.GetAttribute("data-board-chain-scale")));
        Assert.Contains("--board-chain-scale", boardChain.GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SsrRoundCompleteChrome)]
    public async Task BonesViewerPage_GivenCompletedMatchMidTurn_ExpectedWinnerHiddenAtEarlyTurnIndex()
    {
        const string sessionId = "session-ssr-winner";
        const int seed = 442;
        var matchId = new BonesGameId("match-ssr-winner");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view?turnIndex=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var winner = document.GetElementById("winner");
        Assert.NotNull(winner);
        Assert.Contains("hidden", winner!.ClassList);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SpaReplayGridScale)]
    public async Task BonesViewerScript_GivenRenderFrame_ExpectedUpdatesWinnerVisibilityFromFrameCompletion()
    {
        using var client = _factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("renderFrameChrome", script, StringComparison.Ordinal);
        Assert.Contains("isRoundComplete", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SpaReplayGridScale)]
    public async Task BonesViewerScript_GivenRenderBoardChain_ExpectedAppliesGridPlacementAndScale()
    {
        using var client = _factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("applyBoardChainLayout", script, StringComparison.Ordinal);
        Assert.Contains("--board-chain-scale", script, StringComparison.Ordinal);
        Assert.Contains("--tile-grid-column", script, StringComparison.Ordinal);
        Assert.Contains("resize", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SpaReplayGridScale)]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesViewerPage_GivenReplayScrubTurnIndex_ExpectedBoardTilePipsMatchFrameAtTurn()
    {
        const string sessionId = "session-scrub-pips";
        const int seed = 123;
        var matchId = new BonesGameId("match-scrub-pips");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var frames = viewer.BuildMatchTimeline(registered);
        var targetFrame = frames[frames.Count / 2];

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view?turnIndex={targetFrame.TurnIndex}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardTiles = document.GetElementById("board-chain")!.QuerySelectorAll(".board-chain-tile");
        Assert.Equal(targetFrame.BoardLayout.Tiles.Count, boardTiles.Length);

        foreach (var placement in targetFrame.BoardLayout.Tiles)
        {
            var element = boardTiles.First(tile => tile.GetAttribute("data-grid-x") == placement.GridX.ToString());
            Assert.Equal(placement.FacingLowPip.ToString(), element.GetAttribute("data-facing-low-pip"));
            Assert.Equal(placement.FacingHighPip.ToString(), element.GetAttribute("data-facing-high-pip"));
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SpaReplayGridScale)]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesViewerPage_GivenReplayScrubTurnIndex_ExpectedHandTilesMatchFrameAtTurn()
    {
        const string sessionId = "session-scrub-hands";
        const int seed = 442;
        var matchId = new BonesGameId("match-scrub-hands");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var frames = viewer.BuildMatchTimeline(registered);
        var targetFrame = frames[frames.Count / 2];

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view?turnIndex={targetFrame.TurnIndex}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());

        foreach (var (seat, expectedTiles) in targetFrame.HandTilesBySeat)
        {
            var handElement = document.QuerySelector($".hand[data-seat=\"{seat}\"]");
            Assert.NotNull(handElement);

            var handTiles = handElement!.QuerySelectorAll(".hand-tiles .domino-tile").ToArray();
            Assert.Equal(expectedTiles.Count, handTiles.Length);

            for (var index = 0; index < expectedTiles.Count; index++)
            {
                var expected = expectedTiles[index];
                var element = handTiles[index];
                Assert.Equal(expected.LowPip.ToString(), element.GetAttribute("data-low-pip"));
                Assert.Equal(expected.HighPip.ToString(), element.GetAttribute("data-high-pip"));
                Assert.Equal(seat.ToString(), element.GetAttribute("data-owner-seat"));
            }
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SpaReplayGridScale)]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesViewerPage_GivenReplayScrubTurnIndex_ExpectedOpenEndsMatchFrame()
    {
        const string sessionId = "session-scrub-ends";
        const int seed = 123;
        var matchId = new BonesGameId("match-scrub-ends");
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var frames = viewer.BuildMatchTimeline(registered);
        var targetFrame = frames.First(frame => frame.BoardLayout.Tiles.Count >= 2);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/view?turnIndex={targetFrame.TurnIndex}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var leftEnd = document.GetElementById("left-end-pip");
        var rightEnd = document.GetElementById("right-end-pip");

        Assert.NotNull(leftEnd);
        Assert.NotNull(rightEnd);
        Assert.Equal(targetFrame.LeftEndPip?.ToString(), leftEnd!.GetAttribute("data-pip"));
        Assert.Equal(targetFrame.RightEndPip?.ToString(), rightEnd!.GetAttribute("data-pip"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerReplayScalingRequirementsChecklistItems.SpaReplayGridScale)]
    public async Task BonesViewerPage_GivenLongChain_ExpectedBoardChainScaleAttributeBelowOne()
    {
        var layout = new BonesBoardVisualLayout
        {
            Tiles = Enumerable.Range(0, 8).Select(CreatePlacement).ToArray(),
            MinGridX = 0,
            MaxGridX = 7,
            MinGridY = 0,
            MaxGridY = 0,
            ColumnCount = 8,
            RowCount = 1,
        };

        var scale = BonesBoardChainScaleCalculator.ComputeScale(layout, containerWidth: 200, containerHeight: 160);
        Assert.True(scale < 1.0);

        var html = BonesViewerPageRenderer.Render(
            new BonesMatchViewModel
            {
                SessionId = "session-scale-dom",
                MatchId = "match-scale-dom",
                LeftEndPip = 1,
                RightEndPip = 2,
                HandTileCountsBySeat = new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1, [4] = 1 },
                BoardLayout = layout,
                HandTilesBySeat = new Dictionary<int, IReadOnlyList<BonesHandTileView>>(),
                PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
                ActiveSeat = 1,
                CumulativeScoresBySeat = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0 },
                FrameCount = 1,
                Revision = 1,
                IsComplete = false,
            },
            null,
            null);

        var document = await ParseHtmlAsync(html);
        var boardChain = document.GetElementById("board-chain");
        Assert.NotNull(boardChain);
        var renderedScale = double.Parse(boardChain!.GetAttribute("data-board-chain-scale")!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(renderedScale < 1.0);
    }

    private async Task<(string SessionId, BonesGameId MatchId, BonesMatchViewModel Expected)> RegisterSeededMatchAsync(
        string sessionId,
        int seed,
        string matchValue)
    {
        var matchId = new BonesGameId(matchValue);
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var expected = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        return (sessionId, matchId, expected);
    }

    private static BonesMatchViewModel CreateSnapshot(BonesBoardVisualLayout layout) => new()
    {
        SessionId = "session-grid-branch",
        MatchId = "match-grid-branch",
        LeftEndPip = 1,
        RightEndPip = 3,
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

    private static BonesBoardTilePlacement CreatePlacement(int gridX) => new()
    {
        FacingLowPip = 1,
        FacingHighPip = 2,
        ChainIndex = gridX,
        LowPip = 1,
        HighPip = 2,
        IsDouble = false,
        Orientation = BonesTileOrientation.Horizontal,
        GridX = gridX,
        GridY = 0,
        PlayedBySeat = 1,
    };

    private static void AssertNoOverlappingFineCells(IReadOnlyList<BonesBoardTilePlacement> tiles)
    {
        for (var leftIndex = 0; leftIndex < tiles.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < tiles.Count; rightIndex++)
            {
                var left = tiles[leftIndex];
                var right = tiles[rightIndex];
                var columnOverlap = left.FineColumnStart < right.FineColumnEnd && right.FineColumnStart < left.FineColumnEnd;
                var rowOverlap = left.FineRowStart < right.FineRowEnd && right.FineRowStart < left.FineRowEnd;
                Assert.False(
                    columnOverlap && rowOverlap,
                    $"Tiles at chain index {leftIndex} and {rightIndex} overlap in fine-grid cells.");
            }
        }
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
