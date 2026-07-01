using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests.Viewer;

public sealed class BonesViewerDominoRenderingApiTests : IClassFixture<BonesWebApplicationFactory>
{
    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerDominoRenderingApiTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.JsonDomPipParity)]
    public async Task BonesViewerReplayScalingApiTests_GivenMidFrame_ExpectedBoardLayoutTilesCarryFacingAndCanonicalPips()
    {
        const string sessionId = "session-domino-board-frame-api";
        const int seed = 9001;
        var matchId = new BonesGameId("match-domino-board-frame-api");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var frames = viewer.BuildMatchTimeline(registered);
        var targetTurn = Math.Max(0, frames.Count / 2);
        var expectedFrame = frames[targetTurn];

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{targetTurn}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var frameApi = await response.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);
        Assert.NotNull(frameApi);
        Assert.Equal(expectedFrame.TurnIndex, frameApi!.TurnIndex);
        Assert.Equal(expectedFrame.BoardLayout.Tiles.Count, frameApi.BoardLayout.Tiles.Count);

        foreach (var expected in expectedFrame.BoardLayout.Tiles)
        {
            var actual = frameApi.BoardLayout.Tiles.Single(tile =>
                tile.GridX == expected.GridX && tile.GridY == expected.GridY);
            Assert.Equal(expected.FacingLowPip, actual.FacingLowPip);
            Assert.Equal(expected.FacingHighPip, actual.FacingHighPip);
            Assert.Equal(expected.LowPip, actual.LowPip);
            Assert.Equal(expected.HighPip, actual.HighPip);
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.HandPipParity)]
    public async Task BonesViewerReplayScalingApiTests_GivenMidFrame_ExpectedHandTilesCarryCanonicalPipsOnly()
    {
        const string sessionId = "session-domino-hand-frame-api";
        const int seed = 9001;
        var matchId = new BonesGameId("match-domino-hand-frame-api");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var frames = viewer.BuildMatchTimeline(registered);
        var targetTurn = Math.Max(0, frames.Count / 2);
        var expectedFrame = frames[targetTurn];

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{targetTurn}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var frameApi = await response.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);
        Assert.NotNull(frameApi);
        Assert.Equal(expectedFrame.TurnIndex, frameApi!.TurnIndex);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            Assert.True(expectedFrame.HandTilesBySeat.TryGetValue(seat, out var expectedTiles));
            Assert.True(frameApi.HandTilesBySeat.TryGetValue(seat, out var actualTiles));
            Assert.Equal(expectedTiles!.Count, actualTiles!.Count);

            for (var index = 0; index < expectedTiles.Count; index++)
            {
                Assert.Equal(expectedTiles[index].LowPip, actualTiles[index].LowPip);
                Assert.Equal(expectedTiles[index].HighPip, actualTiles[index].HighPip);
            }
        }
    }

    private void RegisterSeededMatch(string sessionId, BonesGameId matchId, int seed)
    {
        var matchResult = CreateSeededMatch(matchId, seed, targetScore: 15);
        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));
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