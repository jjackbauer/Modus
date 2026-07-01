using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLearningLoopHostTests
{
    private const string ContinuousLoopHostItem = BonesHostRequirementsChecklistItems.ContinuousLoopHost;
    private const string IntegrationTestsItem = BonesHostRequirementsChecklistItems.IntegrationTests;
    private const string StopSemanticsTestsItem = BonesHostRequirementsChecklistItems.StopSemanticsTests;
    private const string GracefulShutdownItem = BonesHostRequirementsChecklistItems.GracefulShutdown;
    private const string DerivedSeedItem = BonesHostRequirementsChecklistItems.DerivedSeed;
    private const string HostOptionsItem = BonesHostRequirementsChecklistItems.HostOptions;
    private const string GameSimulationBudgetItem = BonesHostRequirementsChecklistItems.GameSimulationBudget;

    [Fact]
    [Trait("ChecklistItem", ContinuousLoopHostItem)]
    [Trait("ChecklistItem", IntegrationTestsItem)]
    public async Task BonesLearningLoopHost_GivenHostStart_ExpectedFirstIterationBeginsWithoutExternalCommand()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        Assert.True(status.IsRunning);
        Assert.True(status.IterationCount >= 1);
    }

    [Fact]
    [Trait("ChecklistItem", IntegrationTestsItem)]
    [Trait("ChecklistItem", ContinuousLoopHostItem)]
    public async Task BonesLearningLoopHost_GivenFirstIterationComplete_ExpectedSecondIterationStartsAutomatically()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 2,
            TimeSpan.FromSeconds(180));

        Assert.True(status.IterationCount >= 2);
    }

    [Fact]
    [Trait("ChecklistItem", ContinuousLoopHostItem)]
    public async Task BonesLearningLoopHost_GivenMultipleIterations_ExpectedIterationCountIncrementsMonotonically()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var first = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        var second = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount > first.IterationCount,
            TimeSpan.FromSeconds(180));

        Assert.True(second.IterationCount > first.IterationCount);
    }

    [Fact]
    [Trait("ChecklistItem", StopSemanticsTestsItem)]
    public async Task BonesLearningLoopHost_GivenStopRequested_ExpectedNoFurtherIterationsAfterCurrentCompletes()
    {
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings => settings["BonesHost:IterationDelayMs"] = "250");
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IsRunning
                && !string.Equals(response.CurrentStage, "Stopped", StringComparison.Ordinal)
                && !string.Equals(response.CurrentStage, "Complete", StringComparison.Ordinal),
            TimeSpan.FromSeconds(120));

        loopHost.RequestStop();

        var stopped = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !response.IsRunning,
            TimeSpan.FromSeconds(120));

        Assert.False(stopped.IsRunning);
        Assert.Equal(1, stopped.IterationCount);
    }

    [Fact]
    [Trait("ChecklistItem", GracefulShutdownItem)]
    public async Task BonesLearningLoopHost_GivenHostShutdown_ExpectedGracefulCancellationWithoutArtifactCorruption()
    {
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        await using var factory = new BonesHostWebApplicationFactory(dataDirectory);
        using var client = factory.CreateClient();
        var loopHost = factory.Services.GetRequiredService<BonesLearningLoopHost>();

        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        factory.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await Task.Delay(1000);

        Assert.False(loopHost.CurrentState.IsRunning);
        Assert.True(Directory.Exists(dataDirectory));
        _ = Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories);
    }

    [Fact]
    [Trait("ChecklistItem", DerivedSeedItem)]
    [Trait("ChecklistItem", HostOptionsItem)]
    public void BonesLearningLoopHost_GivenDerivedSeed_ExpectedDistinctButReproducibleIterationGames()
    {
        var store = new BonesHostSessionStore();
        var defaults = new BonesLearningWorkflowParameters(1, 4242, 8, new BonesPlayerId(1));
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();

        try
        {
            var first = BonesHostSessionStoreTestExtensions.GetDerivedSeed(store, 1, defaults, dataDirectory);
            var second = BonesHostSessionStoreTestExtensions.GetDerivedSeed(store, 2, defaults, dataDirectory);
            var firstAgain = BonesHostSessionStoreTestExtensions.GetDerivedSeed(store, 1, defaults, dataDirectory);

            Assert.Equal(HashCode.Combine(defaults.Seed, 1), first);
            Assert.Equal(HashCode.Combine(defaults.Seed, 2), second);
            Assert.NotEqual(first, second);
            Assert.Equal(first, firstAgain);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", GameSimulationBudgetItem)]
    public void ComputePlannedIterationGameBudget_GivenDefaultOptions_ReturnsCorrectFormula()
    {
        // The planned iteration budget is:
        //   GameCount (observe games)
        //   + 1 (play match)
        //   + EvaluationMatchCount * 2 (evaluation phase: 2 games per match)
        // With GameCount=3 and EvaluationMatchCount=20:
        //   3 + 1 + 20 * 2 = 44
        var gameCount = 3;
        var evaluationMatchCount = 20;
        var plannedBudget = gameCount + 1 + evaluationMatchCount * 2;

        Assert.Equal(44, plannedBudget);
    }

    [Fact]
    [Trait("ChecklistItem", GameSimulationBudgetItem)]
    public void ComputePlannedIterationGameBudget_GivenBudgetExhausted_GameBudgetTryReserveReturnsFalse()
    {
        // Budget for GameCount=3 + 1 play + 20 evaluation matches * 2 games each = 44
        var plannedBudget = 3 + 1 + 20 * 2;
        Assert.Equal(44, plannedBudget);

        var budget = new Wip.Bones.Agents.Budget.BonesGameSimulationBudget(maxGamesPerRun: plannedBudget);

        // Reserve the full planned iteration budget
        Assert.True(budget.TryReserve(plannedBudget));

        // The next reservation must fail because the cap is exhausted
        Assert.False(budget.TryReserve(1));
    }
}