using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Contracts.Triggers;

/// <summary>Which fact activated a role (source §8 "Trigger / input" column, F-G.1–F-G.9).</summary>
public enum RoleTriggerKind
{
    RequirementSubmitted = 1,
    AlignedRequirement = 2,
    ReadyWork = 3,
    ProposedImplementation = 4,
    AcceptedReview = 5,
    MergeCandidate = 6,
    ValidatedCandidate = 7,

    /// <summary>A durable scheduled occurrence emitted through Modus' scheduler (F-N.3).</summary>
    ScheduleOccurrence = 8,

    /// <summary>A production telemetry signal that breached its threshold.</summary>
    TelemetrySignal = 9
}

/// <summary>What activated one role, recorded so an artifact can never hide which trigger produced it.</summary>
public sealed record RoleTrigger(RoleTriggerKind Kind, string Detail)
{
    public static RoleTrigger Of(RoleTriggerKind kind, string detail) => new(kind, detail);
}

/// <summary>One operational observation that may or may not warrant an operations role run.</summary>
public sealed record TelemetrySignal(
    string SignalName,
    double ObservedValue,
    double Threshold,
    WorkItemId WorkItemId)
{
    /// <summary>True when the observation breaches the threshold in the worsening direction.</summary>
    public bool BreachesThreshold => ObservedValue > Threshold;
}
