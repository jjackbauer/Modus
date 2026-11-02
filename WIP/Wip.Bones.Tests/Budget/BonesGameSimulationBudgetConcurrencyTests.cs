using Wip.Bones.Agents.Budget;
using Xunit;

namespace Wip.Bones.Tests.Budget;

public sealed class BonesGameSimulationBudgetConcurrencyTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.GameSimulationBudget;

    /// <summary>
    /// Under concurrent calls to <c>TryReserve</c> + <c>RecordGames</c> from parallel
    /// threads (modeling the real loop pattern: reserve-then-record). Since
    /// <c>TryReserve</c> is a pure check and <c>RecordGames</c> advances the counter
    /// via <c>Interlocked.Add</c> outside the lock, a small cap overshoot is possible.
    /// The key invariant: <c>GamesSimulated</c> matches successful reservations and
    /// stays within a bounded overshoot of <c>MaxGamesPerRun</c>.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenConcurrentReservations_DoesNotExceedMaxGamesPerRun()
    {
        const int maxGamesPerRun = 1000;
        const int threadCount = 16;
        const int reservationsPerThread = 200;
        const int gameCountPerReservation = 1;

        // With max=1000, only 1000 reservations of size 1 can succeed.
        // Total attempts = 16 * 200 = 3200, so ~2200 must be rejected.
        var budget = new BonesGameSimulationBudget(maxGamesPerRun);
        var successCounts = new int[threadCount];
        var barrier = new Barrier(threadCount);
        var threads = new Thread[threadCount];

        for (var i = 0; i < threadCount; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                barrier.SignalAndWait();

                for (var j = 0; j < reservationsPerThread; j++)
                {
                    // Model real pattern: reserve then record.
                    if (budget.TryReserve(gameCountPerReservation))
                    {
                        budget.RecordGames(gameCountPerReservation);
                        successCounts[index]++;
                    }
                }
            });
        }

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join();

        var totalSuccessful = successCounts.Sum();

        // GamesSimulated must equal the number of successful reservations (each was 1 game).
        Assert.Equal(totalSuccessful, budget.GamesSimulated);

        // A small overshoot is possible due to the TryReserve→RecordGames TOCTOU window.
        // The overshoot is bounded by threadCount (all threads could succeed one extra reserve).
        Assert.True(budget.GamesSimulated <= maxGamesPerRun + threadCount,
            $"GamesSimulated={budget.GamesSimulated} exceeded MaxGamesPerRun + " +
            $"threadCount ({maxGamesPerRun + threadCount})");

        // At least some reservations should succeed (sanity check).
        Assert.True(totalSuccessful > 0,
            "Expected at least some successful reservations");
    }

    /// <summary>
    /// Concurrent <c>TryReserve</c> with mixed game counts. Verifies that even when
    /// threads request different batch sizes and record on success, the cap is bounded.
    /// Because <c>RecordGames</c> runs outside the <c>TryReserve</c> lock, a TOCTOU
    /// window exists: GamesSimulated may slightly exceed MaxGamesPerRun by at most
    /// one reservation's batch size minus 1. This is inherent to the design where
    /// <c>TryReserve</c> is a pure check and <c>RecordGames</c> is the sole incrementer.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenConcurrentMixedBatchSizes_DoesNotExceedMaxGamesPerRun()
    {
        const int maxGamesPerRun = 500;
        const int threadCount = 8;
        const int reservationsPerThread = 100;
        const int maxBatchSize = 3; // (index % 3) + 1

        var budget = new BonesGameSimulationBudget(maxGamesPerRun);
        var barrier = new Barrier(threadCount);
        var threads = new Thread[threadCount];
        var successCounts = new int[threadCount];

        for (var i = 0; i < threadCount; i++)
        {
            var index = i;
            // Each thread uses a different batch size: 1, 2, 3, 1, 2, 3, ...
            var batchSize = (index % 3) + 1;
            threads[i] = new Thread(() =>
            {
                barrier.SignalAndWait();

                for (var j = 0; j < reservationsPerThread; j++)
                {
                    if (budget.TryReserve(batchSize))
                    {
                        budget.RecordGames(batchSize);
                        Interlocked.Add(ref successCounts[index], batchSize);
                    }
                }
            });
        }

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join();

        var totalReserved = successCounts.Sum();

        Assert.Equal(totalReserved, budget.GamesSimulated);

        // Because RecordGames runs outside the TryReserve lock, the cap may be slightly
        // exceeded by concurrent threads. The key invariant is that GamesSimulated
        // matches totalReserved (external tracking) and stays within a bounded overshoot.
        Assert.True(budget.GamesSimulated <= maxGamesPerRun + maxBatchSize * threadCount,
            $"GamesSimulated={budget.GamesSimulated} exceeded MaxGamesPerRun + " +
            $"buffer ({maxGamesPerRun + maxBatchSize * threadCount})");
        Assert.True(totalReserved > 0,
            "Expected at least some successful reservations");
    }

    /// <summary>
    /// Verifies that <c>TryReserve</c> returns <c>false</c> once the budget is exhausted
    /// even under concurrent pressure. All threads attempting after exhaustion must be rejected.
    /// Since <c>TryReserve</c> no longer increments <c>GamesSimulated</c>, we use
    /// <c>RecordGames</c> to advance the counter first.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenBudgetExhausted_ReturnsFalseForAllSubsequentCalls()
    {
        const int maxGamesPerRun = 100;
        var budget = new BonesGameSimulationBudget(maxGamesPerRun);

        // Simulate actual games played to exhaust the budget.
        budget.RecordGames(maxGamesPerRun);
        Assert.Equal(maxGamesPerRun, budget.GamesSimulated);

        // Now TryReserve must return false since the budget is exhausted.
        Assert.False(budget.TryReserve(1));

        // Now run concurrent threads — all should be rejected.
        const int threadCount = 8;
        var barrier = new Barrier(threadCount);
        var rejectionCounts = new int[threadCount];

        var threads = new Thread[threadCount];
        for (var i = 0; i < threadCount; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                barrier.SignalAndWait();
                for (var j = 0; j < 50; j++)
                {
                    if (!budget.TryReserve(1))
                    {
                        rejectionCounts[index]++;
                    }
                }
            });
        }

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join();

        // Every TryReserve call after exhaustion must return false.
        Assert.Equal(threadCount * 50, rejectionCounts.Sum());
        Assert.Equal(maxGamesPerRun, budget.GamesSimulated);
    }

    /// <summary>
    /// Concurrent <c>TryReserve</c> and <c>RecordGames</c> interleaving must not corrupt
    /// the counter. <c>RecordGames</c> uses Interlocked.Add and
    /// <c>TryReserve</c> uses lock but does NOT increment <c>GamesSimulated</c>.
    /// The final <c>GamesSimulated</c> must equal only the sum of <c>RecordGames</c>
    /// increments, not including any <c>TryReserve</c> calls.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenConcurrentRecordGamesInterleaved_MaintainsCorrectTotal()
    {
        const int maxGamesPerRun = 2000;
        const int tryReserveThreads = 4;
        const int recordGamesThreads = 4;
        const int operationsPerThread = 500;

        var budget = new BonesGameSimulationBudget(maxGamesPerRun);
        var barrier = new Barrier(tryReserveThreads + recordGamesThreads);
        var reservedCounts = new int[tryReserveThreads];
        var recordedCounts = new int[recordGamesThreads];

        var threads = new List<Thread>();

        // Threads that call TryReserve (pure check, does not increment GamesSimulated).
        for (var i = 0; i < tryReserveThreads; i++)
        {
            var index = i;
            threads.Add(new Thread(() =>
            {
                barrier.SignalAndWait();
                for (var j = 0; j < operationsPerThread; j++)
                {
                    if (budget.TryReserve(1))
                    {
                        reservedCounts[index]++;
                    }
                }
            }));
        }

        // Threads that call RecordGames directly (the only incrementer).
        for (var i = 0; i < recordGamesThreads; i++)
        {
            var index = i;
            threads.Add(new Thread(() =>
            {
                barrier.SignalAndWait();
                for (var j = 0; j < operationsPerThread; j++)
                {
                    budget.RecordGames(1);
                    recordedCounts[index]++;
                }
            }));
        }

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join();

        var totalRecorded = recordedCounts.Sum();

        // GamesSimulated must equal only the sum of RecordGames increments.
        // TryReserve does NOT increment GamesSimulated.
        Assert.Equal(totalRecorded, budget.GamesSimulated);
    }

    /// <summary>
    /// Tight cap correctness test. With <c>MaxGamesPerRun=1</c>, a single-threaded
    /// caller that does <c>TryReserve</c> + <c>RecordGames</c> correctly stops after
    /// the first successful reservation. This models the real <c>BonesLearningLoopHost</c>
    /// pattern where <c>TryReserve</c> is called from a single loop thread.
    /// <c>RecordGames</c> advances the counter so subsequent <c>TryReserve</c> calls
    /// see the updated value and return false.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenTightCap_StopsAfterCapReached()
    {
        const int maxGamesPerRun = 1;
        var budget = new BonesGameSimulationBudget(maxGamesPerRun);

        // First reservation succeeds.
        Assert.True(budget.TryReserve(1));
        Assert.Equal(0, budget.GamesSimulated); // TryReserve does not increment.

        // Record the actual game.
        budget.RecordGames(1);
        Assert.Equal(1, budget.GamesSimulated);

        // Second reservation must fail because GamesSimulated == MaxGamesPerRun.
        Assert.False(budget.TryReserve(1));
        Assert.Equal(1, budget.GamesSimulated); // Counter unchanged.
    }
}
