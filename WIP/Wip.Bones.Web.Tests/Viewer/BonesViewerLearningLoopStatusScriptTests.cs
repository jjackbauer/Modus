using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public sealed class BonesViewerLearningLoopStatusScriptTests : IClassFixture<BonesPlaywrightHost>
{
    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerLearningLoopStatusScriptTests(BonesPlaywrightHost playwrightHost)
    {
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.PollLearningLoopStatus)]
    public async Task BonesViewerLearningLoopStatus_GivenStatusEndpointReturnsJson_ExpectedPanelPopulatesDataAttributes()
    {
        const string sessionId = "session-learning-loop-status-populate";
        var matchId = new BonesGameId("match-learning-loop-status-populate");
        RegisterSeededMatch(sessionId, matchId, 442);

        var statusPayload = CreateStatusPayload(
            iterationCount: 3,
            currentStage: "Play",
            gamesSimulated: 12,
            maxGamesPerRun: 50);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, statusPayload);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.False(dom.PanelHidden);
            Assert.Equal("3", dom.IterationCount);
            Assert.Equal("Play", dom.CurrentStage);
            Assert.Equal("12", dom.GamesSimulated);
            Assert.Equal("12 / 50", dom.GameBudgetText);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.PollLearningLoopStatus)]
    public async Task BonesViewerLearningLoopStatus_GivenStatusEndpointReturns404_ExpectedPanelHiddenAndNoUncaughtError()
    {
        const string sessionId = "session-learning-loop-status-404";
        var matchId = new BonesGameId("match-learning-loop-status-404");
        RegisterSeededMatch(sessionId, matchId, 9001);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, statusPayload: null, returnNotFound: true);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);

            await page.WaitForFunctionAsync(
                @"() => document.getElementById('learning-loop-status')?.classList.contains('hidden')",
                new PageWaitForFunctionOptions { Timeout = 10_000 });

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.True(dom.PanelHidden);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(
                page,
                await ResolveLatestTurnAsync(page));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.RenderLearningLoopStatus)]
    public async Task BonesViewerLearningLoopStatus_GivenMaxGamesPerRunZero_ExpectedBudgetShowsSimulatedCountOnly()
    {
        const string sessionId = "session-learning-loop-status-budget";
        var matchId = new BonesGameId("match-learning-loop-status-budget");
        RegisterSeededMatch(sessionId, matchId, 442);

        var statusPayload = CreateStatusPayload(gamesSimulated: 7, maxGamesPerRun: 0);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, statusPayload);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.Equal("7", dom.GameBudgetText);
            Assert.DoesNotContain("/0", dom.GameBudgetText, StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.WireStatusPoll)]
    public async Task BonesViewerScript_GivenInitViewer_ExpectedStatusPollOnSeparateIntervalFromLiveUpdates()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("window.setInterval(pollLiveUpdates, LIVE_POLL_INTERVAL_MS)", script, StringComparison.Ordinal);
        Assert.Contains("window.setInterval(pollLearningLoopStatus, STATUS_POLL_INTERVAL_MS)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("await pollLearningLoopStatus", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.WireStatusPoll)]
    public async Task BonesViewerLearningLoopStatus_GivenStatusPollWhileMatchLoads_ExpectedSnapshotBootstrapCompletes()
    {
        const string sessionId = "session-learning-loop-status-parallel";
        var matchId = new BonesGameId("match-learning-loop-status-parallel");
        RegisterSeededMatch(sessionId, matchId, 442);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, CreateStatusPayload(currentStage: "Observe"));
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var latestTurn = await ResolveLatestTurnAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, latestTurn);
            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.Equal("Observe", dom.CurrentStage);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.MatchTurnLine)]
    public async Task BonesViewerLearningLoopStatus_GivenStatusPollWhileScrubberActive_ExpectedMatchTurnLineUsesScrubberIndex()
    {
        const string sessionId = "session-learning-loop-status-scrubber";
        var matchId = new BonesGameId("match-learning-loop-status-scrubber");
        RegisterSeededMatch(sessionId, matchId, 9001);

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
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, CreateStatusPayload());
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, snapshot.FrameCount - 1);

            await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.Equal(targetTurn.ToString(CultureInfo.InvariantCulture), dom.TurnIndex);
            Assert.Equal(snapshot.FrameCount.ToString(CultureInfo.InvariantCulture), dom.FrameCount);
            Assert.Equal("Turn " + targetTurn + " of " + snapshot.FrameCount, dom.MatchTurnsText);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.MatchTurnLine)]
    public async Task BonesViewerLearningLoopStatus_GivenSnapshotFrameCountIncrease_ExpectedTurnOfYUpdates()
    {
        const string sessionId = "session-learning-loop-status-frame-growth";
        var matchId = new BonesGameId("match-learning-loop-status-frame-growth");
        RegisterSeededMatch(sessionId, matchId, 442);

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
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, CreateStatusPayload());
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, snapshot.FrameCount - 1);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.Equal(snapshot.FrameCount.ToString(CultureInfo.InvariantCulture), dom.FrameCount);
            Assert.Equal(
                "Turn " + snapshot.FrameCount + " of " + snapshot.FrameCount,
                dom.MatchTurnsText);

            using var factory = new BonesWebApplicationFactory();
            using var client = factory.CreateClient();
            var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();
            Assert.Contains(
                "renderLearningLoopStatus(lastLearningLoopStatus, model, turnIndex)",
                script,
                StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.PendingErrorDisplay)]
    public async Task BonesViewerLearningLoopStatus_GivenLearningPlayerPending_ExpectedPendingLabelWithoutEffectivenessBlock()
    {
        const string sessionId = "session-learning-loop-status-pending";
        var matchId = new BonesGameId("match-learning-loop-status-pending");
        RegisterSeededMatch(sessionId, matchId, 442);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(
                page,
                CreateStatusPayload(learningPlayerStatus: "pending"));
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.False(dom.PonderPendingHidden);
            Assert.True(dom.LearningPlayerHidden);
            Assert.DoesNotContain("W/L", dom.PageHtml, StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.PendingErrorDisplay)]
    public async Task BonesViewerLearningLoopStatus_GivenLastIterationError_ExpectedErrorMessageAndStageInErrorStyledNode()
    {
        const string sessionId = "session-learning-loop-status-iteration-error";
        var matchId = new BonesGameId("match-learning-loop-status-iteration-error");
        RegisterSeededMatch(sessionId, matchId, 442);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(
                page,
                CreateStatusPayload(lastIterationError: new { message = "Provider timeout", stage = "Ponder" }));
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.False(dom.LastIterationErrorHidden);
            Assert.Equal("Ponder", dom.FailureStage);
            Assert.Contains("Provider timeout", dom.LastIterationErrorText, StringComparison.Ordinal);

            var errorClass = await page.Locator("#loop-last-iteration-error").GetAttributeAsync("class");
            Assert.Contains("status-error", errorClass, StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.PendingErrorDisplay)]
    public async Task BonesViewerLearningLoopStatus_GivenLearningPlayerError_ExpectedErrorStyledMessage()
    {
        const string sessionId = "session-learning-loop-status-player-error";
        var matchId = new BonesGameId("match-learning-loop-status-player-error");
        RegisterSeededMatch(sessionId, matchId, 442);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(
                page,
                CreateStatusPayload(learningPlayerError: "Knowledge store unavailable"));
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.False(dom.LearningPlayerErrorHidden);
            Assert.Equal("Knowledge store unavailable", dom.LearningPlayerErrorText);

            var errorClass = await page.Locator("#loop-learning-player-error").GetAttributeAsync("class");
            Assert.Contains("status-error", errorClass, StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.RenderLearningLoopStatus)]
    public async Task BonesViewerLearningLoopStatus_GivenLastPromotionRejected_ExpectedPromotionOutcomeDisplayed()
    {
        const string sessionId = "session-learning-loop-status-promotion";
        var matchId = new BonesGameId("match-learning-loop-status-promotion");
        RegisterSeededMatch(sessionId, matchId, 442);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(
                page,
                CreateStatusPayload(lastPromotionOutcome: "Rejected"));
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.False(dom.PromotionHidden);
            Assert.Equal("Rejected", dom.PromotionText);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesWebHost_GivenStubStatusRouteAndRegisteredMatch_ExpectedViewerSidebarReflectsStubStatusAfterLoad()
    {
        const string sessionId = "session-learning-loop-status-host-stub";
        var matchId = new BonesGameId("match-learning-loop-status-host-stub");
        RegisterSeededMatch(sessionId, matchId, 442);

        var statusPayload = CreateStatusPayload(
            iterationCount: 5,
            currentStage: "Enhance",
            gamesSimulated: 18,
            maxGamesPerRun: 25,
            lastPromotionOutcome: "Promoted");

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, statusPayload);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.False(dom.PanelHidden);
            Assert.Equal("5", dom.IterationCount);
            Assert.Equal("Enhance", dom.CurrentStage);
            Assert.Equal("18", dom.GamesSimulated);
            Assert.Equal("18 / 25", dom.GameBudgetText);
            Assert.Equal("Promoted", dom.PromotionText);
            Assert.Equal("Iteration 5", await page.Locator("#loop-iteration-value").TextContentAsync());
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesWebHost_GivenStubStatusStagePlay_ExpectedDataCurrentStageAttributeMatchesJson()
    {
        const string sessionId = "session-learning-loop-status-host-play";
        var matchId = new BonesGameId("match-learning-loop-status-host-play");
        RegisterSeededMatch(sessionId, matchId, 9001);

        var statusPayload = CreateStatusPayload(currentStage: "Play", iterationCount: 2, gamesSimulated: 4);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, statusPayload);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.Equal("Play", dom.CurrentStage);
            Assert.Equal("Play", await page.Locator("#learning-loop-status").GetAttributeAsync("data-current-stage"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.HostLiveLoopProof)]
    public void BehaviorProofComplianceRegistry_GivenViewerLearningLoopStatusHostProof_ExpectedHostTestsExist()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var hostTestsPath = Path.Combine(
            repositoryRoot,
            "WIP",
            "Wip.Bones.Host.Tests",
            "BonesHostViewerLearningLoopStatusTests.cs");
        Assert.True(File.Exists(hostTestsPath));
        var content = File.ReadAllText(hostTestsPath);
        Assert.Contains(
            "BonesHostViewerLearningLoopStatus_GivenRunningLoop_ExpectedSidebarStageTracksStatusApi",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "BonesHostViewerLearningLoopStatus_GivenLiveMatchPublish_ExpectedTurnOfYIncreasesWithFrameCount",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.NoSecretLeakage)]
    public async Task BonesViewerLearningLoopStatus_GivenModelProviderDeepSeek_ExpectedProviderAndModelWithoutSecretFields()
    {
        const string sessionId = "session-learning-loop-status-secrets";
        var matchId = new BonesGameId("match-learning-loop-status-secrets");
        RegisterSeededMatch(sessionId, matchId, 442);

        var statusPayload = CreateStatusPayload(
            modelProvider: new
            {
                provider = "DeepSeek",
                model = "deepseek-chat",
                baseUrl = "https://api.deepseek.com",
                timeoutSeconds = 120,
                apiKeySource = "sk-live-secret-key-do-not-leak",
            });

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _playwrightHost.ListeningUri,
            sessionId,
            matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.RouteBonesStatusStubAsync(page, statusPayload);
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            Assert.Equal("DeepSeek", dom.Provider);
            Assert.Equal("deepseek-chat", dom.Model);
            Assert.Equal("DeepSeek / deepseek-chat", dom.ModelProviderText);
            Assert.DoesNotMatch(new Regex("sk-[A-Za-z0-9-]+", RegexOptions.CultureInvariant), dom.PageHtml);
            Assert.DoesNotContain("apiKeySource", dom.PageHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-live-secret-key-do-not-leak", dom.PageHtml, StringComparison.Ordinal);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static object CreateStatusPayload(
        bool isRunning = true,
        int iterationCount = 1,
        string currentStage = "Observe",
        int gamesSimulated = 1,
        int maxGamesPerRun = 10,
        string? learningPlayerStatus = null,
        string? learningPlayerError = null,
        object? lastIterationError = null,
        string? lastPromotionOutcome = null,
        object? modelProvider = null)
    {
        return new
        {
            isRunning,
            iterationCount,
            currentStage,
            failureStage = (string?)null,
            gamesSimulated,
            maxGamesPerRun,
            capReached = false,
            learningPlayer = (object?)null,
            learningPlayerStatus,
            learningPlayerError,
            lastIterationError,
            lastPromotionOutcome,
            libraryEffectiveness = (object?)null,
            modelProvider = modelProvider ?? new { provider = "Stub", model = "stub-model" },
        };
    }

    private static async Task<int> ResolveLatestTurnAsync(IPage page)
    {
        var frameCountText = await page.EvaluateAsync<string>(
            @"() => document.getElementById('bones-viewer')?.dataset?.frameCount ?? '0'");
        var frameCount = int.Parse(frameCountText, CultureInfo.InvariantCulture);
        return Math.Max(0, frameCount - 1);
    }

    private void RegisterSeededMatch(string sessionId, BonesGameId matchId, int seed)
    {
        _playwrightHost.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            CreateSeededMatch(matchId, seed, targetScore: 15)));
    }

    private static BonesMatchResult CreateSeededMatch(BonesGameId matchId, int seed, int targetScore)
    {
        var simulator = new BonesMatchSimulator();
        var config = new BonesMatchConfig(matchId, seed, targetScore, CreateFirstLegalMoveSlots());
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
