using System.Net;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesStatusApiTests
{
    private const string StatusControlApiItem = BonesHostRequirementsChecklistItems.StatusControlApi;

    [Fact]
    [Trait("ChecklistItem", StatusControlApiItem)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedReportsRunningStateAndIterationCount()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1 && response.IsRunning,
            TimeSpan.FromSeconds(120));

        Assert.True(status.IsRunning);
        Assert.True(status.IterationCount >= 1);
        Assert.False(string.IsNullOrWhiteSpace(status.CurrentStage));
    }

    [Fact]
    [Trait("ChecklistItem", StatusControlApiItem)]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.LiveMatchFeed)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedIncludesReachableViewerUrl()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        Assert.False(string.IsNullOrWhiteSpace(status.ViewerUrl));

        var viewerPath = new Uri(status.ViewerUrl!).PathAndQuery;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        HttpStatusCode viewerStatus = HttpStatusCode.NotFound;
        while (DateTime.UtcNow < deadline)
        {
            viewerStatus = (await client.GetAsync(viewerPath)).StatusCode;
            if (viewerStatus == HttpStatusCode.OK)
                break;

            await Task.Delay(100);
        }

        Assert.Equal(HttpStatusCode.OK, viewerStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.StatusApiLearningPlayer)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedReportsLearningPlayerBudgetAndResumeFields()
    {
        await using var factory = new BonesHostWebApplicationFactory(configureConfiguration: settings =>
        {
            settings["BonesHost:MaxGamesPerRun"] = "50";
            settings["BonesHost:ResumeFromBest"] = "false";
        });

        using var client = factory.CreateClient();
        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1
                && response.LearningPlayer is not null
                && response.GamesSimulated > 0,
            TimeSpan.FromSeconds(120));

        // Default iteration: observe (GameCount=1) + play (1) + promotion evaluation (5).
        Assert.True(status.GamesSimulated >= 2);
        Assert.Equal(50, status.MaxGamesPerRun);
        Assert.False(status.ResumeFromBest);
        Assert.NotNull(status.LearningPlayer);
        Assert.False(string.IsNullOrWhiteSpace(status.LearningPlayer!.ActiveStrategyId));
        Assert.NotNull(status.LearningPlayer.Effectiveness);
    }
}