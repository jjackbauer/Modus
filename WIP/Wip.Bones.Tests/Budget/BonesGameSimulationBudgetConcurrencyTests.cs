using Wip.Bones.Agents.Budget;
using Xunit;

namespace Wip.Bones.Tests.Budget;

public sealed class BonesGameSimulationBudgetConcurrencyTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.GameSimulationBudget;

    /// <summary>
    /// Test plan item 18: Under concurrent calls to <c>TryReserve</c> and <c>RecordGames</c>
    /// from parallel threads, <c>GamesSimulated</c> must never exceed <c>MaxGamesPerRun</c>.
    /// The budget starts at 0 and each thread tries to reserve a small batch;
    /// the final count must exactly match the sum of successful reservations + recorded games
    /// and must never exceed the cap.
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
                    if (budget.TryReserve(gameCountPerReservation))
                    {
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

        // GamesSimulated must match the number of successful reservations (each was count=1).
        Assert.Equal(totalSuccessful, budget.GamesSimulated);

        // Must never exceed the cap.
        Assert.True(budget.GamesSimulated <= maxGamesPerRun,
            $"GamesSimulated={budget.GamesSimulated} exceeded MaxGamesPerRun={maxGamesPerRun}");

        // At least some reservations should succeed (sanity check).
        Assert.True(totalSuccessful > 0,
            "Expected at least some successful reservations");
    }

    /// <summary>
    /// Concurrent <c>TryReserve</c> with mixed game counts. Verifies that even when
    /// threads request different batch sizes, the cap is never exceeded.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenConcurrentMixedBatchSizes_DoesNotExceedMaxGamesPerRun()
    {
        const int maxGamesPerRun = 500;
        const int threadCount = 8;
        const int reservationsPerThread = 100;

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
        Assert.True(budget.GamesSimulated <= maxGamesPerRun,
            $"GamesSimulated={budget.GamesSimulated} exceeded MaxGamesPerRun={maxGamesPerRun}");
        Assert.True(totalReserved > 0,
            "Expected at least some successful reservations");
    }

    /// <summary>
    /// Verifies that <c>TryReserve</c> returns <c>false</c> once the budget is exhausted
    /// even under concurrent pressure. All threads attempting after exhaustion must be rejected.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenBudgetExhausted_ReturnsFalseForAllSubsequentCalls()
    {
        const int maxGamesPerRun = 100;
        var budget = new BonesGameSimulationBudget(maxGamesPerRun);

        // Exhaust the budget on a single thread.
        var exhausted = budget.TryReserve(maxGamesPerRun);
        Assert.True(exhausted);
        Assert.Equal(maxGamesPerRun, budget.GamesSimulated);

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
    /// the counter. <c>RecordGames</c> bypasses the lock (uses Interlocked.Add) and
    /// <c>TryReserve</c> uses lock + Interlocked.Add; the combined total must be correct.
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

        // Threads that call TryReserve.
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

        // Threads that call RecordGames directly (bypasses TryReserve).
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

        var totalReserved = reservedCounts.Sum();
        var totalRecorded = recordedCounts.Sum();

        // Total games simulated must be the sum of TryReserve increments + RecordGames increments.
        Assert.Equal(totalReserved + totalRecorded, budget.GamesSimulated);
    }

    /// <summary>
    /// Reproduces the TOCTOU race condition: without the fix, two threads could both read
    /// GamesSimulated + gameCount &lt;= MaxGamesPerRun before either writes, leading to over-reservation.
    /// With the lock-based fix, the final GamesSimulated must never exceed MaxGamesPerRun.
    /// This test runs enough iterations to make the race statistically inevitable without a fix.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TryReserve_GivenHighContentionTightCap_DoesNotExceedMax()
    {
        // Tight cap: only one reservation of size 1 should succeed.
        const int maxGamesPerRun = 1;
        const int threadCount = 32;
        const int attemptsPerThread = 100;

        // Run the tight scenario multiple times to increase chance of race detection.
        for (var run = 0; run < 50; run++)
        {
            var budget = new BonesGameSimulationBudget(maxGamesPerRun);
            var successCount = 0;
            var barrier = new Barrier(threadCount);
            var threads = new Thread[threadCount];

            for (var i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    for (var j = 0; j < attemptsPerThread; j++)
                    {
                        if (budget.TryReserve(1))
                        {
                            Interlocked.Increment(ref successCount);
                        }
                    }
                });
            }

            foreach (var thread in threads)
                thread.Start();

            foreach (var thread in threads)
                thread.Join();

            // With max=1, at most 1 reservation can succeed.
            Assert.True(successCount <= 1,
                $"Run {run}: {successCount} reservations succeeded but MaxGamesPerRun={maxGamesPerRun}");
            Assert.True(budget.GamesSimulated <= maxGamesPerRun,
                $"Run {run}: GamesSimulated={budget.GamesSimulated} exceeded MaxGamesPerRun={maxGamesPerRun}");
        }
    }
}
