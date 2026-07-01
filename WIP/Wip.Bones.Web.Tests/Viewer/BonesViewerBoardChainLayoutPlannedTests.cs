using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("BonesViewerPlaywright")]
public sealed class BonesViewerBoardChainLayoutPlannedTests : IClassFixture<BonesPlaywrightHost>
{
    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerBoardChainLayoutPlannedTests(BonesPlaywrightHost playwrightHost)
    {
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ReplayJsOnView)]
    public async Task BonesViewerScript_GivenViewRouteWithoutQueryParams_ExpectedReadsSessionFromDataAttributes()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("root.dataset.sessionId", script, StringComparison.Ordinal);
        Assert.Contains("root.dataset.matchId", script, StringComparison.Ordinal);
        Assert.Contains("resolveIdsFromPath", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.ReplayJsOnView)]
    public async Task BonesViewerPage_GivenViewRoute_ExpectedScrubberChangesBoardTileCount()
    {
        const string sessionId = "session-scrubber-board-count";
        const int seed = 442;
        var matchId = new BonesGameId("match-scrubber-board-count");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var targetTurn = Math.Max(0, snapshot.FrameCount / 2);
        var expectedFrame = viewer.BuildMatchTimeline(registered)[targetTurn];

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

            Assert.True(
                await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn),
                "Scrubber must fetch frame and update turn chrome.");

            var boardTileCount = await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page);
            Assert.Equal(expectedFrame.BoardLayout.Tiles.Count, boardTileCount);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerBoardChainLayoutRequirementsChecklistItems.TransformCssInnerWrapper)]
    public async Task BonesViewerPage_GivenLongChain_ExpectedTransformScaleOnInnerWrapper()
    {
        const string sessionId = "session-inner-transform";
        var matchId = new BonesGameId("match-inner-transform");
        RegisterLongChainMatch(sessionId, matchId);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);

            var metrics = await page.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
                const boardChain = document.getElementById('board-chain');
                const inner = boardChain ? boardChain.querySelector('.board-chain-inner') : null;
                const innerStyle = inner ? getComputedStyle(inner) : null;
                const scale = Number((boardChain && boardChain.dataset && boardChain.dataset.boardChainScale) || 1);
                const tileCount = inner ? inner.querySelectorAll('.board-chain-tile').length : 0;
                return {
                    tileCount: tileCount,
                    scale: scale,
                    transform: innerStyle ? innerStyle.transform : '',
                };
            }");

            Assert.True(metrics.GetProperty("tileCount").GetInt32() >= 12);
            Assert.True(metrics.GetProperty("scale").GetDouble() < 1.0);
            var transform = metrics.GetProperty("transform").GetString() ?? string.Empty;
            Assert.True(
                transform.Contains("matrix", StringComparison.OrdinalIgnoreCase)
                || transform.Contains("scale", StringComparison.OrdinalIgnoreCase));
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

    private void RegisterLongChainMatch(string sessionId, BonesGameId matchId)
    {
        var (seed, matchResult) = CreateLongChainSeededMatch(matchId, minimumTileCount: 12);
        _playwrightHost.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));
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
}