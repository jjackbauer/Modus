using System.Net;
using System.Net.Http.Json;
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

public sealed class BonesWebApplicationFactory : WebApplicationFactory<Program>
{
    public IBonesMatchCatalog Catalog { get; } = new InMemoryBonesMatchCatalog();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var catalogDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IBonesMatchCatalog));
            if (catalogDescriptor is not null)
                services.Remove(catalogDescriptor);

            services.AddSingleton<IBonesMatchCatalog>(Catalog);
        });
    }
}

public sealed class BonesMatchViewerServiceTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.ViewerApi;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchViewerService_GivenRoundState_ExpectedViewModelMatchesBoardHandsAndActiveSeat()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("viewer-round"), shuffleSeed: 442);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (engine.GetLegalMoves(state, state.CurrentPlayer).Count > 0 && state.EventLog.Length < 3)
        {
            var move = engine.GetLegalMoves(state, state.CurrentPlayer)[0];
            state = engine.ApplyMove(state, move);
        }

        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new BonesPlayerId(1)] = 12,
            [new BonesPlayerId(2)] = 4,
            [new BonesPlayerId(3)] = 0,
            [new BonesPlayerId(4)] = 8,
        };

        var snapshot = service.BuildSnapshot(state, cumulativeScores);

        Assert.Equal(state.Board.LeftEnd?.Pip.Value, snapshot.LeftEndPip);
        Assert.Equal(state.Board.RightEnd?.Pip.Value, snapshot.RightEndPip);
        Assert.Equal(state.CurrentPlayer.Seat, snapshot.ActiveSeat);
        Assert.Equal(state.EventLog.Length, snapshot.FrameCount);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            Assert.Equal(
                state.Hands.GetHand(playerId).Tiles.Length,
                snapshot.HandTileCountsBySeat[seat]);
            Assert.Equal(cumulativeScores[playerId], snapshot.CumulativeScoresBySeat[seat]);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchViewerService_GivenEventLog_ExpectedTimelineFrameCountMatchesTurnIndices()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("viewer-timeline"), shuffleSeed: 9001);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (!engine.IsRoundComplete(state))
        {
            var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
            var move = legalMoves.Count == 0
                ? BonesMove.Pass(new BonesMoveId($"pass-{state.EventLog.Length}"), state.CurrentPlayer)
                : legalMoves[0];
            state = engine.ApplyMove(state, move);
        }

        var events = state.EventLog;
        var frames = service.BuildTimeline(events);

        Assert.Equal(events.Length, frames.Count);

        for (var index = 0; index < events.Length; index++)
        {
            Assert.Equal(events[index].TurnIndex, frames[index].TurnIndex);
            Assert.Equal(events[index].Kind, frames[index].EventKind);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildMatchTimeline_GivenInProgressSecondRound_ExpectedLastFrameMatchesEngineState()
    {
        const int seed = -940591841;
        var matchId = new BonesGameId("learning-in-progress-r2");
        var engine = new BonesGameEngine();
        var round1Config = new BonesRoundConfig(
            new BonesGameId($"{matchId.Value}-r1"),
            HashCode.Combine(seed, 1));

        var state = engine.StartRound(round1Config);
        while (!engine.IsRoundComplete(state))
        {
            var move = engine.GetLegalMoves(state, state.CurrentPlayer)[0];
            state = engine.ApplyMove(state, move);
        }

        var round1Events = state.EventLog.ToArray();
        var round1Score = engine.ScoreRound(state);
        var rounds = new List<BonesMatchRoundRecord>
        {
            new(1, round1Score, round1Events),
        };

        var transcript = new List<BonesEvent>(round1Events);
        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [round1Score.Winner] = round1Score.Points,
        };
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (!cumulativeScores.ContainsKey(playerId))
                cumulativeScores[playerId] = 0;
        }

        var round2Config = new BonesRoundConfig(
            new BonesGameId($"{matchId.Value}-r2"),
            HashCode.Combine(seed, 2));
        state = engine.StartRound(round2Config);
        const int round2Moves = 5;
        for (var moveIndex = 0; moveIndex < round2Moves && !engine.IsRoundComplete(state); moveIndex++)
        {
            var move = engine.GetLegalMoves(state, state.CurrentPlayer)[0];
            state = engine.ApplyMove(state, move);
        }

        var turnOffset = transcript.Count;
        foreach (var roundEvent in state.EventLog)
        {
            transcript.Add(new BonesEvent(
                turnOffset + roundEvent.TurnIndex,
                roundEvent.PlayerId,
                roundEvent.Kind,
                roundEvent.Tile,
                roundEvent.Side));
        }

        var matchResult = new BonesMatchResult(
            matchId,
            round1Score.Winner,
            cumulativeScores,
            rounds,
            transcript);

        var registeredMatch = new BonesRegisteredMatch(
            new SessionId("session-in-progress-r2"),
            matchId,
            seed,
            matchResult,
            IsComplete: false);

        var viewer = new BonesMatchViewerService(engine);
        var frames = viewer.BuildMatchTimeline(registeredMatch);
        var lastFrame = frames[^1];

        Assert.Equal(state.Board.LeftEnd?.Pip.Value, lastFrame.LeftEndPip);
        Assert.Equal(state.Board.RightEnd?.Pip.Value, lastFrame.RightEndPip);
        Assert.Equal(state.CurrentPlayer.Seat, lastFrame.ActiveSeat);
        AssertUniqueTilesAcrossBoardAndHands(state);
        AssertFrameHandTilesMatchState(lastFrame, state);
    }

    private static void AssertUniqueTilesAcrossBoardAndHands(BonesRoundState state)
    {
        var tileCounts = new Dictionary<(int Low, int High), int>();
        foreach (var chainTile in state.Board.Tiles)
            AddTileCount(tileCounts, chainTile.Tile.LowPip.Value, chainTile.Tile.HighPip.Value);

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            foreach (var tile in state.Hands.GetHand(new BonesPlayerId(seat)).Tiles)
                AddTileCount(tileCounts, tile.LowPip.Value, tile.HighPip.Value);
        }

        Assert.Equal(28, tileCounts.Values.Sum());
        Assert.All(tileCounts.Values, count => Assert.Equal(1, count));
    }

    private static void AssertFrameHandTilesMatchState(BonesMatchFrame frame, BonesRoundState state)
    {
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            var expectedTiles = state.Hands.GetHand(playerId).Tiles;
            Assert.True(frame.HandTilesBySeat.TryGetValue(seat, out var actualTiles));
            Assert.Equal(expectedTiles.Length, actualTiles.Count);

            for (var index = 0; index < expectedTiles.Length; index++)
            {
                Assert.Equal(expectedTiles[index].LowPip.Value, actualTiles[index].LowPip);
                Assert.Equal(expectedTiles[index].HighPip.Value, actualTiles[index].HighPip);
            }
        }
    }

    private static void AddTileCount(Dictionary<(int Low, int High), int> tileCounts, int lowPip, int highPip)
    {
        var normalized = lowPip <= highPip ? (lowPip, highPip) : (highPip, lowPip);
        tileCounts.TryGetValue(normalized, out var count);
        tileCounts[normalized] = count + 1;
    }
}

public sealed class BonesWebHostIntegrationTests : IClassFixture<BonesWebApplicationFactory>
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.ViewerApi;

    private readonly BonesWebApplicationFactory _factory;

    public BonesWebHostIntegrationTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesWebHost_GivenMatchSnapshotRequest_ExpectedReturnsEngineEquivalentJson()
    {
        const string sessionId = "session-viewer-a";
        const int seed = 442;
        var matchId = new BonesGameId("match-viewer-a");
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

        var actual = await response.Content.ReadFromJsonAsync<BonesMatchViewModel>(
            BonesWebHost.JsonOptions);

        Assert.NotNull(actual);
        Assert.Equal(expected.SessionId, actual!.SessionId);
        Assert.Equal(expected.MatchId, actual.MatchId);
        Assert.Equal(expected.LeftEndPip, actual.LeftEndPip);
        Assert.Equal(expected.RightEndPip, actual.RightEndPip);
        Assert.Equal(expected.ActiveSeat, actual.ActiveSeat);
        Assert.Equal(expected.FrameCount, actual.FrameCount);
        Assert.Equal(expected.WinnerSeat, actual.WinnerSeat);
        Assert.Equal(expected.IsComplete, actual.IsComplete);
        Assert.Equal(expected.HandTileCountsBySeat, actual.HandTileCountsBySeat);
        Assert.Equal(expected.CumulativeScoresBySeat, actual.CumulativeScoresBySeat);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesWebHost_GivenForeignSessionMatch_ExpectedDeterministicNotFoundWithoutLeakage()
    {
        const string ownerSessionId = "session-owner";
        const string foreignSessionId = "session-foreign";
        var matchId = new BonesGameId("match-isolated");
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
        Assert.DoesNotContain(ownerSessionId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(matchResult.Winner.Seat.ToString(), body, StringComparison.Ordinal);
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
