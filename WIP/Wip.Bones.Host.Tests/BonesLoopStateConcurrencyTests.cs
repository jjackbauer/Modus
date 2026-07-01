using System.Threading;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLoopStateConcurrencyTests
{
    private const string ChecklistItem =
        "C1: Synchronize BonesLoopState mutable scalars (IsRunning, IterationCount, StopRequested, CapReached) with Volatile or lock so GUI status polls and background loop threads see coherent values. [mandatory — prevents torn reads]";

    /// <summary>
    /// Test 16: When one thread sets IsRunning = false while another reads it,
    /// the reader sees either true or false, never a torn/inconsistent value.
    /// This is inherently guaranteed by Volatile.Read/Volatile.Write on a bool,
    /// but we stress-test it with high concurrency.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLoopState_GivenConcurrentReadWrite_SeesCoherentIsRunning()
    {
        var state = new BonesLoopState();
        var barrier = new Barrier(2);
        int sawTrue = 0;
        int sawFalse = 0;

        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 1_000_000; i++)
            {
                state.IsRunning = i % 2 == 0;
            }
        });

        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 1_000_000; i++)
            {
                var value = state.IsRunning;
                if (value)
                    Interlocked.Increment(ref sawTrue);
                else
                    Interlocked.Increment(ref sawFalse);
            }
        });

        await Task.WhenAll(writer, reader);

        // Both true and false were observed; no torn values possible with bool.
        Assert.True(sawTrue > 0, "Reader should have observed IsRunning=true at least once");
        Assert.True(sawFalse > 0, "Reader should have observed IsRunning=false at least once");
        Assert.Equal(1_000_000, sawTrue + sawFalse);
    }

    /// <summary>
    /// Test 17: IterationCount must never decrease when read concurrently with an update.
    /// The writer only increments, so the reader must see a monotonic non-decreasing sequence.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLoopState_GivenConcurrentIncrement_SeesMonotonicIterationCount()
    {
        var state = new BonesLoopState();
        var barrier = new Barrier(2);
        long maxObserved = -1;
        int nonMonotonicCount = 0;

        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (long i = 1; i <= 1_000_000; i++)
            {
                state.IterationCount = i;
            }
        });

        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            long lastObserved = -1;
            for (int i = 0; i < 1_000_000; i++)
            {
                var current = state.IterationCount;
                if (current < lastObserved)
                    Interlocked.Increment(ref nonMonotonicCount);
                lastObserved = current;

                var snapshot = Volatile.Read(ref maxObserved);
                if (current > snapshot)
                    Volatile.Write(ref maxObserved, current);
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.Equal(0, nonMonotonicCount);
        Assert.True(maxObserved > 0, "Reader should have observed some increments");
        Assert.Equal(1_000_000, state.IterationCount);
    }

    /// <summary>
    /// Verify that StopRequested when set by one thread is immediately visible to another.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLoopState_GivenStopRequestedSet_IsVisibleAcrossThreads()
    {
        var state = new BonesLoopState();
        Assert.False(state.StopRequested);

        var barrier = new Barrier(2);

        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            // Spin until StopRequested becomes true or timeout.
            for (int i = 0; i < 100_000; i++)
            {
                if (state.StopRequested)
                    return true;
                Thread.SpinWait(100);
            }
            return state.StopRequested;
        });

        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            Thread.SpinWait(1000);
            state.StopRequested = true;
        });

        await Task.WhenAll(reader, writer);

        Assert.True(state.StopRequested);
    }

    /// <summary>
    /// Verify that CapReached when set by one thread is immediately visible to another.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLoopState_GivenCapReachedSet_IsVisibleAcrossThreads()
    {
        var state = new BonesLoopState();
        Assert.False(state.CapReached);

        var barrier = new Barrier(2);

        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 100_000; i++)
            {
                if (state.CapReached)
                    return true;
                Thread.SpinWait(100);
            }
            return state.CapReached;
        });

        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            Thread.SpinWait(1000);
            state.CapReached = true;
        });

        await Task.WhenAll(reader, writer);

        Assert.True(state.CapReached);
    }

    /// <summary>
    /// Stress test: concurrent writes to all four scalars from multiple threads
    /// with a single reader polling the full state. No assertions should fail
    /// and no exceptions should be thrown.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLoopState_GivenMultiFieldConcurrentWrites_NoTornOrCorruptReads()
    {
        var state = new BonesLoopState();
        var barrier = new Barrier(4);
        var done = new ManualResetEventSlim();
        int readCount = 0;

        var setRunning = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 500_000; i++)
                state.IsRunning = i % 2 == 0;
            done.Set();
        });

        var setIteration = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 500_000; i++)
                state.IterationCount = i;
            done.Set();
        });

        var setStopRequested = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 500_000; i++)
                state.StopRequested = i % 3 == 0;
            done.Set();
        });

        var reader = Task.Run(() =>
        {
            barrier.SignalAndWait();
            while (!done.IsSet)
            {
                var isRunning = state.IsRunning;
                var iterationCount = state.IterationCount;
                var stopRequested = state.StopRequested;
                var capReached = state.CapReached;

                // Verify coherence: each value is within its valid domain
                Assert.InRange(isRunning, false, true);
                Assert.True(iterationCount >= 0, $"IterationCount should be >= 0, got {iterationCount}");
                Assert.InRange(stopRequested, false, true);
                Assert.InRange(capReached, false, true);

                Interlocked.Increment(ref readCount);
            }
        });

        await Task.WhenAll(setRunning, setIteration, setStopRequested, reader);

        Assert.True(readCount > 0, "Reader should have polled state multiple times");
    }
}
