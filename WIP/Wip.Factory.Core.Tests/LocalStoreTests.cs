using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Persistence;
using Xunit;

namespace Wip.Factory.Core.Tests;

public sealed class LocalStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wip-factory-store-tests", Guid.NewGuid().ToString("N"));

    private static readonly WorkItemId Item = new("W-1");

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
            // A leftover temp directory must not turn a green test red.
        }
    }

    private static FactoryEventEnvelope Event(string id, string type = FactoryEventTypes.RequirementSubmitted) =>
        new(
            new FactoryEventId(id),
            type,
            "/factory/test",
            "work/W-1",
            DateTimeOffset.UnixEpoch,
            new CorrelationId("C-1"),
            null,
            new Dictionary<string, string> { ["workItemId"] = "W-1" });

    private static WorkItemSnapshot Snapshot(int version, FactoryWorkState state) =>
        new(Item, state, version, "modus", [], DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Store_GivenStateChangeWithEvent_NeverExposesStateWithoutItsEvent()
    {
        var store = new LocalFactoryStore(_directory);

        var first = await store.CommitAsync(new WorkItemCommit(Snapshot(1, FactoryWorkState.Proposed), ExpectedVersion: 0, []));
        Assert.True(first.Succeeded);

        var second = await store.CommitAsync(new WorkItemCommit(
            Snapshot(2, FactoryWorkState.RequirementsAligned),
            ExpectedVersion: 1,
            [Event("E-1")]));
        Assert.True(second.Succeeded);

        var loaded = await store.LoadAsync(Item);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Version);
        var events = await store.ReadAllAsync();
        Assert.Single(events);
        Assert.Equal("E-1", events[0].Id.Value);

        // Drop the record that carried both the v2 state and E-1: the pair must vanish together.
        var logPath = Path.Combine(_directory, "factory.log");
        var lines = File.ReadAllLines(logPath);
        Assert.Equal(2, lines.Length);
        File.WriteAllText(logPath, lines[0] + Environment.NewLine);

        var reopened = new LocalFactoryStore(_directory);
        var afterTear = await reopened.LoadAsync(Item);

        Assert.NotNull(afterTear);
        Assert.Equal(1, afterTear!.Version);
        Assert.Equal(FactoryWorkState.Proposed, afterTear.State);
        Assert.Empty(await reopened.ReadAllAsync());
    }

    [Fact]
    public async Task Inbox_GivenDuplicateDelivery_AcknowledgesOnceAndTransitionsOnce()
    {
        var store = new LocalFactoryStore(_directory);
        var subscriber = new SubscriberId("subscriber.requirements");
        var eventId = new FactoryEventId("E-1");

        await store.CommitAsync(new WorkItemCommit(
            Snapshot(1, FactoryWorkState.RequirementsAligned),
            ExpectedVersion: 0,
            [Event("E-1")]));

        Assert.True(await store.TryClaimAsync(subscriber, eventId));
        Assert.False(await store.TryClaimAsync(subscriber, eventId));
        Assert.False(await store.IsAcknowledgedAsync(subscriber, eventId));

        await store.AcknowledgeAsync(subscriber, eventId);
        Assert.True(await store.IsAcknowledgedAsync(subscriber, eventId));

        // A duplicate delivery after acknowledgement must still not be re-claimable.
        Assert.False(await store.TryClaimAsync(subscriber, eventId));

        // A different subscriber is an independent consumption record.
        Assert.True(await store.TryClaimAsync(new SubscriberId("subscriber.review"), eventId));

        // Acknowledgement survives a reload.
        var reopened = new LocalFactoryStore(_directory);
        Assert.True(await reopened.IsAcknowledgedAsync(subscriber, eventId));
        Assert.False(await reopened.TryClaimAsync(subscriber, eventId));

        // The duplicate delivery produced exactly one committed state change.
        var loaded = await reopened.LoadAsync(Item);
        Assert.Equal(1, loaded!.Version);
    }

    [Fact]
    public async Task Lease_GivenStaleFencingToken_RejectsWriteAndEmitsNoEvent()
    {
        var store = new LocalFactoryStore(_directory);
        var stage = new StageName("implementation");

        await store.CommitAsync(new WorkItemCommit(
            Snapshot(1, FactoryWorkState.ReadyForImplementation),
            ExpectedVersion: 0,
            []));

        var firstLease = await store.TryAcquireAsync(Item, stage, TimeSpan.FromMinutes(5));
        Assert.NotNull(firstLease);
        Assert.Equal(1, firstLease!.FencingToken.Value);

        // A live lease cannot be re-acquired by a second worker.
        Assert.Null(await store.TryAcquireAsync(Item, stage, TimeSpan.FromMinutes(5)));

        Assert.True(await store.ReleaseAsync(firstLease));
        var secondLease = await store.TryAcquireAsync(Item, stage, TimeSpan.FromMinutes(5));
        Assert.NotNull(secondLease);
        Assert.Equal(2, secondLease!.FencingToken.Value);

        var stale = await store.CommitAsync(new WorkItemCommit(
            Snapshot(2, FactoryWorkState.InImplementation),
            ExpectedVersion: 1,
            [Event("E-stale")],
            firstLease.FencingToken));

        Assert.Equal(CommitStatus.StaleFencingToken, stale.Status);
        Assert.Null(stale.Committed);
        Assert.Contains("superseded", stale.Reason, StringComparison.Ordinal);

        var unchanged = await store.LoadAsync(Item);
        Assert.Equal(1, unchanged!.Version);
        Assert.Empty(await store.ReadAllAsync());

        // The current token is accepted.
        var accepted = await store.CommitAsync(new WorkItemCommit(
            Snapshot(2, FactoryWorkState.InImplementation),
            ExpectedVersion: 1,
            [Event("E-current")],
            secondLease.FencingToken));

        Assert.True(accepted.Succeeded);
        Assert.Equal(2, (await store.LoadAsync(Item))!.Version);
        Assert.Single(await store.ReadAllAsync());
    }

    [Fact]
    public async Task Commit_GivenConcurrentVersionMismatch_EmitsNoEvent()
    {
        var store = new LocalFactoryStore(_directory);

        Assert.True((await store.CommitAsync(new WorkItemCommit(
            Snapshot(1, FactoryWorkState.Proposed), ExpectedVersion: 0, []))).Succeeded);

        // A second writer that still believes the aggregate is at version 0 must lose without side effects.
        var conflict = await store.CommitAsync(new WorkItemCommit(
            Snapshot(1, FactoryWorkState.RequirementsAligned),
            ExpectedVersion: 0,
            [Event("E-loser")]));

        Assert.Equal(CommitStatus.VersionConflict, conflict.Status);
        Assert.Contains("committed version is 1", conflict.Reason, StringComparison.Ordinal);

        var loaded = await store.LoadAsync(Item);
        Assert.Equal(FactoryWorkState.Proposed, loaded!.State);
        Assert.Equal(1, loaded.Version);
        Assert.Empty(await store.ReadAllAsync());

        // A correctly versioned follow-up still commits.
        var winner = await store.CommitAsync(new WorkItemCommit(
            Snapshot(2, FactoryWorkState.RequirementsAligned),
            ExpectedVersion: 1,
            [Event("E-winner")]));
        Assert.True(winner.Succeeded);
        Assert.Single(await store.ReadAllAsync());
    }
}
