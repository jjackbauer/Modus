using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerReplayScalingApiTests : IClassFixture<BonesWebApplicationFactory>
{
    private const string ChecklistItem =
        BonesViewerReplayScalingRequirementsChecklistItems.JsonFrameCompletionFields;

    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerReplayScalingApiTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesWebHost_GivenMidTurnFrameRequest_ExpectedIsRoundCompleteFalseInJson()
    {
        const string sessionId = "session-replay-mid-json";
        const int seed = 442;
        var matchId = new BonesGameId("match-replay-mid-json");
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
        var midFrame = frames[frames.Count / 2];
        Assert.False(midFrame.IsRoundComplete);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{midFrame.TurnIndex}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("isRoundComplete", out var isRoundComplete));
        Assert.False(isRoundComplete.GetBoolean());
        Assert.False(root.TryGetProperty("winnerSeat", out _));
        Assert.False(root.TryGetProperty("roundPipScore", out _));

        Assert.True(root.TryGetProperty("boardLayout", out var boardLayout));
        Assert.True(boardLayout.TryGetProperty("tiles", out var tiles));
        Assert.True(tiles.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("handTilesBySeat", out _));
        Assert.Equal(midFrame.TurnIndex, root.GetProperty("turnIndex").GetInt32());
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesWebHost_GivenFinalFrameRequest_ExpectedCompletionFieldsMatchEngine()
    {
        const string sessionId = "session-replay-final-json";
        const int seed = 9001;
        var matchId = new BonesGameId("match-replay-final-json");
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
        var finalFrame = frames[^1];
        Assert.True(finalFrame.IsRoundComplete);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{finalFrame.TurnIndex}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actual = await response.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);

        Assert.NotNull(actual);
        Assert.True(actual!.IsRoundComplete);
        Assert.Equal(finalFrame.WinnerSeat, actual.WinnerSeat);
        Assert.Equal(finalFrame.RoundPipScore, actual.RoundPipScore);
        Assert.Equal(finalFrame.BoardLayout.Tiles.Count, actual.BoardLayout.Tiles.Count);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesWebHost_GivenMidTurnFrameRequest_ExpectedBoardLayoutExtentMetadataPresent()
    {
        const string sessionId = "session-replay-extent-json";
        const int seed = 123;
        var matchId = new BonesGameId("match-replay-extent-json");
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
        var midFrame = frames.First(frame => frame.BoardLayout.Tiles.Count >= 2);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{midFrame.TurnIndex}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var layout = document.RootElement.GetProperty("boardLayout");

        Assert.Equal(midFrame.BoardLayout.MinGridX, layout.GetProperty("minGridX").GetInt32());
        Assert.Equal(midFrame.BoardLayout.MaxGridX, layout.GetProperty("maxGridX").GetInt32());
        Assert.Equal(midFrame.BoardLayout.MinGridY, layout.GetProperty("minGridY").GetInt32());
        Assert.Equal(midFrame.BoardLayout.MaxGridY, layout.GetProperty("maxGridY").GetInt32());
        Assert.Equal(midFrame.BoardLayout.ColumnCount, layout.GetProperty("columnCount").GetInt32());
        Assert.Equal(midFrame.BoardLayout.RowCount, layout.GetProperty("rowCount").GetInt32());
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
