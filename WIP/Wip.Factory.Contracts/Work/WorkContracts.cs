using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Contracts.Work;

/// <summary>
/// The factory's durable work state (F-A.3). Deliberately a distinct type from
/// <see cref="Wip.Abstractions.Sessions.SessionState"/>: a work item outlives many WIP sessions, so
/// factory business states must never be added to the session enum (F-A.4).
/// </summary>
public enum FactoryWorkState
{
    Proposed = 1,
    RequirementsAligned = 2,
    ArchitectureRefined = 3,
    ReadyForImplementation = 4,
    InImplementation = 5,
    InReview = 6,
    ReadyForDelivery = 7,
    DeliveryValidation = 8,
    ReleaseCandidate = 9,
    Released = 10,
    OperationallyValidated = 11,
    Blocked = 12,
    NeedsRework = 13,
    Failed = 14,
    Cancelled = 15,
    Closed = 16,
}

/// <summary>How a prerequisite relates to a dependent work item.</summary>
public enum DependencyKind
{
    /// <summary>The prerequisite work item must reach a released state.</summary>
    WorkItem = 1,

    /// <summary>A specific artifact version must be published.</summary>
    ArtifactVersion = 2,

    /// <summary>An architecture or design decision must be recorded.</summary>
    Decision = 3,

    /// <summary>A code baseline (commit) must exist.</summary>
    CodeBaseline = 4,
}

/// <summary>A single prerequisite edge declared by a work item.</summary>
public sealed record WorkDependency(WorkItemId Prerequisite, DependencyKind Kind, string Reference);

/// <summary>
/// Immutable view of a work item at a specific aggregate version. The factory commits state changes
/// by compare-and-swap on <see cref="Version"/> (F-C.5); the store rejects a version below
/// <see cref="MinimumVersion"/> before it can be persisted.
/// </summary>
public sealed record WorkItemSnapshot(
    WorkItemId WorkItemId,
    FactoryWorkState State,
    int Version,
    string Repository,
    IReadOnlyList<WorkDependency> Dependencies,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>The lowest legal aggregate version. The first committed state is version 1.</summary>
    public const int MinimumVersion = 1;
}
