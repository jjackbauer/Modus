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
public sealed class BonesViewerScrubberChromeSyncTests : IClassFixture<BonesPlaywrightHost>
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.ScrubberChromeSync;

    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerScrubberChromeSyncTests(BonesPlaywrightHost playwrightHost)
    {
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerScript_GivenUpdateTurnLabel_ExpectedSetsScrubberValue()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        var updateTurnLabelIndex = script.IndexOf("function updateTurnLabel", StringComparison.Ordinal);
        Assert.True(updateTurnLabelIndex >= 0);
        var updateTurnLabelBlock = script[updateTurnLabelIndex..];
        var blockEnd = updateTurnLabelBlock.IndexOf("\n  function ", StringComparison.Ordinal);
        if (blockEnd > 0)
        {
            updateTurnLabelBlock = updateTurnLabelBlock[..blockEnd];
        }

        Assert.Contains("scrubber.value = turnText", updateTurnLabelBlock, StringComparison.Ordinal);
        Assert.Contains("turnIndexOutput.dataset.turnIndex = turnText", updateTurnLabelBlock, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerScript_GivenScrubberInput_ExpectedTurnLabelMatchesScrubberValueImmediately()
    {
        const string sessionId = "session-scrubber-chrome-sync";
        const int seed = 442;
        var matchId = new BonesGameId("match-scrubber-chrome-sync");
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

            var immediateState = await BonesViewerScriptTestDriver.ReadScrubberChromeSyncAsync(page);
            BonesViewerScriptTestDriver.AssertScrubberChromeInSync(immediateState, targetTurn);

            frameGate.SetResult();
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);

            var afterFrameState = await BonesViewerScriptTestDriver.ReadScrubberChromeSyncAsync(page);
            BonesViewerScriptTestDriver.AssertScrubberChromeInSync(afterFrameState, targetTurn);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenBootstrapInitialTurn_ExpectedScrubberChromeSyncedAfterRenderFrame()
    {
        const string sessionId = "session-scrubber-bootstrap-sync";
        const int seed = 9001;
        var matchId = new BonesGameId("match-scrubber-bootstrap-sync");
        RegisterSeededMatch(sessionId, matchId, seed);

        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var initialTurn = Math.Max(0, snapshot.FrameCount / 4);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value,
            turnIndex: initialTurn);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, initialTurn);

            var state = await BonesViewerScriptTestDriver.ReadScrubberChromeSyncAsync(page);
            BonesViewerScriptTestDriver.AssertScrubberChromeInSync(state, initialTurn);
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
