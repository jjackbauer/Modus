using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesStopApiTests
{
    private const string StopSemanticsTestsItem = BonesHostRequirementsChecklistItems.StopSemanticsTests;
    private const string StatusControlApiItem = BonesHostRequirementsChecklistItems.StatusControlApi;

    [Fact]
    [Trait("ChecklistItem", StopSemanticsTestsItem)]
    public async Task BonesStopApi_GivenRunningLoop_ExpectedStopsFurtherIterations()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var running = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1 && response.IsRunning,
            TimeSpan.FromSeconds(120));

        await BonesHostStatusClient.StopAsync(client);

        var stopped = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !response.IsRunning,
            TimeSpan.FromSeconds(120));

        Assert.False(stopped.IsRunning);
        Assert.True(stopped.IterationCount >= running.IterationCount);

        await Task.Delay(500);
        var afterDelay = await BonesHostStatusClient.GetStatusAsync(client);
        Assert.Equal(stopped.IterationCount, afterDelay.IterationCount);
    }

    [Fact]
    [Trait("ChecklistItem", StopSemanticsTestsItem)]
    [Trait("ChecklistItem", StatusControlApiItem)]
    public async Task BonesStopApi_GivenAlreadyStopped_ExpectedIdempotentStopResponse()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        await BonesHostStatusClient.StopAsync(client);
        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !response.IsRunning,
            TimeSpan.FromSeconds(120));

        var firstStop = await BonesHostStatusClient.StopAsync(client);
        var secondStop = await BonesHostStatusClient.StopAsync(client);

        Assert.False(firstStop.IsRunning);
        Assert.False(secondStop.IsRunning);
        Assert.Equal(firstStop.IterationCount, secondStop.IterationCount);
    }

    [Fact]
    [Trait("ChecklistItem", StatusControlApiItem)]
    public async Task BonesStartApi_GivenStoppedLoop_ExpectedCanRestartLoop()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        await BonesHostStatusClient.StopAsync(client);
        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !response.IsRunning,
            TimeSpan.FromSeconds(120));

        var startResponse = await client.PostAsync("/api/bones/start", content: null);
        startResponse.EnsureSuccessStatusCode();

        var restarted = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IsRunning,
            TimeSpan.FromSeconds(120));

        Assert.True(restarted.IsRunning);
    }
}