using System.Net;
using System.Net.Http.Json;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostFeedTests
{
    private const string LiveMatchFeedItem = BonesHostRequirementsChecklistItems.LiveMatchFeed;
    private const string IntegrationTestsItem = BonesHostRequirementsChecklistItems.IntegrationTests;
    private const string ViewerLayoutLiveViewItem = BonesHostRequirementsChecklistItems.ViewerLayoutLiveView;
    private const string ViewerLayoutRevisionItem = BonesHostRequirementsChecklistItems.ViewerLayoutRevision;

    [Fact]
    [Trait("ChecklistItem", LiveMatchFeedItem)]
    [Trait("ChecklistItem", IntegrationTestsItem)]
    public async Task BonesHostFeed_GivenRunningLoop_ExpectedViewerSnapshotFrameCountGreaterThanZero()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        Assert.False(string.IsNullOrWhiteSpace(status.ViewerUrl));

        var viewerUri = new Uri(status.ViewerUrl!);
        var segments = viewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sessionId = segments[2];
        var matchId = segments[4];

        HttpResponseMessage snapshotResponse = new(HttpStatusCode.NotFound);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            snapshotResponse = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId}");
            if (snapshotResponse.StatusCode == HttpStatusCode.OK)
                break;

            await Task.Delay(100);
        }

        snapshotResponse.EnsureSuccessStatusCode();

        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<BonesMatchViewModel>(BonesWebHost.JsonOptions);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.FrameCount > 0);
    }

    [Fact]
    [Trait("ChecklistItem", LiveMatchFeedItem)]
    public async Task BonesHostFeed_GivenSuccessiveIterations_ExpectedCatalogRegistersDistinctMatchIds()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var firstStatus = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1 && !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        var firstMatchId = ExtractMatchId(firstStatus.ViewerUrl!);

        var secondStatus = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 2
                && !string.IsNullOrWhiteSpace(response.ViewerUrl)
                && !string.Equals(ExtractMatchId(response.ViewerUrl!), firstMatchId, StringComparison.Ordinal),
            TimeSpan.FromSeconds(180));

        var secondMatchId = ExtractMatchId(secondStatus.ViewerUrl!);

        Assert.NotEqual(firstMatchId, secondMatchId);
    }

    [Fact]
    [Trait("ChecklistItem", LiveMatchFeedItem)]
    public async Task BonesHostFeed_GivenPublicSnapshot_ExpectedNoStrategyMarkdownInResponseBody()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        var viewerUri = new Uri(status.ViewerUrl!);
        var segments = viewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sessionId = segments[2];
        var matchId = segments[4];

        HttpResponseMessage snapshotResponse = new(HttpStatusCode.NotFound);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            snapshotResponse = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId}");
            if (snapshotResponse.StatusCode == HttpStatusCode.OK)
                break;

            await Task.Delay(100);
        }

        snapshotResponse.EnsureSuccessStatusCode();
        var body = await snapshotResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("bones-strategy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prefer matching open ends", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private hand contents", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ViewerLayoutLiveViewItem)]
    [Trait("ChecklistItem", LiveMatchFeedItem)]
    public async Task BonesHostFeed_GivenRunningLoopAndOpenView_ExpectedScrubMidMatchReturnsCoherentFrames()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        var viewerUri = new Uri(status.ViewerUrl!);
        var segments = viewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sessionId = segments[2];
        var matchId = segments[4];

        var snapshot = await WaitForScrubbableSnapshotAsync(client, sessionId, matchId);

        var viewResponse = await client.GetAsync(viewerUri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);

        var viewBody = await viewResponse.Content.ReadAsStringAsync();
        Assert.Contains("viewer-sidebar", viewBody, StringComparison.Ordinal);
        Assert.Contains("viewer-main", viewBody, StringComparison.Ordinal);
        Assert.Contains("id=\"bones-board\"", viewBody, StringComparison.Ordinal);

        var midTurn = Math.Max(1, snapshot.FrameCount / 2);
        var frameResponse = await client.GetAsync(
            $"/bones/sessions/{sessionId}/matches/{matchId}/frames/{midTurn}");
        Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);

        var frame = await frameResponse.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);
        Assert.NotNull(frame);
        Assert.Equal(snapshot.Revision, frame.Revision);
        Assert.Equal(midTurn, frame.TurnIndex);
    }

    [Fact]
    [Trait("ChecklistItem", ViewerLayoutRevisionItem)]
    [Trait("ChecklistItem", LiveMatchFeedItem)]
    public async Task BonesHostFeed_GivenLivePublishAdvance_ExpectedSnapshotRevisionIncreasesWithout500()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        var viewerUri = new Uri(status.ViewerUrl!);
        var segments = viewerUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sessionId = segments[2];
        var matchId = segments[4];

        long lastRevision = 0;
        var sawIncrease = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var snapshot = await response.Content.ReadFromJsonAsync<BonesMatchViewModel>(BonesWebHost.JsonOptions);
                Assert.NotNull(snapshot);
                Assert.True(snapshot.Revision >= lastRevision);

                if (snapshot.Revision > lastRevision)
                {
                    sawIncrease = true;
                    lastRevision = snapshot.Revision;
                }

                if (sawIncrease && snapshot.FrameCount > 3)
                    break;
            }

            await Task.Delay(150);
        }

        Assert.True(sawIncrease, "Expected live publish to increase snapshot revision at least once.");
    }

    private static async Task<BonesMatchViewModel> WaitForScrubbableSnapshotAsync(
        HttpClient client,
        string sessionId,
        string matchId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var snapshot = await response.Content.ReadFromJsonAsync<BonesMatchViewModel>(BonesWebHost.JsonOptions);
                if (snapshot is not null && snapshot.FrameCount >= 3 && snapshot.Revision > 0)
                    return snapshot;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for a scrubbable match snapshot with frameCount >= 3.");
    }

    private static string ExtractMatchId(string viewerUrl)
    {
        var segments = new Uri(viewerUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments[4];
    }
}