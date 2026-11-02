using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// Behavior-proof tests for BonesCoLearningMetrics cumulative promotion/rejection counters.
/// Tests 8-12 from the Wip.Bones.StatusApiFixes test plan.
/// </summary>
public sealed class BonesCoLearningMetricsTests
{
    // ── Test 8 ────────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: After two iterations with 2 promoted in iter1 then 1 promoted + 1 rejected
    /// in iter2, cumulative promoted = 3 and cumulative rejected = 1.
    /// </summary>
    [Fact]
    public void RecordIterationCompleted_AcrossMultipleIterations_CumulativeCountsAccumulate()
    {
        var metrics = new BonesCoLearningMetrics();

        // Iteration 1: seat 1 promoted, seat 2 promoted
        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(1, "Promoted"),
            new BonesSeatPromotionOutcome(2, "Promoted"),
        });

        Assert.Equal(1, metrics.CoLearningIterationsCompleted);
        var (p1, r1) = metrics.CumulativePromotionCounts;
        Assert.Equal(2, p1);
        Assert.Equal(0, r1);

        // Iteration 2: seat 3 promoted, seat 4 rejected
        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(3, "Promoted"),
            new BonesSeatPromotionOutcome(4, "Rejected"),
        });

        Assert.Equal(2, metrics.CoLearningIterationsCompleted);

        // Cumulative counts accumulate across iterations.
        var (promoted, rejected) = metrics.CumulativePromotionCounts;
        Assert.Equal(3, promoted);
        Assert.Equal(1, rejected);

        // LastPromotionOutcomes still only shows the most recent iteration's outcomes.
        Assert.Equal(2, metrics.LastPromotionOutcomes.Count);
        Assert.Equal(1, CountPromoted(metrics.LastPromotionOutcomes));
        Assert.Equal(1, CountRejected(metrics.LastPromotionOutcomes));
    }

    // ── Test 9 ────────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: Multiple parallel sessions calling RecordIterationCompleted in the
    /// same batch — cumulative counters sum ALL outcomes, not just the last writer.
    /// </summary>
    [Fact]
    public void RecordIterationCompleted_ParallelSessions_PromotionOutcomesMerge()
    {
        var metrics = new BonesCoLearningMetrics();

        // Simulate 4 parallel sessions in one batch, each producing outcomes.
        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(1, "Promoted"),
            new BonesSeatPromotionOutcome(2, "Promoted"),
        });

        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(3, "Promoted"),
        });

        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(4, "Rejected"),
        });

        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(5, "Promoted"),
        });

        // Cumulative totals sum all four sessions.
        var (promoted, rejected) = metrics.CumulativePromotionCounts;
        Assert.Equal(4, promoted);  // seats 1, 2, 3, 5
        Assert.Equal(1, rejected);  // seat 4

        // CoLearningIterationsCompleted counts per-session calls.
        Assert.Equal(4, metrics.CoLearningIterationsCompleted);

        // LastPromotionOutcomes = last writer only (seat 5).
        Assert.Single(metrics.LastPromotionOutcomes);
        Assert.Equal(5, metrics.LastPromotionOutcomes[0].Seat);
    }

    // ── Test 10 ───────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: After 3 batches of 4 sessions each with varying outcomes,
    /// cumulative counts match the sum across all 12 sessions.
    /// </summary>
    [Fact]
    public void CoLearningMetrics_CumulativeCounts_SurviveMultipleBatches()
    {
        var metrics = new BonesCoLearningMetrics();

        var totalPromoted = 0;
        var totalRejected = 0;

        // Batch 1: 4 sessions — 3 promoted, 1 rejected per session
        for (var batch = 0; batch < 3; batch++)
        {
            for (var session = 0; session < 4; session++)
            {
                var outcomes = new List<BonesSeatPromotionOutcome>();
                // Each session: seats (session*2+1) and (session*2+2) promoted, plus one rejected
                outcomes.Add(new BonesSeatPromotionOutcome(session * 2 + 1 + batch * 100, "Promoted"));
                outcomes.Add(new BonesSeatPromotionOutcome(session * 2 + 2 + batch * 100, "Promoted"));
                outcomes.Add(new BonesSeatPromotionOutcome(session * 2 + 3 + batch * 100, "Rejected"));
                totalPromoted += 2;
                totalRejected += 1;

                metrics.RecordIterationCompleted(outcomes);
            }
        }

        var (promoted, rejected) = metrics.CumulativePromotionCounts;
        Assert.Equal(totalPromoted, promoted);   // 3 batches × 4 sessions × 2 = 24
        Assert.Equal(totalRejected, rejected);   // 3 batches × 4 sessions × 1 = 12
        Assert.Equal(12, metrics.CoLearningIterationsCompleted);
    }

    // ── Test 11 ───────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: After RecordIterationCompleted with outcomes for seats 1 (Promoted),
    /// 3 (Rejected), 4 (Promoted), per-seat counters show seat1.promoted=1,
    /// seat3.rejected=1, seat4.promoted=1, seat2 unchanged.
    /// </summary>
    [Fact]
    public void CoLearningMetrics_PerSeatCounters_IncrementPerSeat()
    {
        var metrics = new BonesCoLearningMetrics();

        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(1, "Promoted"),
            new BonesSeatPromotionOutcome(3, "Rejected"),
            new BonesSeatPromotionOutcome(4, "Promoted"),
        });

        var perSeat = metrics.PerSeatPromotionCounts;

        // Seat 1: promoted once.
        Assert.True(perSeat.ContainsKey(1));
        Assert.Equal(1, perSeat[1].Promoted);
        Assert.Equal(0, perSeat[1].Rejected);

        // Seat 2: not present (unchanged).
        Assert.False(perSeat.ContainsKey(2));

        // Seat 3: rejected once.
        Assert.True(perSeat.ContainsKey(3));
        Assert.Equal(0, perSeat[3].Promoted);
        Assert.Equal(1, perSeat[3].Rejected);

        // Seat 4: promoted once.
        Assert.True(perSeat.ContainsKey(4));
        Assert.Equal(1, perSeat[4].Promoted);
        Assert.Equal(0, perSeat[4].Rejected);
    }

    // ── Test 12 ───────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: After two iterations where seat 1 is promoted both times,
    /// perSeatPromotionCounts[1].promoted = 2.
    /// </summary>
    [Fact]
    public void CoLearningMetrics_PerSeatCounters_AccumulateAcrossIterations()
    {
        var metrics = new BonesCoLearningMetrics();

        // Iteration 1: seat 1 promoted, seat 2 rejected
        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(1, "Promoted"),
            new BonesSeatPromotionOutcome(2, "Rejected"),
        });

        // Iteration 2: seat 1 promoted again, seat 3 promoted
        metrics.RecordIterationCompleted(new[]
        {
            new BonesSeatPromotionOutcome(1, "Promoted"),
            new BonesSeatPromotionOutcome(3, "Promoted"),
        });

        var perSeat = metrics.PerSeatPromotionCounts;

        // Seat 1: promoted twice across two iterations.
        Assert.Equal(2, perSeat[1].Promoted);
        Assert.Equal(0, perSeat[1].Rejected);

        // Seat 2: rejected once.
        Assert.Equal(0, perSeat[2].Promoted);
        Assert.Equal(1, perSeat[2].Rejected);

        // Seat 3: promoted once.
        Assert.Equal(1, perSeat[3].Promoted);
        Assert.Equal(0, perSeat[3].Rejected);

        // Cumulative counts also match.
        var (promoted, rejected) = metrics.CumulativePromotionCounts;
        Assert.Equal(3, promoted);  // seat1 twice, seat3 once
        Assert.Equal(1, rejected);  // seat2 once
    }

    private static int CountPromoted(IReadOnlyList<BonesSeatPromotionOutcome> outcomes)
        => outcomes.Count(o => string.Equals(o.Outcome, "Promoted", StringComparison.OrdinalIgnoreCase));

    private static int CountRejected(IReadOnlyList<BonesSeatPromotionOutcome> outcomes)
        => outcomes.Count(o => string.Equals(o.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase));

    // ── Throughput metrics tests ──────────────────────────────────────────────

    [Fact]
    public void RecordGameCompleted_GivenMultipleGames_CalculatesAverageDurationMs()
    {
        var metrics = new BonesCoLearningMetrics();

        metrics.RecordGameCompleted(100);
        metrics.RecordGameCompleted(200);
        metrics.RecordGameCompleted(300);

        Assert.Equal(200.0, metrics.AverageGameDurationMs, precision: 3);
    }

    [Fact]
    public void AverageGameDurationMs_GivenNoGames_ReturnsZero()
    {
        var metrics = new BonesCoLearningMetrics();
        Assert.Equal(0.0, metrics.AverageGameDurationMs);
    }

    [Fact]
    public void GamesPerHour_GivenNoGames_ReturnsZero()
    {
        var metrics = new BonesCoLearningMetrics();
        Assert.Equal(0.0, metrics.GamesPerHour);
    }

    [Fact]
    public void ConcurrentUtilizationPercent_DefaultsToZero()
    {
        var metrics = new BonesCoLearningMetrics();
        Assert.Equal(0.0, metrics.ConcurrentUtilizationPercent);
    }
}
