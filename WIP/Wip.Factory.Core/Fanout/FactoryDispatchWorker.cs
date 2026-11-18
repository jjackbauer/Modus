using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;

namespace Wip.Factory.Core.Fanout;

/// <summary>
/// The deterministic dispatch loop (F-C.6). It claims committed-but-unpublished events, evaluates
/// dependency readiness against the committed aggregate, invokes the stage handler, and commits the
/// resulting state and events atomically. It never decides <i>what</i> work to do — only whether work
/// is eligible — so there is no supervisor agent in this path (M-13).
/// </summary>
/// <remarks>
/// Restart safety comes from the durable inbox and outbox: an event already acknowledged is skipped
/// without side effects, and an event that was claimed but never acknowledged is retried. Attempt
/// counts for poison detection are held in memory for the alpha; quarantine itself is durable.
/// </remarks>
public sealed class FactoryDispatchWorker
{
    private readonly IWorkItemStore _workItemStore;
    private readonly IEventOutbox _outbox;
    private readonly IEventInbox _inbox;
    private readonly IStageDispatchHandler _handler;
    private readonly FactoryDispatchOptions _options;
    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);

    public FactoryDispatchWorker(
        IWorkItemStore workItemStore,
        IEventOutbox outbox,
        IEventInbox inbox,
        IStageDispatchHandler handler,
        FactoryDispatchOptions options)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Runs one pass. Returns what happened so a host can report it.</summary>
    public async ValueTask<FactoryDispatchRunReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claimed = await _outbox
            .ClaimUnpublishedAsync(_options.Subscriber, _options.MaxEventsPerRun, cancellationToken)
            .ConfigureAwait(false);

        var dispatched = 0;
        var blocked = 0;
        var duplicates = 0;
        var skipped = 0;
        var failed = 0;
        var quarantined = 0;

        foreach (var envelope in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _inbox.IsAcknowledgedAsync(_options.Subscriber, envelope.Id, cancellationToken).ConfigureAwait(false))
            {
                await _outbox.MarkPublishedAsync(envelope.Id, cancellationToken).ConfigureAwait(false);
                duplicates++;
                continue;
            }

            var dispatch = await TryDispatchAsync(envelope, cancellationToken).ConfigureAwait(false);

            switch (dispatch)
            {
                case DispatchDisposition.Dispatched:
                    dispatched++;
                    break;
                case DispatchDisposition.Blocked:
                    blocked++;
                    break;
                case DispatchDisposition.Skipped:
                    skipped++;
                    break;
                case DispatchDisposition.Quarantined:
                    quarantined++;
                    break;
                case DispatchDisposition.Retry:
                    failed++;
                    break;
            }
        }

        return new FactoryDispatchRunReport(claimed.Count, dispatched, blocked, duplicates, skipped, failed, quarantined);
    }

    private enum DispatchDisposition
    {
        Dispatched,
        Blocked,
        Skipped,
        Retry,
        Quarantined
    }

    private async ValueTask<DispatchDisposition> TryDispatchAsync(
        FactoryEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DispatchCoreAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await RecordFailureAsync(envelope, exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<DispatchDisposition> DispatchCoreAsync(
        FactoryEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!envelope.Data.TryGetValue(FactoryDispatchOptions.WorkItemDataKey, out var workItemValue)
            || string.IsNullOrWhiteSpace(workItemValue))
        {
            return await RecordFailureAsync(
                envelope,
                $"Event '{envelope.Id.Value}' carries no '{FactoryDispatchOptions.WorkItemDataKey}'.",
                cancellationToken).ConfigureAwait(false);
        }

        var workItemId = new WorkItemId(workItemValue);
        var snapshot = await _workItemStore.LoadAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return await RecordFailureAsync(
                envelope,
                $"Work item '{workItemValue}' is not committed.",
                cancellationToken).ConfigureAwait(false);
        }

        var committedStates = await LoadPrerequisiteStatesAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var satisfiedReferences = new HashSet<string>(StringComparer.Ordinal);
        var ready = DependencyReadiness.IsSatisfied(snapshot, committedStates, satisfiedReferences);

        if (!ready)
        {
            if (snapshot.State == FactoryWorkState.Blocked)
            {
                await AcknowledgeAsync(envelope, cancellationToken).ConfigureAwait(false);
                return DispatchDisposition.Blocked;
            }

            var blockTransition = WorkStateMachine.Transition(snapshot.State, FactoryWorkState.Blocked);
            if (blockTransition.Succeeded)
            {
                var committed = snapshot with { State = FactoryWorkState.Blocked, Version = snapshot.Version + 1 };
                var commit = await _workItemStore.CommitAsync(
                    new WorkItemCommit(
                        committed,
                        snapshot.Version,
                        [BuildBlockedEvent(committed.WorkItemId, envelope)]),
                    cancellationToken).ConfigureAwait(false);

                if (!commit.Succeeded)
                {
                    return await RecordFailureAsync(envelope, commit.Reason ?? "block commit refused.", cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await AcknowledgeAsync(envelope, cancellationToken).ConfigureAwait(false);
            return DispatchDisposition.Blocked;
        }

        // A work item woken by its last dependency leaves Blocked before it can be dispatched.
        if (snapshot.State == FactoryWorkState.Blocked)
        {
            var wake = WorkStateMachine.Transition(snapshot.State, FactoryWorkState.ReadyForImplementation);
            if (!wake.Succeeded)
            {
                return await RecordFailureAsync(envelope, wake.RejectionReason ?? "wake refused.", cancellationToken)
                    .ConfigureAwait(false);
            }

            var woken = snapshot with { State = FactoryWorkState.ReadyForImplementation, Version = snapshot.Version + 1 };
            var wakeCommit = await _workItemStore.CommitAsync(
                new WorkItemCommit(woken, snapshot.Version, []),
                cancellationToken).ConfigureAwait(false);

            if (!wakeCommit.Succeeded)
            {
                return await RecordFailureAsync(envelope, wakeCommit.Reason ?? "wake commit refused.", cancellationToken)
                    .ConfigureAwait(false);
            }

            snapshot = woken;
        }

        var outcome = await _handler.HandleAsync(snapshot, envelope, cancellationToken).ConfigureAwait(false);

        switch (outcome.Kind)
        {
            case StageDispatchKind.NoOp:
                await AcknowledgeAsync(envelope, cancellationToken).ConfigureAwait(false);
                return DispatchDisposition.Skipped;

            case StageDispatchKind.Failed:
                return await RecordFailureAsync(
                    envelope,
                    outcome.FailureCategory ?? "stage reported failure",
                    cancellationToken).ConfigureAwait(false);

            case StageDispatchKind.Advanced:
            {
                if (outcome.NextState is not { } nextState)
                {
                    return await RecordFailureAsync(envelope, "advance without a target state", cancellationToken)
                        .ConfigureAwait(false);
                }

                var transition = WorkStateMachine.Transition(snapshot.State, nextState);
                if (!transition.Succeeded)
                {
                    return await RecordFailureAsync(
                        envelope,
                        transition.RejectionReason ?? "illegal stage transition",
                        cancellationToken).ConfigureAwait(false);
                }

                var advanced = snapshot with { State = nextState, Version = snapshot.Version + 1 };
                var commit = await _workItemStore.CommitAsync(
                    new WorkItemCommit(advanced, snapshot.Version, outcome.Events),
                    cancellationToken).ConfigureAwait(false);

                if (!commit.Succeeded)
                {
                    return await RecordFailureAsync(envelope, commit.Reason ?? "advance commit refused.", cancellationToken)
                        .ConfigureAwait(false);
                }

                await AcknowledgeAsync(envelope, cancellationToken).ConfigureAwait(false);
                _attempts.Remove(envelope.Id.Value);
                return DispatchDisposition.Dispatched;
            }

            case StageDispatchKind.Recorded:
            {
                // Facts advance the aggregate version but never the state (v0.3 3.2).
                var recorded = snapshot with { Version = snapshot.Version + 1 };
                var recordCommit = await _workItemStore.CommitAsync(
                    new WorkItemCommit(recorded, snapshot.Version, outcome.Events),
                    cancellationToken).ConfigureAwait(false);

                if (!recordCommit.Succeeded)
                {
                    return await RecordFailureAsync(envelope, recordCommit.Reason ?? "record commit refused.", cancellationToken)
                        .ConfigureAwait(false);
                }

                await AcknowledgeAsync(envelope, cancellationToken).ConfigureAwait(false);
                _attempts.Remove(envelope.Id.Value);
                return DispatchDisposition.Dispatched;
            }

            default:
                return await RecordFailureAsync(envelope, "unknown stage outcome", cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<Dictionary<WorkItemId, FactoryWorkState>> LoadPrerequisiteStatesAsync(
        WorkItemSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<WorkItemId, FactoryWorkState>();
        foreach (var dependency in snapshot.Dependencies.Where(static d => d.Kind == DependencyKind.WorkItem))
        {
            var prerequisite = await _workItemStore.LoadAsync(dependency.Prerequisite, cancellationToken)
                .ConfigureAwait(false);
            if (prerequisite is not null)
            {
                states[prerequisite.WorkItemId] = prerequisite.State;
            }
        }

        return states;
    }

    private async ValueTask<DispatchDisposition> RecordFailureAsync(
        FactoryEventEnvelope envelope,
        string reason,
        CancellationToken cancellationToken)
    {
        var attempts = _attempts.TryGetValue(envelope.Id.Value, out var current) ? current + 1 : 1;
        _attempts[envelope.Id.Value] = attempts;

        if (attempts < _options.MaxAttemptsPerEvent)
        {
            // Leave the event unpublished so the next pass retries it.
            return DispatchDisposition.Retry;
        }

        await _outbox.QuarantineAsync(envelope.Id, reason, attempts, cancellationToken).ConfigureAwait(false);
        // A quarantined event was never successfully processed, so it must not be acknowledged in the
        // inbox: acknowledgement is what would make a later replay look like a duplicate.
        await _outbox.MarkPublishedAsync(envelope.Id, cancellationToken).ConfigureAwait(false);
        _attempts.Remove(envelope.Id.Value);
        return DispatchDisposition.Quarantined;
    }

    private async ValueTask AcknowledgeAsync(FactoryEventEnvelope envelope, CancellationToken cancellationToken)
    {
        await _inbox.TryClaimAsync(_options.Subscriber, envelope.Id, cancellationToken).ConfigureAwait(false);
        await _inbox.AcknowledgeAsync(_options.Subscriber, envelope.Id, cancellationToken).ConfigureAwait(false);
        await _outbox.MarkPublishedAsync(envelope.Id, cancellationToken).ConfigureAwait(false);
    }

    private static FactoryEventEnvelope BuildBlockedEvent(WorkItemId workItemId, FactoryEventEnvelope trigger)
        => new(
            new FactoryEventId(Guid.NewGuid().ToString("N")),
            FactoryEventTypes.WorkBlocked,
            "/factory/dispatch",
            $"work/{workItemId.Value}",
            DateTimeOffset.UtcNow,
            trigger.CorrelationId,
            trigger.Id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FactoryDispatchOptions.WorkItemDataKey] = workItemId.Value
            });
}
