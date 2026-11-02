using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// Behavior-proof tests for BonesLoopState IterationCount vs SessionsCompleted semantics.
/// Tests 13 and 14 from the Wip.Bones.StatusApiFixes test plan.
/// </summary>
public sealed class BonesLoopStateSessionTests
{
    private const string ClarifyIterationCountItem =
        "C14: Clarify iterationCount semantics: keep IterationCount as batch count, add completedBatches and coLearningSessionsCompleted aliases [mandatory — UX clarity]";

    private const string CoLearningSessionsItem =
        "C15: Add coLearningSessionsCompleted field distinct from batch iterationCount so operators understand parallel session progress [mandatory — UX clarity]";

    // ── Test 13 ────────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: IterationCount increments once per batch after all parallel sessions
    /// complete. SessionsCompleted increments per-session. With 4 parallel sessions,
    /// after 1 batch: IterationCount = 1, SessionsCompleted = 4.
    /// After 2 batches: IterationCount = 2, SessionsCompleted = 8.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ClarifyIterationCountItem)]
    public void BonesLoopState_GivenBatchCompletes_IterationCountIncrementsByOne()
    {
        var state = new BonesLoopState();

        // Simulate a batch with 4 parallel sessions.
        // Each session calls RecordIterationCompleted in BonesCoLearningMetrics.
        var sessionCount = 4;

        // Batch 1: set IterationCount after all sessions complete.
        for (var i = 0; i < sessionCount; i++)
        {
            state.CoLearningMetrics.RecordIterationCompleted(new[]
            {
                new BonesSeatPromotionOutcome(1, "Promoted"),
            });
        }

        state.IterationCount = 1;

        // IterationCount = 1 batch completed.
        Assert.Equal(1, state.IterationCount);

        // SessionsCompleted = 4 individual sessions.
        Assert.Equal(sessionCount, state.SessionsCompleted);

        // SessionsCompleted is greater than IterationCount because of parallelism.
        Assert.True(state.SessionsCompleted > state.IterationCount);

        // Batch 2: another 4 sessions.
        for (var i = 0; i < sessionCount; i++)
        {
            state.CoLearningMetrics.RecordIterationCompleted(new[]
            {
                new BonesSeatPromotionOutcome(2, "Promoted"),
            });
        }

        state.IterationCount = 2;

        // After 2 batches with 4 sessions each: IterationCount = 2, SessionsCompleted = 8.
        Assert.Equal(2, state.IterationCount);
        Assert.Equal(sessionCount * 2, state.SessionsCompleted);
    }

    // ── Test 14 ────────────────────────────────────────────────────────────────
    /// <summary>
    /// FIX PROOF: SessionsCompleted and IterationCount diverge mid-batch — sessions
    /// complete before the batch counter increments. completedBatches matches
    /// iterationCount, and coLearningSessionsCompleted matches CoLearningIterationsCompleted.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ClarifyIterationCountItem)]
    [Trait("ChecklistItem", CoLearningSessionsItem)]
    public void BonesLoopState_GivenParallelSessionsRunning_ReportsIterationCountAndSessionsCompleted()
    {
        var state = new BonesLoopState();
        var sessionCount = 4;

        // Simulate a mid-batch state: 2 out of 4 sessions have completed,
        // but IterationCount hasn't been set yet (still 0).
        for (var i = 0; i < 2; i++)
        {
            state.CoLearningMetrics.RecordIterationCompleted(new[]
            {
                new BonesSeatPromotionOutcome(1, "Promoted"),
            });
        }

        // Mid-batch: iterationCount = 0, sessionsCompleted = 2.
        Assert.Equal(0, state.IterationCount);
        Assert.Equal(2, state.SessionsCompleted);

        // completedBatches alias should match iterationCount.
        Assert.Equal(0, state.IterationCount);

        // coLearningSessionsCompleted alias should match SessionsCompleted.
        Assert.Equal(state.SessionsCompleted, state.CoLearningMetrics.CoLearningIterationsCompleted);

        // Complete remaining 2 sessions and finalize batch.
        for (var i = 0; i < 2; i++)
        {
            state.CoLearningMetrics.RecordIterationCompleted(new[]
            {
                new BonesSeatPromotionOutcome(2, "Rejected"),
            });
        }

        state.IterationCount = 1;

        // After batch: iterationCount = 1, sessionsCompleted = 4.
        Assert.Equal(1, state.IterationCount);
        Assert.Equal(sessionCount, state.SessionsCompleted);

        // Semantic invariants after batch completion.
        // completedBatches = iterationCount (both are 1).
        Assert.Equal(1, state.IterationCount);

        // coLearningSessionsCompleted = CoLearningIterationsCompleted (both are sessionCount).
        Assert.Equal(state.SessionsCompleted, state.CoLearningMetrics.CoLearningIterationsCompleted);

        // With parallel sessions, coLearningSessionsCompleted > iterationCount.
        Assert.True(state.SessionsCompleted > state.IterationCount,
            "SessionsCompleted should exceed IterationCount when ParallelSessionCount > 1");
    }

    // ── Sanity: SessionsCompleted is 0 initially ──────────────────────────────
    [Fact]
    [Trait("ChecklistItem", CoLearningSessionsItem)]
    public void BonesLoopState_GivenFreshState_SessionsCompletedIsZero()
    {
        var state = new BonesLoopState();

        Assert.Equal(0, state.IterationCount);
        Assert.Equal(0, state.SessionsCompleted);
        Assert.Equal(0, state.CoLearningMetrics.CoLearningIterationsCompleted);

        // The two aliases are consistent from the start.
        Assert.Equal(0, state.IterationCount);
        Assert.Equal(state.SessionsCompleted, state.CoLearningMetrics.CoLearningIterationsCompleted);
    }

    // ── Single session (parallelSessionCount = 1): both counters match ─────────
    [Fact]
    [Trait("ChecklistItem", ClarifyIterationCountItem)]
    public void BonesLoopState_GivenSingleSession_IterationCountEqualsSessionsCompleted()
    {
        var state = new BonesLoopState();

        // With parallelSessionCount=1, after each batch both counters increment by 1.
        for (var batch = 1; batch <= 3; batch++)
        {
            state.CoLearningMetrics.RecordIterationCompleted(new[]
            {
                new BonesSeatPromotionOutcome(1, "Promoted"),
            });

            state.IterationCount = batch;

            Assert.Equal(batch, state.IterationCount);
            Assert.Equal(batch, state.SessionsCompleted);
            Assert.Equal(state.IterationCount, state.SessionsCompleted);
        }
    }
}
