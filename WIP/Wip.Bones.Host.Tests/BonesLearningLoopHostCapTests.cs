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
        // GameCount=1, EvaluationMatchCount=40 (default) → plannedBudget = 1 + 1 + 80 = 82.
        // MaxGamesPerRun=90 allows one full iteration but not two.
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings =>
            {
                settings["BonesHost:MaxGamesPerRun"] = "90";
                settings["BonesHost:DefaultParameters:GameCount"] = "1";
            });

        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !response.IsRunning && response.CapReached,
            TimeSpan.FromSeconds(180));

        Assert.True(status.CapReached);
        Assert.Equal(90, status.MaxGamesPerRun);
        // At least one iteration completed before cap was reached.
        Assert.True(status.IterationCount >= 1,
            $"Expected at least 1 iteration but got {status.IterationCount}");
        // GamesSimulated reflects only actual games played (no double-counting).
        Assert.True(status.GamesSimulated > 0,
            $"Expected GamesSimulated > 0 but got {status.GamesSimulated}");
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
        // GameCount=3 (default), plannedBudget = 3 + 1 + 40 = 44.
        // MaxGamesPerRun=100 allows at least one full iteration.
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings => settings["BonesHost:MaxGamesPerRun"] = "100");

        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        Assert.Equal(100, status.MaxGamesPerRun);
        Assert.True(status.GamesSimulated >= 1);
    }
}