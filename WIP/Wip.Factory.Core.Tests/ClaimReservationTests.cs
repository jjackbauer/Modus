using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Persistence;
using Xunit;

namespace Wip.Factory.Core.Tests;

/// <summary>
/// Claim reservation (v0.3 §5.1): a claim is a durable reservation with an owner and a deadline, so a live claim is
/// not handed to another subscriber and a crashed worker's claim becomes reclaimable.
/// </summary>
public sealed class ClaimReservationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "factory-claim-" + Guid.NewGuid().ToString("N"));

    private static readonly SubscriberId WorkerA = new("worker-a");
    private static readonly SubscriberId WorkerB = new("worker-b");

    private static FactoryEventEnvelope Event()
        => new(
            new FactoryEventId(Guid.NewGuid().ToString("N")),
            FactoryEventTypes.RequirementSubmitted,
            "test",
            "W-1",
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["workItemId"] = "W-1" });

    private async Task<LocalFactoryStore> SeededAsync()
    {
        Directory.CreateDirectory(_directory);
        var store = new LocalFactoryStore(_directory);
        var snapshot = new WorkItemSnapshot(
            new WorkItemId("W-1"), FactoryWorkState.Proposed, WorkItemSnapshot.MinimumVersion,
            "/repo", [], DateTimeOffset.UtcNow);

        var commit = await store.CommitAsync(new WorkItemCommit(snapshot, 0, [Event()]));
        Assert.True(commit.Succeeded, commit.Reason);
        return store;
    }

    [Fact]
    public async Task Claim_GivenLiveClaim_IsNotDispatchedTwice()
    {
        var store = await SeededAsync();

        var first = await store.ClaimUnpublishedAsync(WorkerA, 10);
        Assert.Single(first);

        // A different subscriber must not receive a live claim.
        var second = await store.ClaimUnpublishedAsync(WorkerB, 10);
        Assert.Empty(second);

        // Durable: a fresh store over the same log still refuses the other subscriber.
        Assert.Empty(await new LocalFactoryStore(_directory).ClaimUnpublishedAsync(WorkerB, 10));

        // The owner may retry its own claim (that is the retry path, not duplication).
        var retry = await store.ClaimUnpublishedAsync(WorkerA, 10);
        Assert.Single(retry);
    }

    [Fact]
    public async Task Claim_GivenCrashAfterClaim_IsReclaimableAfterDeadline()
    {
        // A zero lease models a worker that claimed and then died: its reservation is already expired.
        Directory.CreateDirectory(_directory);
        var dying = new LocalFactoryStore(_directory, claimLease: TimeSpan.Zero);

        var snapshot = new WorkItemSnapshot(
            new WorkItemId("W-1"), FactoryWorkState.Proposed, WorkItemSnapshot.MinimumVersion,
            "/repo", [], DateTimeOffset.UtcNow);
        Assert.True((await dying.CommitAsync(new WorkItemCommit(snapshot, 0, [Event()]))).Succeeded);
        Assert.Single(await dying.ClaimUnpublishedAsync(WorkerA, 10));

        // Another subscriber reclaims the expired reservation from durable state alone.
        var survivor = new LocalFactoryStore(_directory, claimLease: TimeSpan.Zero);
        var reclaimed = await survivor.ClaimUnpublishedAsync(WorkerB, 10);
        Assert.Single(reclaimed);
    }
    [Fact]
    public async Task Claim_GivenPublishedEvent_IsNeverClaimedAgain()
    {
        var store = await SeededAsync();
        var claimed = await store.ClaimUnpublishedAsync(WorkerA, 10);
        Assert.Single(claimed);

        await store.MarkPublishedAsync(claimed[0].Id);

        Assert.Empty(await store.ClaimUnpublishedAsync(WorkerA, 10));
        Assert.Empty(await store.ClaimUnpublishedAsync(WorkerB, 10));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}