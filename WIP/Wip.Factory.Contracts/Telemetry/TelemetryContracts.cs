using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Contracts.Telemetry;

/// <summary>One sanitized cross-session trace (F-I.1). Raw tool arguments are never stored here.</summary>
public sealed record AgentSessionTrace(
    SessionId SessionId,
    WorkItemId WorkItemId,
    AgentDefinitionId AgentDefinitionId,
    AgentProfileVersion AgentProfileVersion,
    ModelId ModelId,
    int InputTokens,
    int OutputTokens,
    string Outcome,
    string? FailureCategory,
    IReadOnlyList<string> ToolNames,
    string Summary,
    DateTimeOffset RecordedAtUtc);

/// <summary>A normalized failure cluster for cross-session analysis (F-I.2).</summary>
public sealed record FailureCluster(string Category, int Occurrences, IReadOnlyList<SessionId> Sessions);

/// <summary>
/// Cross-session evidence. Traces are sanitized before they are persisted, so a secret can never be
/// recovered from the store (F-I.5).
/// </summary>
public interface IAgentSessionTraceStore
{
    ValueTask AppendAsync(AgentSessionTrace trace, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AgentSessionTrace>> QueryAsync(
        WorkItemId workItemId,
        CancellationToken cancellationToken = default);
}

/// <summary>Searchable summaries and failure clusters across sessions (F-I.2).</summary>
public interface IAgentExperienceIndex
{
    ValueTask<IReadOnlyList<FailureCluster>> FailureClustersAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AgentSessionTrace>> RecentAsync(int count, CancellationToken cancellationToken = default);
}

/// <summary>A downstream observation linked back to the work that produced it (F-I.3).</summary>
public sealed record DownstreamOutcome(
    OutcomeId OutcomeId,
    WorkItemId WorkItemId,
    SessionId SessionId,
    string ObservationKind,
    double Value,
    string AttributionUncertainty,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Links work → session → artifact → commit → release → observation without mutating earlier
/// observations, and always carries its attribution uncertainty (F-I.3, F-I.6).
/// </summary>
public interface IOutcomeAttributionStore
{
    ValueTask LinkAsync(DownstreamOutcome outcome, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<DownstreamOutcome>> LinksForAsync(
        WorkItemId workItemId,
        CancellationToken cancellationToken = default);
}

/// <summary>The provenance every factory artifact must retain (F-I.4, source §10.2).</summary>
public sealed record ArtifactProvenance(
    ArtifactId ArtifactId,
    SessionId SessionId,
    AgentDefinitionId AgentDefinitionId,
    AgentProfileVersion AgentProfileVersion,
    IReadOnlyList<string> CapabilityVersions,
    string PluginOrigin,
    ModelId ModelId,
    string CodeCommit,
    string ContentDigest)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(PluginOrigin)
        && !string.IsNullOrWhiteSpace(ModelId.Value)
        && !string.IsNullOrWhiteSpace(CodeCommit)
        && !string.IsNullOrWhiteSpace(ContentDigest);
}
