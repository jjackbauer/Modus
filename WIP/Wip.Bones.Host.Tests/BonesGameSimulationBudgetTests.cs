using Wip.Bones.Agents.Budget;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesGameSimulationBudgetTests
{
    private const string ChecklistItem = BonesHostRequirementsChecklistItems.GameSimulationBudget;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesGameSimulationBudget_GivenCapAndPartialConsumption_ExpectedTryReserveSucceedsWithinRemaining()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 10);
        budget.RecordGames(3);

        Assert.True(budget.TryReserve(5));
        Assert.Equal(3, budget.GamesSimulated);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesGameSimulationBudget_GivenCapExceeded_ExpectedTryReserveReturnsFalse()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 5);
        budget.RecordGames(4);

        Assert.False(budget.TryReserve(2));
        Assert.Equal(4, budget.GamesSimulated);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesGameSimulationBudget_GivenUnlimitedCap_ExpectedAlwaysReserves()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 0);
        budget.RecordGames(1000);

        Assert.True(budget.TryReserve(int.MaxValue));
        Assert.Equal(1000, budget.GamesSimulated);
    }
}