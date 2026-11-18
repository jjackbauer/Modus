using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;

namespace Wip.Factory.Persistence;

/// <summary>
/// The local durable factory store (D-02). One append-only log is the whole store: a work-state change
/// and its outgoing events are written as a <b>single</b> record, so a reader can never observe one
/// without the other (F-C.2). A truncated trailing record is discarded on replay, which is what makes
/// a torn write indistinguishable from a write that never happened.
/// </summary>
/// <remarks>
/// A process-wide mutex serializes appends. Fencing counters are derived from the log on load, so
/// tokens stay monotonic across restarts even though lease <i>tickets</i> themselves are ephemeral.
/// </remarks>
public sealed class LocalFactoryStore : IWorkItemStore, IEventOutbox, IEventInbox, IEventLease
{
    private const string LogFileName = "factory.log";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<WorkItemId, WorkItemSnapshot> _snapshots = [];
    private readonly List<StoredEvent> _events = [];
    private readonly Dictionary<string, InboxEntry> _inbox = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LeaseEntry> _leases = new(StringComparer.Ordinal);
    private readonly Dictionary<WorkItemId, long> _maxFencingToken = [];
    private readonly Dictionary<string, QuarantinedEvent> _quarantine = new(StringComparer.Ordinal);

    private bool _loaded;
    private int _tornRecordCount;

    public LocalFactoryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, LogFileName);
    }

    /// <summary>Number of trailing records discarded as torn during the most recent load.</summary>
    public int TornRecordCount => _tornRecordCount;

    public async ValueTask<WorkItemSnapshot?> LoadAsync(
        WorkItemId workItemId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _snapshots.TryGetValue(workItemId, out var snapshot) ? snapshot : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<WorkStoreCommitResult> CommitAsync(
        WorkItemCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();

            var currentVersion = _snapshots.TryGetValue(commit.Snapshot.WorkItemId, out var current)
                ? current.Version
                : 0;

            if (commit.ExpectedVersion != currentVersion)
            {
                return WorkStoreCommitResult.Refused(
                    CommitStatus.VersionConflict,
                    $"Expected aggregate version {commit.ExpectedVersion} but the committed version is {currentVersion}.");
            }

            if (commit.FencingToken is { } token
                && _maxFencingToken.TryGetValue(commit.Snapshot.WorkItemId, out var newest)
                && token.Value < newest)
            {
                return WorkStoreCommitResult.Refused(
                    CommitStatus.StaleFencingToken,
                    $"Fencing token {token.Value} has been superseded by {newest}.");
            }

            if (commit.Snapshot.Version != currentVersion + 1)
            {
                return WorkStoreCommitResult.Refused(
                    CommitStatus.Rejected,
                    $"The snapshot version {commit.Snapshot.Version} must be exactly {currentVersion + 1}.");
            }

            var record = new LogRecord
            {
                Kind = LogKinds.WorkCommit,
                WorkItemId = commit.Snapshot.WorkItemId.Value,
                Snapshot = commit.Snapshot,
                Events = commit.OutgoingEvents.Count == 0 ? null : commit.OutgoingEvents,
                FencingToken = commit.FencingToken?.Value,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);

            return WorkStoreCommitResult.Accepted(commit.Snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<FactoryEventEnvelope>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _events.Select(static stored => stored.Envelope).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<FactoryEventEnvelope>> ClaimUnpublishedAsync(
        SubscriberId subscriber,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        if (maxCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "At least one event must be claimable.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _events
                .Where(stored => !stored.Published
                    && !_quarantine.ContainsKey(stored.Envelope.Id.Value))
                .Take(maxCount)
                .Select(static stored => stored.Envelope)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask MarkPublishedAsync(
        FactoryEventId eventId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            var index = _events.FindIndex(stored => stored.Envelope.Id.Equals(eventId));
            if (index < 0 || _events[index].Published)
            {
                return;
            }

            var record = new LogRecord
            {
                Kind = LogKinds.EventPublished,
                EventId = eventId.Value,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> TryClaimAsync(
        SubscriberId subscriber,
        FactoryEventId eventId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            var key = InboxKey(subscriber, eventId);
            if (_inbox.ContainsKey(key))
            {
                return false;
            }

            var record = new LogRecord
            {
                Kind = LogKinds.InboxClaim,
                Subscriber = subscriber.Value,
                EventId = eventId.Value,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask AcknowledgeAsync(
        SubscriberId subscriber,
        FactoryEventId eventId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            var key = InboxKey(subscriber, eventId);
            if (_inbox.TryGetValue(key, out var entry) && entry.Acknowledged)
            {
                return;
            }

            var record = new LogRecord
            {
                Kind = LogKinds.InboxAcknowledge,
                Subscriber = subscriber.Value,
                EventId = eventId.Value,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> IsAcknowledgedAsync(
        SubscriberId subscriber,
        FactoryEventId eventId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _inbox.TryGetValue(InboxKey(subscriber, eventId), out var entry) && entry.Acknowledged;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<EventLeaseTicket?> TryAcquireAsync(
        WorkItemId workItemId,
        StageName stage,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "A lease must have a positive lifetime.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();

            var key = LeaseKey(workItemId, stage);
            var now = DateTimeOffset.UtcNow;

            if (_leases.TryGetValue(key, out var held) && held.ExpiresAtUtc > now && !held.Released)
            {
                return null;
            }

            var next = _maxFencingToken.TryGetValue(workItemId, out var newest) ? newest + 1 : 1;
            var record = new LogRecord
            {
                Kind = LogKinds.LeaseAcquire,
                WorkItemId = workItemId.Value,
                Stage = stage.Value,
                FencingToken = next,
                RecordedAtUtc = now,
                ExpiresAtUtc = now.Add(timeToLive)
            };

            Append(record);
            Apply(record);

            return new EventLeaseTicket(workItemId, stage, new FencingToken(next), now, now.Add(timeToLive));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> ReleaseAsync(
        EventLeaseTicket ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            var key = LeaseKey(ticket.WorkItemId, ticket.Stage);

            if (!_leases.TryGetValue(key, out var held)
                || held.Released
                || held.FencingToken != ticket.FencingToken.Value)
            {
                return false;
            }

            var record = new LogRecord
            {
                Kind = LogKinds.LeaseRelease,
                WorkItemId = ticket.WorkItemId.Value,
                Stage = ticket.Stage.Value,
                FencingToken = ticket.FencingToken.Value,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> IsCurrentAsync(
        WorkItemId workItemId,
        StageName stage,
        FencingToken fencingToken,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _maxFencingToken.TryGetValue(workItemId, out var newest)
                && newest == fencingToken.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask QuarantineAsync(
        FactoryEventId eventId,
        string reason,
        int attempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            if (_quarantine.ContainsKey(eventId.Value))
            {
                return;
            }

            var record = new LogRecord
            {
                Kind = LogKinds.EventQuarantined,
                EventId = eventId.Value,
                Reason = reason,
                Attempts = attempts,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<QuarantinedEvent>> ReadQuarantinedAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _quarantine.Values.OrderBy(static q => q.QuarantinedAtUtc).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> ReplayQuarantinedAsync(
        FactoryEventId eventId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            if (!_quarantine.ContainsKey(eventId.Value))
            {
                return false;
            }

            var record = new LogRecord
            {
                Kind = LogKinds.EventReplayed,
                EventId = eventId.Value,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            Append(record);
            Apply(record);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string InboxKey(SubscriberId subscriber, FactoryEventId eventId)
        => subscriber.Value + "\u001f" + eventId.Value;

    private static string LeaseKey(WorkItemId workItemId, StageName stage)
        => workItemId.Value + "\u001f" + stage.Value;

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        if (!File.Exists(_logPath))
        {
            return;
        }

        var content = File.ReadAllText(_logPath);
        var lines = content.Split('\n');
        var lastNonEmptyIndex = lines.Length - 1;
        while (lastNonEmptyIndex >= 0 && string.IsNullOrWhiteSpace(lines[lastNonEmptyIndex]))
        {
            lastNonEmptyIndex--;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            LogRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<LogRecord>(line, SerializerOptions);
            }
            catch (JsonException) when (index == lastNonEmptyIndex)
            {
                // Only a trailing record can be a torn write: it was never a committed fact.
                // An unparseable record anywhere else is real corruption and must not be hidden.
                record = null;
            }

            if (record is null)
            {
                _tornRecordCount++;
                continue;
            }

            Apply(record);
        }
    }

    private void Append(LogRecord record)
    {
        var line = JsonSerializer.Serialize(record, SerializerOptions) + "\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);

        using var stream = new FileStream(
            _logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);

        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private void Apply(LogRecord record)
    {
        switch (record.Kind)
        {
            case LogKinds.WorkCommit when record.Snapshot is not null:
            {
                _snapshots[record.Snapshot.WorkItemId] = record.Snapshot;
                if (record.FencingToken is { } token)
                {
                    BumpFencing(record.Snapshot.WorkItemId, token);
                }

                foreach (var envelope in record.Events ?? [])
                {
                    _events.Add(new StoredEvent(envelope, Published: false));
                }

                break;
            }

            case LogKinds.EventPublished when record.EventId is { } eventId:
            {
                var index = _events.FindIndex(stored =>
                    string.Equals(stored.Envelope.Id.Value, eventId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    _events[index] = _events[index] with { Published = true };
                }

                break;
            }

            case LogKinds.InboxClaim when record.Subscriber is { } claimSubscriber && record.EventId is { } claimEvent:
            {
                _inbox[claimSubscriber + "\u001f" + claimEvent] = new InboxEntry(Claimed: true, Acknowledged: false);
                break;
            }

            case LogKinds.InboxAcknowledge when record.Subscriber is { } ackSubscriber && record.EventId is { } ackEvent:
            {
                _inbox[ackSubscriber + "\u001f" + ackEvent] = new InboxEntry(Claimed: true, Acknowledged: true);
                break;
            }

            case LogKinds.LeaseAcquire
                when record.WorkItemId is { } leaseItem
                     && record.Stage is { } leaseStage
                     && record.FencingToken is { } leaseToken:
            {
                var item = new WorkItemId(leaseItem);
                BumpFencing(item, leaseToken);
                _leases[LeaseKey(item, new StageName(leaseStage))] = new LeaseEntry(
                    leaseToken,
                    Claimed: true,
                    Released: false,
                    record.ExpiresAtUtc ?? record.RecordedAtUtc);
                break;
            }

            case LogKinds.EventQuarantined when record.EventId is { } quarantinedId:
            {
                var index = _events.FindIndex(stored =>
                    string.Equals(stored.Envelope.Id.Value, quarantinedId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    var stored = _events[index];
                    _events[index] = stored with { Published = true };
                    _quarantine[quarantinedId] = new QuarantinedEvent(
                        stored.Envelope,
                        record.Reason ?? "unspecified",
                        record.Attempts ?? 0,
                        record.RecordedAtUtc);
                }

                break;
            }

            case LogKinds.EventReplayed when record.EventId is { } replayedId:
            {
                _quarantine.Remove(replayedId);
                var index = _events.FindIndex(stored =>
                    string.Equals(stored.Envelope.Id.Value, replayedId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    _events[index] = _events[index] with { Published = false };
                }

                break;
            }

            case LogKinds.LeaseRelease
                when record.WorkItemId is { } releaseItem
                     && record.Stage is { } releaseStage
                     && record.FencingToken is { } releaseToken:
            {
                var key = LeaseKey(new WorkItemId(releaseItem), new StageName(releaseStage));
                if (_leases.TryGetValue(key, out var existing) && existing.FencingToken == releaseToken)
                {
                    _leases[key] = existing with { Released = true };
                }

                break;
            }
        }
    }

    private void BumpFencing(WorkItemId workItemId, long token)
    {
        var current = _maxFencingToken.TryGetValue(workItemId, out var newest) ? newest : 0;
        if (token > current)
        {
            _maxFencingToken[workItemId] = token;
        }
    }

    private sealed record StoredEvent(FactoryEventEnvelope Envelope, bool Published);

    private sealed record InboxEntry(bool Claimed, bool Acknowledged);

    private sealed record LeaseEntry(long FencingToken, bool Claimed, bool Released, DateTimeOffset ExpiresAtUtc);

    private static class LogKinds
    {
        public const string WorkCommit = "work-commit";
        public const string EventPublished = "event-published";
        public const string InboxClaim = "inbox-claim";
        public const string InboxAcknowledge = "inbox-ack";
        public const string LeaseAcquire = "lease-acquire";
        public const string LeaseRelease = "lease-release";
        public const string EventQuarantined = "event-quarantined";
        public const string EventReplayed = "event-replayed";
    }

    private sealed class LogRecord
    {
        public string Kind { get; set; } = string.Empty;

        public string? WorkItemId { get; set; }

        public WorkItemSnapshot? Snapshot { get; set; }

        public IReadOnlyList<FactoryEventEnvelope>? Events { get; set; }

        public long? FencingToken { get; set; }

        public string? Subscriber { get; set; }

        public string? EventId { get; set; }

        public string? Stage { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; }

        public DateTimeOffset? ExpiresAtUtc { get; set; }

        public string? Reason { get; set; }

        public int? Attempts { get; set; }
    }
}
