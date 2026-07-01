using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Playwright;
using Wip.Bones.Web;
using Wip.Bones.Web.Tests;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Host.Tests;

[Collection("BonesViewerPlaywright")]
public sealed class BonesHostViewerLearningLoopStatusTests : IClassFixture<BonesHostPlaywrightApplication>
{
    private const string HostLiveLoopProofItem =
        BonesViewerLearningLoopStatusRequirementsChecklistItems.HostLiveLoopProof;

    private readonly BonesHostPlaywrightApplication _host;

    public BonesHostViewerLearningLoopStatusTests(BonesHostPlaywrightApplication host)
    {
        _host = host;
    }

    [Fact]
    [Trait("ChecklistItem", HostLiveLoopProofItem)]
    public async Task BonesHostViewerLearningLoopStatus_GivenRunningLoop_ExpectedSidebarStageTracksStatusApi()
    {
        using var client = _host.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl)
                && !string.IsNullOrWhiteSpace(response.CurrentStage)
                && response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        var viewerUri = new Uri(status.ViewerUrl!);
        var segments = viewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sessionId = segments[2];
        var matchId = segments[4];

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
            browser,
            _host.ListenUri,
            sessionId,
            matchId);
        try
        {
            await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
            await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

            var observedStages = new HashSet<string>(StringComparer.Ordinal);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                status = await WaitForSidebarStageToMatchStatusAsync(page, client, TimeSpan.FromSeconds(5));
                observedStages.Add(status.CurrentStage);

                var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
                Assert.False(dom.PanelHidden);
                Assert.Equal(status.CurrentStage, dom.CurrentStage);
                Assert.True(
                    long.TryParse(dom.IterationCount, out var domIteration),
                    "Sidebar iteration count must be numeric.");
                Assert.True(
                    domIteration >= 1,
                    "Sidebar iteration count must be at least 1.");

                if (observedStages.Count >= 2)
                    break;

                await Task.Delay(250);
            }

            Assert.True(
                observedStages.Count >= 2,
                $"Expected learning-loop stage to advance at least once. Observed stages: {string.Join(", ", observedStages)}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", HostLiveLoopProofItem)]
    public async Task BonesHostViewerLearningLoopStatus_GivenLiveMatchPublish_ExpectedTurnOfYIncreasesWithFrameCount()
    {
        var host = new BonesHostPlaywrightApplication();
        await host.InitializeAsync();
        try
        {
            using var client = host.CreateClient();

            var status = await BonesHostStatusPolling.WaitForStatusAsync(
                client,
                response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
                TimeSpan.FromSeconds(120));

            var viewerUri = new Uri(status.ViewerUrl!);
            var segments = viewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var sessionId = segments[2];
            var matchId = segments[4];

            var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
            var page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(
                browser,
                host.ListenUri,
                sessionId,
                matchId);
            try
            {
                await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);

                var baselineFrameCount = 0;
                var targetFrameCount = 0;
                int? previousSample = null;
                var trackedIteration = status.IterationCount;
                var growthViewerUrl = status.ViewerUrl!;
                var growthSessionId = sessionId;
                var growthMatchId = matchId;
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);

                while (DateTime.UtcNow < deadline)
                {
                    status = await BonesHostStatusClient.GetStatusAsync(client);
                    if (status.IterationCount != trackedIteration)
                    {
                        trackedIteration = status.IterationCount;
                        previousSample = null;
                    }
                    var activeViewerUri = string.IsNullOrWhiteSpace(status.ViewerUrl)
                        ? viewerUri
                        : new Uri(status.ViewerUrl!);
                    var activeSegments = activeViewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    sessionId = activeSegments[2];
                    matchId = activeSegments[4];

                    var snapshotResponse = await client.GetAsync(
                        $"/bones/sessions/{sessionId}/matches/{matchId}");
                    if (!snapshotResponse.IsSuccessStatusCode)
                    {
                        await Task.Delay(10);
                        continue;
                    }

                    var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<BonesMatchViewModel>(
                        BonesWebHost.JsonOptions);
                    if (snapshot is null || snapshot.FrameCount <= 0)
                    {
                        await Task.Delay(10);
                        continue;
                    }

                    if (previousSample.HasValue && snapshot.FrameCount > previousSample.Value)
                    {
                        baselineFrameCount = previousSample.Value;
                        targetFrameCount = snapshot.FrameCount;
                        growthViewerUrl = activeViewerUri.ToString();
                        growthSessionId = sessionId;
                        growthMatchId = matchId;
                        break;
                    }

                    previousSample = snapshot.FrameCount;
                    await Task.Delay(10);
                }

                Assert.True(
                    targetFrameCount > baselineFrameCount,
                    $"Expected snapshot frameCount to increase during live play (last sample={previousSample}).");

                if (!page.Url.Contains(growthMatchId, StringComparison.OrdinalIgnoreCase))
                {
                    await page.GotoAsync(
                        growthViewerUrl,
                        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                    await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
                }

                await BonesViewerScriptTestDriver.WaitForLearningLoopPanelPopulatedAsync(page);

                var growthSnapshotResponse = await client.GetAsync(
                    $"/bones/sessions/{growthSessionId}/matches/{growthMatchId}");
                growthSnapshotResponse.EnsureSuccessStatusCode();
                var growthSnapshot = await growthSnapshotResponse.Content.ReadFromJsonAsync<BonesMatchViewModel>(
                    BonesWebHost.JsonOptions)
                    ?? throw new InvalidOperationException("Growth match snapshot was null.");
                var assertedFrameCount = Math.Max(targetFrameCount, growthSnapshot.FrameCount);
                Assert.True(
                    assertedFrameCount > baselineFrameCount,
                    "Growth match snapshot should remain above the pre-growth baseline.");

                await BonesViewerScriptTestDriver.WaitForLearningLoopFrameCountAsync(page, assertedFrameCount);
                await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(
                    page,
                    Math.Max(0, assertedFrameCount - 1));

                var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
                Assert.Equal(
                    assertedFrameCount.ToString(CultureInfo.InvariantCulture),
                    dom.FrameCount);
                Assert.Contains(
                    " of " + assertedFrameCount.ToString(CultureInfo.InvariantCulture),
                    dom.MatchTurnsText ?? string.Empty,
                    StringComparison.Ordinal);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static async Task<BonesHostStatusResponse> WaitForSidebarStageToMatchStatusAsync(
        IPage page,
        HttpClient client,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        BonesHostStatusResponse? lastStatus = null;
        string? lastDomStage = null;

        while (DateTime.UtcNow < deadline)
        {
            lastStatus = await BonesHostStatusClient.GetStatusAsync(client);
            var dom = await BonesViewerScriptTestDriver.ReadLearningLoopStatusDomAsync(page);
            lastDomStage = dom.CurrentStage;

            if (string.Equals(lastStatus.CurrentStage, dom.CurrentStage, StringComparison.Ordinal))
                return lastStatus;

            await Task.Delay(100);
        }

        throw new InvalidOperationException(
            $"Timed out waiting for sidebar stage to match status API after {timeout}. "
            + $"Last API stage: {lastStatus?.CurrentStage ?? "<none>"}; last DOM stage: {lastDomStage ?? "<none>"}.");
    }
}
