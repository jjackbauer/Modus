using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;

namespace Wip.Factory.Core.Fanout;

/// <summary>What a stage handler decided to do with a dispatched work item.</summary>
public enum StageDispatchKind
{
    /// <summary>Work advanced; the returned state must be a legal successor.</summary>
    Advanced = 1,

    /// <summary>Nothing to do for this trigger; no state change.</summary>
    NoOp = 2,

    /// <summary>The stage failed; the attempt is retried until the bound is reached.</summary>
    Failed = 3,

    /// <summary>
    /// Durable facts and events were produced but the work state must not change (v0.3 3.2).
    /// Used for contract proposals and evidence that must survive without advancing the aggregate.
    /// </summary>
    Recorded = 4
}

/// <summary>Result of one stage dispatch.</summary>
public sealed record StageDispatchOutcome(
    StageDispatchKind Kind,
    FactoryWorkState? NextState,
    IReadOnlyList<FactoryEventEnvelope> Events,
    string? FailureCategory)
{
    public static StageDispatchOutcome Advanced(
        FactoryWorkState nextState,
        IReadOnlyList<FactoryEventEnvelope>? events = null)
        => new(StageDispatchKind.Advanced, nextState, events ?? [], null);

    public static StageDispatchOutcome NoOp() => new(StageDispatchKind.NoOp, null, [], null);

    /// <summary>Persists evidence and events while preserving the work state.</summary>
    public static StageDispatchOutcome Recorded(IReadOnlyList<FactoryEventEnvelope> events)
        => new(StageDispatchKind.Recorded, null, events, null);

    public static StageDispatchOutcome Failed(string failureCategory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCategory);
        return new(StageDispatchKind.Failed, null, [], failureCategory);
    }
}

/// <summary>
/// Executes one stage for a work item. The dispatcher owns readiness, leasing and state commits; a
/// handler owns only the stage's own work (source §5.5: the dispatcher is deterministic
/// infrastructure, not an AI supervisor).
/// </summary>
public interface IStageDispatchHandler
{
    ValueTask<StageDispatchOutcome> HandleAsync(
        WorkItemSnapshot workItem,
        FactoryEventEnvelope trigger,
        CancellationToken cancellationToken = default);
}

/// <summary>Worker configuration.</summary>
public sealed record FactoryDispatchOptions(
    SubscriberId Subscriber,
    int MaxAttemptsPerEvent = 3,
    int MaxEventsPerRun = 32)
{
    public const string WorkItemDataKey = "workItemId";
}

/// <summary>What one worker pass did.</summary>
public sealed record FactoryDispatchRunReport(
    int Claimed,
    int Dispatched,
    int Blocked,
    int Duplicates,
    int Skipped,
    int Failed,
    int Quarantined)
{
    public static FactoryDispatchRunReport Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
