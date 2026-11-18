using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Contracts.Events;

/// <summary>
/// The committed-fact envelope (M-04, source §5.4). An event notifies that something happened; it is
/// never the source of truth for mutable state, and it carries references rather than full context.
/// </summary>
public sealed record FactoryEventEnvelope(
    FactoryEventId Id,
    string Type,
    string Source,
    string Subject,
    DateTimeOffset TimeUtc,
    CorrelationId? CorrelationId,
    FactoryEventId? CausationId,
    IReadOnlyDictionary<string, string> Data)
{
    /// <summary>The required envelope specversion for this alpha.</summary>
    public const string SpecVersion = "1.0";
}

/// <summary>Well-known factory event type names, versioned by suffix.</summary>
public static class FactoryEventTypes
{
    public const string RequirementSubmitted = "factory.requirement.submitted.v1";

    /// <summary>A structured contract proposal was recorded without advancing the work state.</summary>
    public const string ContractProposed = "factory.contract.proposed.v1";

    /// <summary>An exact proposal digest was authorised; the contract is now frozen.</summary>
    public const string ContractApproved = "factory.contract.approved.v1";
    public const string RequirementAligned = "factory.requirement.aligned.v1";
    public const string RequirementRefined = "factory.requirement.refined.v1";
    public const string WorkReady = "factory.work.ready.v1";
    public const string WorkBlocked = "factory.work.blocked.v1";
    public const string WorkFailed = "factory.work.failed.v1";
    public const string ImplementationProposed = "factory.implementation.proposed.v1";
    public const string ReviewAccepted = "factory.review.accepted.v1";
    public const string ReviewRejected = "factory.review.rejected.v1";
    public const string DeliveryValidated = "factory.delivery.validated.v1";
    public const string ReleaseAuthorized = "factory.release.authorized.v1";
    public const string ReleaseCompleted = "factory.release.completed.v1";
    public const string OperationsAnomalyDetected = "factory.operations.anomaly-detected.v1";
    public const string ReflectionRequested = "factory.agent.reflection-requested.v1";
    public const string CandidateEvaluated = "factory.agent.candidate-evaluated.v1";
    public const string ProfilePromoted = "factory.agent.profile-promoted.v1";
    public const string ProfileRolledBack = "factory.agent.profile-rolled-back.v1";
    public const string CapabilityAdmissionRequested = "factory.capability.admission-requested.v1";
    public const string CapabilityPromoted = "factory.capability.promoted.v1";
    public const string CapabilityRolledBack = "factory.capability.rolled-back.v1";
}
