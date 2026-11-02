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

    /// <summary>
    /// FIX PROOF: After the fix, TryReserve does NOT increment _gamesSimulated.
    /// The main learning loop calls TryReserve(plannedBudget) as a pure capacity check
    /// and then each parallel session calls RecordGames. The counter reflects only
    /// actual games played, not reservations.
    ///
    /// With parallelSessionCount=4 and GameCount=1, EvalMatchCount=40 (default):
    ///   plannedBudget = 1 + 1 + 80 = 82
    ///   per session actual = 82
    ///   one batch: TryReserve(82) does NOT increment, 4 × RecordGames(82) = 328
    ///   GamesSimulated = 328 (accurate, not inflated).
    /// </summary>
    [Fact]
    public void TryReserve_ThenRecordGames_NoDoubleCounting()
    {
        const int plannedBudget = 82;
        const int parallelSessions = 4;

        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 5000);

        // Main loop: reserve planned budget for the iteration (pure check).
        Assert.True(budget.TryReserve(plannedBudget));

        // After TryReserve, counter is still zero (TryReserve no longer increments).
        Assert.Equal(0, budget.GamesSimulated);

        // Each parallel session records its actual games.
        for (var i = 0; i < parallelSessions; i++)
        {
            budget.RecordGames(plannedBudget);
        }

        // Actual games played: parallelSessions * plannedBudget = 4 * 82 = 328.
        var actualGamesPlayed = parallelSessions * plannedBudget;

        // Counter shows exactly the actual games, not inflated by reservation.
        Assert.Equal(actualGamesPlayed, budget.GamesSimulated);
    }

    /// <summary>
    /// FIX PROOF: Because TryReserve no longer increments _gamesSimulated, the budget
    /// cap check is based on actual games played. With MaxGamesPerRun=5000, the loop
    /// runs until the actual games played reaches approximately the cap. Since
    /// RecordGames happens after TryReserve returns, the final iteration may push
    /// GamesSimulated slightly past MaxGamesPerRun (at most by one iteration's
    /// worth of games minus the reserved budget). This is expected — TryReserve
    /// prevents starting the NEXT iteration, not truncating the current one.
    ///
    /// Example: MaxGamesPerRun=5000, parallelSessionCount=4, GameCount=1, EvalMatchCount=40 (default)
    ///   plannedBudget = 82, per-iteration actual increment = 4*82 = 328
    ///   After 14 iterations: actual = 14*328 = 4592, TryReserve(82) = 4592+82=4674 ≤ 5000 → pass
    ///   After iteration 15: actual = 4920, TryReserve(82) = 4920+82=5002 > 5000 → stop
    ///   GamesSimulated = 4920 (slightly past cap, but correct — no double-count inflation)
    /// </summary>
    [Fact]
    public void TryReserve_CapCheckUsesActualCounter_StopsNearIntendedCap()
    {
        const int maxGamesPerRun = 5000;
        const int plannedBudget = 82;
        const int parallelSessions = 4;

        var budget = new BonesGameSimulationBudget(maxGamesPerRun);

        var iterationsCompleted = 0;
        var actualGamesPlayed = 0;

        while (budget.TryReserve(plannedBudget))
        {
            iterationsCompleted++;
            for (var i = 0; i < parallelSessions; i++)
            {
                budget.RecordGames(plannedBudget);
                actualGamesPlayed += plannedBudget;
            }
        }

        // The loop stops near the intended cap.
        Assert.True(iterationsCompleted > 0, "Should complete at least one iteration.");

        // GamesSimulated must equal actual games played (no inflation from TryReserve).
        Assert.Equal(actualGamesPlayed, budget.GamesSimulated);

        // GamesSimulated may slightly exceed MaxGamesPerRun because the final iteration
        // was allowed by TryReserve and then RecordGames pushed it past. At most
        // parallelSessions * plannedBudget past.
        var maxOvershoot = parallelSessions * plannedBudget;
        Assert.True(budget.GamesSimulated <= maxGamesPerRun + maxOvershoot,
            $"GamesSimulated ({budget.GamesSimulated}) exceeded MaxGamesPerRun + overshoot " +
            $"({maxGamesPerRun + maxOvershoot}).");

        // TryReserve correctly stopped: the counter after the last iteration exceeds
        // the cap, so TryReserve returns false.
        Assert.False(budget.TryReserve(plannedBudget),
            "TryReserve should return false once GamesSimulated > MaxGamesPerRun.");
    }

    // ── Per-session budget tests ─────────────────────────────────────────────

    [Fact]
    public void GetPerSessionMaxGames_GivenPerSessionBudget_ReturnsDividedAmount()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 100)
        {
            IsPerSessionBudget = true,
            SessionCount = 4,
        };

        Assert.Equal(25, budget.GetPerSessionMaxGames());
    }

    [Fact]
    public void GetPerSessionMaxGames_GivenPerSessionBudgetDisabled_ReturnsGlobal()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 100)
        {
            IsPerSessionBudget = false,
            SessionCount = 4,
        };

        Assert.Equal(100, budget.GetPerSessionMaxGames());
    }

    [Fact]
    public void GetPerSessionMaxGames_GivenSingleSession_ReturnsGlobal()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 100)
        {
            IsPerSessionBudget = true,
            SessionCount = 1,
        };

        Assert.Equal(100, budget.GetPerSessionMaxGames());
    }

    [Fact]
    public void GameConcurrencyThrottle_CanBeSet()
    {
        using var throttle = new SemaphoreSlim(4, 4);
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 100)
        {
            GameConcurrencyThrottle = throttle,
        };

        Assert.Same(throttle, budget.GameConcurrencyThrottle);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesGameSimulationBudget_GivenReservationHeld_ExpectedRemainingIncludesReservation()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 10);

        Assert.True(budget.TryReserve(4));
        Assert.Equal(4, budget.ReservedGames);
        Assert.Equal(6, budget.RemainingGames);

        budget.RecordGames(2);
        Assert.Equal(2, budget.GamesSimulated);
        Assert.Equal(2, budget.ReservedGames);
        Assert.Equal(6, budget.RemainingGames);

        budget.ReleaseReservation(2);
        Assert.Equal(0, budget.ReservedGames);
        Assert.Equal(8, budget.RemainingGames);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesGameSimulationBudget_GivenFailedWork_ExpectedReleaseRestoresCapacity()
    {
        var budget = new BonesGameSimulationBudget(maxGamesPerRun: 6);

        Assert.True(budget.TryReserve(4));
        Assert.Equal(2, budget.RemainingGames);
        Assert.False(budget.TryReserve(3));

        budget.ReleaseReservation(4);
        Assert.Equal(6, budget.RemainingGames);
        Assert.True(budget.TryReserve(3));
    }
}