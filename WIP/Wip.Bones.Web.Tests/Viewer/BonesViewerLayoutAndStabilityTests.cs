using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("BonesViewerPlaywright")]
public sealed class BonesViewerLayoutAndStabilityTests : IClassFixture<BonesWebApplicationFactory>, IClassFixture<BonesPlaywrightHost>
{
    private readonly BonesWebApplicationFactory _factory;
    private readonly BonesPlaywrightHost _playwrightHost;

    public BonesViewerLayoutAndStabilityTests(BonesWebApplicationFactory factory, BonesPlaywrightHost playwrightHost)
    {
        _factory = factory;
        _playwrightHost = playwrightHost;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.MatchRevisionType)]
    public void ImmutableBonesMatchCatalog_GivenRepeatedRegisterSameMatch_ExpectedRevisionIncreasesMonotonically()
    {
        var catalog = new InMemoryBonesMatchCatalog();
        const string sessionId = "session-revision";
        var matchId = new BonesGameId("match-revision");
        var matchResult = CreateSeededMatch(matchId, 442, 15);
        catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, 442, matchResult));
        Assert.True(catalog.TryGet(new SessionId(sessionId), matchId, out var first));
        Assert.Equal(1, first.Revision.Value);
        catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, 442, matchResult));
        Assert.True(catalog.TryGet(new SessionId(sessionId), matchId, out var second));
        Assert.Equal(2, second.Revision.Value);
        Assert.True(second.Revision.Value > first.Revision.Value);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ThreadSafeCatalog)]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.BehaviorProofTests)]
    public void ImmutableBonesMatchCatalog_GivenConcurrentRegisterAndTryGet_ExpectedNoExceptionsAndCompleteEntries()
    {
        var catalog = new InMemoryBonesMatchCatalog();
        const string sessionId = "session-concurrent";
        var matchId = new BonesGameId("match-concurrent");
        var matchResult = CreateSeededMatch(matchId, 9001, 15);
        var exceptions = new List<Exception>();
        Parallel.For(0, 40, index =>
        {
            try
            {
                catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, index, matchResult, index % 2 == 0));
                if (catalog.TryGet(new SessionId(sessionId), matchId, out var registered))
                {
                    Assert.True(registered.Revision.Value > 0);
                    Assert.True(registered.MatchResult.Transcript.Length > 0);
                }
            }
            catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
        });
        Assert.Empty(exceptions);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ImmutablePublish)]
    public void ImmutableBonesMatchCatalog_GivenRegisterThenMutateSourceTranscript_ExpectedCatalogEntryUnchanged()
    {
        var catalog = new InMemoryBonesMatchCatalog();
        var matchId = new BonesGameId("match-immutable");
        var events = BuildPartialEvents(matchId, maxEvents: 3);
        var matchResult = BuildMatchResult(matchId, events);
        catalog.Register(new BonesRegisteredMatch(new SessionId("session-immutable"), matchId, 442, matchResult));
        Assert.True(catalog.TryGet(new SessionId("session-immutable"), matchId, out var stored));
        var storedLength = stored.MatchResult.Transcript.Length;
        events.Add(new BonesEvent(events.Count, new BonesPlayerId(1), BonesEventKind.Pass, null, null));
        Assert.Equal(storedLength, stored.MatchResult.Transcript.Length);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ImmutablePublish)]
    public void BonesCatalogPublicMatchFeed_GivenPublishMatchState_ExpectedCatalogHoldsImmutableCopy()
    {
        var catalog = new InMemoryBonesMatchCatalog();
        var feed = new BonesCatalogPublicMatchFeed(catalog);
        var matchId = new BonesGameId("match-feed");
        var events = BuildPartialEvents(matchId, maxEvents: 2);
        var matchResult = BuildMatchResult(matchId, events);
        feed.PublishMatchState(new SessionId("session-feed"), matchId, 123, matchResult, false);
        var publishedLength = events.Count;
        events.Add(new BonesEvent(events.Count, new BonesPlayerId(1), BonesEventKind.Pass, null, null));
        Assert.True(catalog.TryGet(new SessionId("session-feed"), matchId, out var stored));
        Assert.Equal(publishedLength, stored.MatchResult.Transcript.Length);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ImmutablePublish)]
    public void BonesLearningViewerCoordinator_GivenPublishLiveMatchState_ExpectedRevisionExposedOnRegisteredMatch()
    {
        var catalog = new InMemoryBonesMatchCatalog();
        var feed = new BonesCatalogPublicMatchFeed(catalog);
        var coordinator = new BonesLearningViewerCoordinator(feed, new NoOpBonesViewerRunPublisher(), new Uri("http://127.0.0.1:5050"));
        var sessionId = new SessionId("session-coordinator");
        var matchId = BonesViewerUrlBuilder.CreateLearningMatchId(sessionId);
        var partial = CreateSeededMatch(matchId, 77, 15);
        coordinator.PublishLiveMatchState(sessionId, 77, partial, false);
        Assert.True(catalog.TryGet(sessionId, matchId, out var first));
        Assert.Equal(1, first.Revision.Value);
        coordinator.PublishLiveMatchState(sessionId, 77, partial, false);
        Assert.True(catalog.TryGet(sessionId, matchId, out var second));
        Assert.Equal(2, second.Revision.Value);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ExtendedViewModels)]
    public void BonesMatchViewerService_GivenRegisteredMatch_ExpectedSnapshotRevisionMatchesCatalogEntry()
    {
        var registered = RegisterAndGet("session-snapshot-revision", new BonesGameId("match-snapshot-revision"), 442);
        var snapshot = new BonesMatchViewerService().BuildMatchSnapshot(new SessionId("session-snapshot-revision"), registered);
        Assert.Equal(registered.Revision.Value, snapshot.Revision);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ExtendedViewModels)]
    public void BonesMatchViewerService_GivenRegisteredMatch_ExpectedFrameRevisionMatchesCatalogEntry()
    {
        var registered = RegisterAndGet("session-frame-revision", new BonesGameId("match-frame-revision"), 9001);
        var frames = new BonesMatchViewerService().BuildMatchTimeline(registered);
        Assert.NotEmpty(frames);
        Assert.All(frames, frame => Assert.Equal(registered.Revision.Value, frame.Revision));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ExtendedViewModels)]
    public void BonesMatchViewerService_GivenCapturedRevision_ExpectedTimelineLengthEqualsTranscriptLength()
    {
        var registered = RegisterAndGet("session-timeline-length", new BonesGameId("match-timeline-length"), 321);
        var frames = new BonesMatchViewerService().BuildMatchTimeline(registered);
        Assert.Equal(registered.MatchResult.Transcript.Length, frames.Count);
    }
    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.JsonRevisionFields)]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesWebHost_GivenMatchSnapshotRequest_ExpectedJsonIncludesRevisionField()
    {
        const string sessionId = "session-json-revision";
        var matchId = new BonesGameId("match-json-revision");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 442);
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("revision", out var revision));
        Assert.Equal(registered.Revision.Value, revision.GetInt64());
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.JsonRevisionFields)]
    public async Task BonesWebHost_GivenFrameRequest_ExpectedJsonIncludesRevisionMatchingSnapshot()
    {
        const string sessionId = "session-frame-json-revision";
        var matchId = new BonesGameId("match-frame-json-revision");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 9001);
        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var turnIndex = Math.Max(0, snapshot.FrameCount / 2);
        using var client = _factory.CreateClient();
        var frameResponse = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{turnIndex}");
        Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);
        using var json = JsonDocument.Parse(await frameResponse.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("revision", out var revision));
        Assert.Equal(snapshot.Revision, revision.GetInt64());
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.JsonRevisionFields)]
    public async Task BonesWebHost_GivenFrameTurnOutOfRange_ExpectedNotFoundWithoutPartialPayload()
    {
        const string sessionId = "session-frame-404";
        var matchId = new BonesGameId("match-frame-404");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 123);
        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/{snapshot.FrameCount + 5}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("boardLayout", body, StringComparison.Ordinal);
        Assert.DoesNotContain("handTilesBySeat", body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.CoherentHttpRead)]
    public async Task BonesWebHost_GivenConcurrentPublishDuringRequest_ExpectedSingleCatalogCapturePerRequest()
    {
        await using var countingFactory = new CountingCatalogWebApplicationFactory();
        const string sessionId = "session-single-tryget";
        var matchId = new BonesGameId("match-single-tryget");
        countingFactory.Catalog.Inner.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, 442, CreateSeededMatch(matchId, 442, 15)));
        using var client = countingFactory.CreateClient();
        countingFactory.Catalog.TryGetCount = 0;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}")).StatusCode);
        Assert.Equal(1, countingFactory.Catalog.TryGetCount);
        countingFactory.Catalog.TryGetCount = 0;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/frames/0")).StatusCode);
        Assert.Equal(1, countingFactory.Catalog.TryGetCount);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.SidebarShellLayout)]
    public async Task BonesViewerPage_GivenSnapshotPayload_ExpectedSidebarContainsSeatsAndScrubber()
    {
        const string sessionId = "session-sidebar";
        var matchId = new BonesGameId("match-sidebar");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 442);
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/view");
        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var sidebar = document.QuerySelector(".viewer-sidebar");
        Assert.NotNull(sidebar?.QuerySelector(".hands"));
        Assert.NotNull(sidebar?.QuerySelector("#replay-scrubber"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.SidebarShellLayout)]
    public async Task BonesViewerPage_GivenSnapshotPayload_ExpectedMainCanvasContainsBoardOnly()
    {
        const string sessionId = "session-main-canvas";
        var matchId = new BonesGameId("match-main-canvas");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 9001);
        using var client = _factory.CreateClient();
        var document = await ParseHtmlAsync(await (await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/view")).Content.ReadAsStringAsync());
        Assert.NotNull(document.QuerySelector(".viewer-main")?.QuerySelector("#bones-board"));
        Assert.Null(document.QuerySelector(".viewer-sidebar")?.QuerySelector("#bones-board"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.FullCanvasBoard)]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.BehaviorProofTests)]
    public async Task BonesViewerPage_GivenWideViewport_ExpectedMainCanvasWidthExceedsStackedLayoutBaseline()
    {
        const string sessionId = "session-wide-viewport";
        var matchId = new BonesGameId("match-wide-viewport");
        RegisterSeededMatch(_playwrightHost.Catalog, sessionId, matchId, 442);
        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(browser, _playwrightHost.ListeningUri, sessionId, matchId.Value);
        try
        {
            await page.SetViewportSizeAsync(1280, 720);
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            var mainWidth = await page.EvaluateAsync<double>("() => document.querySelector('.viewer-main')?.getBoundingClientRect().width ?? 0");
            Assert.True(mainWidth > 800, $"Expected .viewer-main width > 800 at 1280x720, got {mainWidth}.");
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.SidebarShellLayout)]
    public async Task BonesViewerPage_GivenStaticSpaAssets_ExpectedIndexHtmlMatchesSidebarShellStructure()
    {
        using var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/viewer/index.html")).Content.ReadAsStringAsync();
        Assert.Contains("viewer-sidebar", html, StringComparison.Ordinal);
        Assert.Contains("viewer-main", html, StringComparison.Ordinal);
        Assert.Contains("viewer-shell", html, StringComparison.Ordinal);
    }
    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.ViewerJsRelocation)]
    public async Task BonesViewerScript_GivenSidebarLayout_ExpectedScrubberStillDrivesFrameLoad()
    {
        const string sessionId = "session-sidebar-scrub";
        var matchId = new BonesGameId("match-sidebar-scrub");
        RegisterSeededMatch(_playwrightHost.Catalog, sessionId, matchId, 442);
        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var targetTurn = Math.Max(0, snapshot.FrameCount / 3);
        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(browser, _playwrightHost.ListeningUri, sessionId, matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.DispatchScrubberInputAsync(page, targetTurn);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
            Assert.True(await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page) > 0);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.SpaRevisionHandling)]
    public async Task BonesViewerScript_GivenFrame404OnScrub_ExpectedReloadsSnapshotAndRecoversBoard()
    {
        const string sessionId = "session-frame-404-recover";
        var matchId = new BonesGameId("match-frame-404-recover");
        RegisterSeededMatch(_playwrightHost.Catalog, sessionId, matchId, 9001);
        var viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_playwrightHost.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        var targetTurn = Math.Max(1, snapshot.FrameCount / 2);
        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(browser, _playwrightHost.ListeningUri, sessionId, matchId.Value);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, snapshot.FrameCount - 1);
            var failOnce = 1;
            await page.RouteAsync($"**/frames/{targetTurn}", async route =>
            {
                if (failOnce > 0) { failOnce--; await route.FulfillAsync(new RouteFulfillOptions { Status = 404 }); return; }
                await route.ContinueAsync();
            });
            await BonesViewerScriptTestDriver.DispatchScrubberInputAsync(page, targetTurn);
            await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
            Assert.True(await BonesViewerScriptTestDriver.GetBoardTileCountAsync(page) > 0);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.SpaRevisionHandling)]
    public async Task BonesViewerScript_GivenRevisionMismatch_ExpectedReloadSnapshotBeforeApplyingFrame()
    {
        using var client = _playwrightHost.CreateListeningClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();
        Assert.Contains("async function reloadSnapshot", script, StringComparison.Ordinal);
        Assert.Contains("frame.revision !== snapshotRevision", script, StringComparison.Ordinal);
        Assert.Contains("await reloadSnapshot(turnIndex)", script, StringComparison.Ordinal);
        Assert.Contains("async function pollLiveUpdates", script, StringComparison.Ordinal);
        Assert.Contains("async function switchToMatch", script, StringComparison.Ordinal);
        Assert.Contains("resolveLatestMatchIds", script, StringComparison.Ordinal);
        Assert.Contains("setInterval(pollLiveUpdates", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.FullCanvasBoard)]
    public async Task BonesViewerCss_GivenShellStyles_ExpectedNoCenteredMaxWidthCapOnViewer()
    {
        using var client = _factory.CreateClient();
        var css = await (await client.GetAsync("/viewer/viewer.css")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("max-width: 1080px", css, StringComparison.Ordinal);
        Assert.Contains(".viewer-main", css, StringComparison.Ordinal);
        Assert.Contains(".viewer-sidebar", css, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLayoutAndStabilityRequirementsChecklistItems.HostLiveViewProof)]
    public void BehaviorProofComplianceRegistry_GivenViewerLayoutHostProof_ExpectedHostFeedTestsExist()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var hostTestsPath = Path.Combine(repositoryRoot, "WIP", "Wip.Bones.Host.Tests", "BonesHostFeedTests.cs");
        Assert.True(File.Exists(hostTestsPath));
        var content = File.ReadAllText(hostTestsPath);
        Assert.Contains("BonesHostFeed_GivenRunningLoopAndOpenView_ExpectedScrubMidMatchReturnsCoherentFrames", content, StringComparison.Ordinal);
        Assert.Contains("BonesHostFeed_GivenLivePublishAdvance_ExpectedSnapshotRevisionIncreasesWithout500", content, StringComparison.Ordinal);
    }

    private static BonesRegisteredMatch RegisterAndGet(string sessionId, BonesGameId matchId, int seed)
    {
        var catalog = new InMemoryBonesMatchCatalog();
        catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, seed, CreateSeededMatch(matchId, seed, 15)));
        Assert.True(catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        return registered;
    }

    private static void RegisterSeededMatch(IBonesMatchCatalog catalog, string sessionId, BonesGameId matchId, int seed)
        => catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, seed, CreateSeededMatch(matchId, seed, 15)));

    private static List<BonesEvent> BuildPartialEvents(BonesGameId matchId, int maxEvents)
    {
        var events = new List<BonesEvent>();
        var engine = new BonesGameEngine();
        var state = engine.StartRound(new BonesRoundConfig(matchId, 442));
        while (engine.GetLegalMoves(state, state.CurrentPlayer).Count > 0 && events.Count < maxEvents)
        {
            state = engine.ApplyMove(state, engine.GetLegalMoves(state, state.CurrentPlayer)[0]);
            events.Add(state.EventLog[^1]);
        }
        return events;
    }

    private static BonesMatchResult BuildMatchResult(BonesGameId matchId, List<BonesEvent> events)
    {
        var scores = new Dictionary<BonesPlayerId, int>
        {
            [new BonesPlayerId(1)] = 0, [new BonesPlayerId(2)] = 0, [new BonesPlayerId(3)] = 0, [new BonesPlayerId(4)] = 0,
        };
        return new BonesMatchResult(matchId, new BonesPlayerId(1), scores, [], events.ToImmutableArray());
    }

    private static async Task<IDocument> ParseHtmlAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(request => request.Content(html));
    }

    private static BonesMatchResult CreateSeededMatch(BonesGameId matchId, int seed, int targetScore)
    {
        var simulator = new BonesMatchSimulator();
        return simulator.RunMatch(new BonesMatchConfig(matchId, seed, targetScore, CreateFirstLegalMoveSlots()));
    }

    private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreateFirstLegalMoveSlots()
    {
        var slot = new BonesFirstLegalMovePlayerSlot();
        return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
        {
            [new BonesPlayerId(1)] = slot, [new BonesPlayerId(2)] = slot, [new BonesPlayerId(3)] = slot, [new BonesPlayerId(4)] = slot,
        };
    }
}

internal sealed class CountingBonesMatchCatalog : IBonesMatchCatalog
{
    public CountingBonesMatchCatalog(IBonesMatchCatalog inner) => Inner = inner;
    public IBonesMatchCatalog Inner { get; }
    public int TryGetCount { get; set; }
    public void Register(BonesRegisteredMatch match) => Inner.Register(match);
    public bool TryGet(SessionId sessionId, BonesGameId matchId, out BonesRegisteredMatch match)
    {
        TryGetCount++;
        return Inner.TryGet(sessionId, matchId, out match!);
    }
}

internal sealed class CountingCatalogWebApplicationFactory : WebApplicationFactory<Program>
{
    public CountingBonesMatchCatalog Catalog { get; } = new(new InMemoryBonesMatchCatalog());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBonesMatchCatalog));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddSingleton<IBonesMatchCatalog>(Catalog);
        });
    }
}

internal sealed class NoOpBonesViewerRunPublisher : IBonesViewerRunPublisher
{
    public ValueTask PublishViewerUrlOnceAsync(SessionId sessionId, BonesGameId matchId, Uri viewerUrl, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}