using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLearningLoopHostCapTests
{
    private const string LearningLoopGameCapItem = BonesHostRequirementsChecklistItems.LearningLoopGameCap;
    private const string GameSimulationBudgetItem = BonesHostRequirementsChecklistItems.GameSimulationBudget;

    [Fact]
    [Trait("ChecklistItem", LearningLoopGameCapItem)]
    public async Task BonesLearningLoopHost_GivenMaxGamesPerRunReached_ExpectedStopsWithoutStartingNextIteration()
    {
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings =>
            {
                settings["BonesHost:MaxGamesPerRun"] = "7";
                settings["BonesHost:DefaultParameters:GameCount"] = "1";
            });

        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !response.IsRunning && response.CapReached,
            TimeSpan.FromSeconds(180));

        Assert.True(status.CapReached);
        Assert.Equal("CapReached", status.CurrentStage);
        Assert.Equal(7, status.MaxGamesPerRun);
        Assert.Equal(1, status.IterationCount);
        Assert.Equal(7, status.GamesSimulated);
    }

    [Fact]
    [Trait("ChecklistItem", LearningLoopGameCapItem)]
    [Trait("ChecklistItem", GameSimulationBudgetItem)]
    public void BonesLearningLoopHost_GivenPlannedIterationBudget_ExpectedReservesObservePlayAndEvaluationGames()
    {
        var promotionOptions = new BonesStrategyPromotionOptions { EvaluationMatchCount = 3 };
        var parameters = new BonesLearningWorkflowParameters(2, 4242, 8, new BonesPlayerId(1));

        var plannedBudget = parameters.GameCount + 1 + promotionOptions.EvaluationMatchCount;

        var budget = new Wip.Bones.Agents.Budget.BonesGameSimulationBudget(maxGamesPerRun: plannedBudget);
        Assert.True(budget.TryReserve(plannedBudget));

        budget.RecordGames(plannedBudget);
        Assert.False(budget.TryReserve(plannedBudget));
    }

    [Fact]
    [Trait("ChecklistItem", LearningLoopGameCapItem)]
    public async Task BonesStatusApi_GivenCapConfigured_ExpectedReportsGamesSimulatedAndMaxGamesPerRun()
    {
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings => settings["BonesHost:MaxGamesPerRun"] = "12");

        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        Assert.Equal(12, status.MaxGamesPerRun);
        Assert.True(status.GamesSimulated >= 1);
    }
}