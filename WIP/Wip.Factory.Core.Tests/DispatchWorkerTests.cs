using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Persistence;
using Xunit;

namespace Wip.Factory.Core.Tests;

public sealed class DispatchWorkerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wip-factory-worker-tests", Guid.NewGuid().ToString("N"));

    private static readonly SubscriberId Dispatcher = new("factory.dispatcher");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static FactoryEventEnvelope Event(string id, string workItemId, string type = FactoryEventTypes.WorkReady) =>
        new(
            new FactoryEventId(id),
            type,
            "/factory/test",
            $"work/{workItemId}",
            DateTimeOffset.UtcNow,
            new CorrelationId("C-1"),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["workItemId"] = workItemId });

    private static WorkItemSnapshot Snapshot(
        string id,
        FactoryWorkState state,
        int version,
        IReadOnlyList<WorkDependency>? dependencies = null) =>
        new(new WorkItemId(id), state, version, "modus", dependencies ?? [], DateTimeOffset.UnixEpoch);

    /// <summary>Advances a ready item into implementation and enqueues a follow-up event; otherwise no-ops.</summary>
    private sealed class AdvancingHandler : IStageDispatchHandler
    {
        public int AdvancedCount;
        public int InvocationCount;

        public ValueTask<StageDispatchOutcome> HandleAsync(
            WorkItemSnapshot workItem,
            FactoryEventEnvelope trigger,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;

            if (workItem.State != FactoryWorkState.ReadyForImplementation)
            {
                return ValueTask.FromResult(StageDispatchOutcome.NoOp());
            }

            AdvancedCount++;
            return ValueTask.FromResult(StageDispatchOutcome.Advanced(
                FactoryWorkState.InImplementation,
                [Event("E-advanced-" + workItem.WorkItemId.Value, workItem.WorkItemId.Value,
                    FactoryEventTypes.ImplementationProposed)]));
        }
    }

    private sealed class SwitchableHandler : IStageDispatchHandler
    {
        public Func<WorkItemSnapshot, StageDispatchOutcome> Behaviour = _ => StageDispatchOutcome.NoOp();

        public ValueTask<StageDispatchOutcome> HandleAsync(
            WorkItemSnapshot workItem,
            FactoryEventEnvelope trigger,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Behaviour(workItem));
    }

    private static FactoryDispatchWorker Worker(
        LocalFactoryStore store,
        IStageDispatchHandler handler,
        int maxAttempts = 3) =>
        new(store, store, store, handler, new FactoryDispatchOptions(Dispatcher, maxAttempts));

    [Fact]
    public async Task Worker_GivenHostRestartMidDispatch_ResumesExactlyOnceOnRecovery()
    {
        var first = new LocalFactoryStore(_directory);
        await first.CommitAsync(new WorkItemCommit(
            Snapshot("W-1", FactoryWorkState.ReadyForImplementation, 1),
            ExpectedVersion: 0,
            [Event("E-1", "W-1")]));

        // The host dies here: the commit is durable but nothing was dispatched.
        var restarted = new LocalFactoryStore(_directory);
        var handler = new AdvancingHandler();
        var worker = Worker(restarted, handler);

        var recoveryRun = await worker.RunOnceAsync();
        Assert.Equal(1, recoveryRun.Dispatched);
        Assert.Equal(0, recoveryRun.Duplicates);

        var afterRecovery = await restarted.LoadAsync(new WorkItemId("W-1"));
        Assert.Equal(FactoryWorkState.InImplementation, afterRecovery!.State);
        Assert.Equal(2, afterRecovery.Version);

        // Further passes must not advance the aggregate or re-count the original delivery.
        await worker.RunOnceAsync();
        await worker.RunOnceAsync();

        var settled = await restarted.LoadAsync(new WorkItemId("W-1"));
        Assert.Equal(2, settled!.Version);
        Assert.Equal(1, handler.AdvancedCount);

        // A second restart sees the acknowledged delivery and performs no duplicate transition.
        var restartedAgain = new LocalFactoryStore(_directory);
        await Worker(restartedAgain, handler).RunOnceAsync();
        Assert.Equal(1, handler.AdvancedCount);
        Assert.Equal(2, (await restartedAgain.LoadAsync(new WorkItemId("W-1")))!.Version);
    }

    [Fact]
    public async Task Worker_GivenPoisonEvent_QuarantinesThenReplaysWithoutDuplication()
    {
        var store = new LocalFactoryStore(_directory);
        await store.CommitAsync(new WorkItemCommit(
            Snapshot("W-1", FactoryWorkState.ReadyForImplementation, 1),
            ExpectedVersion: 0,
            [Event("E-1", "W-1")]));

        var handler = new SwitchableHandler
        {
            Behaviour = _ => throw new InvalidOperationException("handler is broken")
        };
        var worker = Worker(store, handler, maxAttempts: 3);

        await worker.RunOnceAsync();
        await worker.RunOnceAsync();
        Assert.Empty(await store.ReadQuarantinedAsync());
        Assert.Equal(1, (await store.LoadAsync(new WorkItemId("W-1")))!.Version);

        var thirdRun = await worker.RunOnceAsync();
        Assert.Equal(1, thirdRun.Quarantined);

        var quarantined = Assert.Single(await store.ReadQuarantinedAsync());
        Assert.Equal("E-1", quarantined.Envelope.Id.Value);
        Assert.Equal(3, quarantined.Attempts);
        Assert.Contains("handler is broken", quarantined.Reason, StringComparison.Ordinal);

        // A quarantined event is out of the dispatchable set.
        Assert.Empty(await store.ClaimUnpublishedAsync(Dispatcher, 10));

        // Repair the handler, replay once, and the work advances exactly once.
        handler.Behaviour = workItem => workItem.State == FactoryWorkState.ReadyForImplementation
            ? StageDispatchOutcome.Advanced(FactoryWorkState.InImplementation)
            : StageDispatchOutcome.NoOp();

        Assert.True(await store.ReplayQuarantinedAsync(new FactoryEventId("E-1")));
        Assert.Empty(await store.ReadQuarantinedAsync());

        var replayRun = await worker.RunOnceAsync();
        Assert.Equal(1, replayRun.Dispatched);

        await worker.RunOnceAsync();
        var settled = await store.LoadAsync(new WorkItemId("W-1"));
        Assert.Equal(FactoryWorkState.InImplementation, settled!.State);
        Assert.Equal(2, settled.Version);
    }

    [Fact]
    public async Task Readiness_GivenIncompleteDependency_KeepsItemBlockedUntilCompletionEvent()
    {
        var store = new LocalFactoryStore(_directory);
        var dependency = new WorkItemId("W-A");

        // The prerequisite is still in flight, so W-B is not eligible.
        await store.CommitAsync(new WorkItemCommit(
            Snapshot("W-A", FactoryWorkState.InImplementation, 1),
            ExpectedVersion: 0,
            []));

        await store.CommitAsync(new WorkItemCommit(
            Snapshot(
                "W-B",
                FactoryWorkState.ReadyForImplementation,
                1,
                [new WorkDependency(dependency, DependencyKind.WorkItem, dependency.Value)]),
            ExpectedVersion: 0,
            [Event("E-b", "W-B")]));

        var handler = new AdvancingHandler();
        var worker = Worker(store, handler);

        var blockedRun = await worker.RunOnceAsync();
        Assert.Equal(1, blockedRun.Blocked);
        Assert.Equal(0, handler.AdvancedCount);

        var blocked = await store.LoadAsync(new WorkItemId("W-B"));
        Assert.Equal(FactoryWorkState.Blocked, blocked!.State);
        Assert.Contains(
            await store.ReadAllAsync(),
            envelope => envelope.Type == FactoryEventTypes.WorkBlocked);

        // The prerequisite completes; its completion event names the item that must wake.
        await store.CommitAsync(new WorkItemCommit(
            Snapshot("W-A", FactoryWorkState.Released, 2),
            ExpectedVersion: 1,
            [Event("E-wake", "W-B", FactoryEventTypes.WorkReady)]));

        await worker.RunOnceAsync();

        var woken = await store.LoadAsync(new WorkItemId("W-B"));
        Assert.Equal(FactoryWorkState.InImplementation, woken!.State);
        Assert.Equal(1, handler.AdvancedCount);

        // Processing the remaining queued events must not advance it a second time.
        await worker.RunOnceAsync();
        Assert.Equal(1, handler.AdvancedCount);
        Assert.Equal(woken.Version, (await store.LoadAsync(new WorkItemId("W-B")))!.Version);
    }
}
