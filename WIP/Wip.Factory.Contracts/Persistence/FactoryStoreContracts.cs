using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;

namespace Wip.Factory.Contracts.Persistence;

/// <summary>
/// A monotonic write guard for a (work item, stage) lease. A write carrying a token that has been
/// superseded is refused, so a stalled worker cannot commit over a newer claim (F-C.4).
/// </summary>
public readonly record struct FencingToken
{
    public FencingToken(long value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A fencing token must be at least 1.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A held lease: which stage, which fencing token, and when it lapses.</summary>
public sealed record EventLeaseTicket(
    WorkItemId WorkItemId,
    StageName Stage,
    FencingToken FencingToken,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Outcome of a requested commit.</summary>
public enum CommitStatus
{
    /// <summary>The state change and its outgoing events were durably committed together.</summary>
    Committed = 1,

    /// <summary>The aggregate moved past the caller's expected version.</summary>
    VersionConflict = 2,

    /// <summary>The caller's fencing token has been superseded by a newer lease.</summary>
    StaleFencingToken = 3,

    /// <summary>The commit was refused for a non-concurrency reason.</summary>
    Rejected = 4
}

/// <summary>
/// A requested atomic mutation: the expected aggregate version, the new snapshot, and the events that
/// must become visible only if the snapshot does (F-C.2, F-C.5).
/// </summary>
public sealed record WorkItemCommit(
    WorkItemSnapshot Snapshot,
    int ExpectedVersion,
    IReadOnlyList<FactoryEventEnvelope> OutgoingEvents,
    FencingToken? FencingToken = null);

/// <summary>Result of a commit attempt. On any non-<see cref="CommitStatus.Committed"/> status nothing changed.</summary>
public sealed record WorkStoreCommitResult(CommitStatus Status, WorkItemSnapshot? Committed, string? Reason)
{
    public bool Succeeded => Status == CommitStatus.Committed;

    public static WorkStoreCommitResult Accepted(WorkItemSnapshot committed)
        => new(CommitStatus.Committed, committed, null);

    public static WorkStoreCommitResult Refused(CommitStatus status, string reason)
        => new(status, null, reason);
}

/// <summary>
/// Durable work aggregate access. Load returns the last committed snapshot or null; commit is the only
/// mutation, and it commits the snapshot and its events together.
/// </summary>
public interface IWorkItemStore
{
    ValueTask<WorkItemSnapshot?> LoadAsync(WorkItemId workItemId, CancellationToken cancellationToken = default);

    ValueTask<WorkStoreCommitResult> CommitAsync(WorkItemCommit commit, CancellationToken cancellationToken = default);
}

/// <summary>An event removed from dispatch after repeated failure (F-C.7).</summary>
public sealed record QuarantinedEvent(
    FactoryEventEnvelope Envelope,
    string Reason,
    int Attempts,
    DateTimeOffset QuarantinedAtUtc);

/// <summary>
/// Durable outgoing-event log. Events are appended only as part of a work commit, so a subscriber can
/// never observe an event whose state change did not commit (F-C.2).
/// </summary>
public interface IEventOutbox
{
    ValueTask<IReadOnlyList<FactoryEventEnvelope>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Events not yet published, in commit order, claimed for the supplied subscriber.</summary>
    ValueTask<IReadOnlyList<FactoryEventEnvelope>> ClaimUnpublishedAsync(
        SubscriberId subscriber,
        int maxCount,
        CancellationToken cancellationToken = default);

    ValueTask MarkPublishedAsync(FactoryEventId eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an event from dispatch after a bounded number of failed attempts. Quarantine is durable:
    /// the event stays out of the dispatchable set until it is replayed explicitly.
    /// </summary>
    ValueTask QuarantineAsync(
        FactoryEventId eventId,
        string reason,
        int attempts,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<QuarantinedEvent>> ReadQuarantinedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a quarantined event to the dispatchable set exactly once. False when not quarantined.</summary>
    ValueTask<bool> ReplayQuarantinedAsync(FactoryEventId eventId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Durable consumption record keyed by (subscriber, event). A claim succeeds once; acknowledgement is
/// recorded only after the subscriber's results are persisted (F-C.3).
/// </summary>
public interface IEventInbox
{
    /// <summary>Returns false when the pair has already been claimed or acknowledged.</summary>
    ValueTask<bool> TryClaimAsync(
        SubscriberId subscriber,
        FactoryEventId eventId,
        CancellationToken cancellationToken = default);

    ValueTask AcknowledgeAsync(
        SubscriberId subscriber,
        FactoryEventId eventId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsAcknowledgedAsync(
        SubscriberId subscriber,
        FactoryEventId eventId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exclusive execution leases per (work item, stage) with monotonic fencing tokens (F-C.4).
/// </summary>
public interface IEventLease
{
    ValueTask<EventLeaseTicket?> TryAcquireAsync(
        WorkItemId workItemId,
        StageName stage,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a held lease. A superseded ticket cannot release the newer lease.</summary>
    ValueTask<bool> ReleaseAsync(EventLeaseTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="fencingToken"/> is still the newest token for the pair.</summary>
    ValueTask<bool> IsCurrentAsync(
        WorkItemId workItemId,
        StageName stage,
        FencingToken fencingToken,
        CancellationToken cancellationToken = default);
}
