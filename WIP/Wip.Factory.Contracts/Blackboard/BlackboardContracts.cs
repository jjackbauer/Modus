using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Contracts.Blackboard;

/// <summary>The six logical views the blackboard resolves across (source §6, F-N.6).</summary>
public enum BlackboardView
{
    WorkAndDependencies = 1,
    ProductAndArchitectureKnowledge = 2,
    GitAndDelivery = 3,
    SessionsAndExperience = 4,
    OperationalObservations = 5,
    AgentCapabilities = 6
}

/// <summary>How widely a reference may be read.</summary>
public enum BlackboardSensitivity
{
    /// <summary>Readable by any principal.</summary>
    Shared = 1,

    /// <summary>Readable only by the role that produced it.</summary>
    RolePrivate = 2,

    /// <summary>Evaluation holdout: never readable by a reflection principal (F-J.6, F-H.3).</summary>
    Holdout = 3
}

/// <summary>
/// A version-pinned reference the blackboard hands out. It carries the durable owner so a consumer can
/// tell which backing store owns the fact (F-N.6) and the digest so a version can never be ambiguous.
/// </summary>
public sealed record BlackboardReference(
    ArtifactId ArtifactId,
    string Version,
    BlackboardView View,
    string DurableOwner,
    string ContentDigest,
    BlackboardSensitivity Sensitivity,
    string ProducingRole,
    int EstimatedTokens);

/// <summary>What the caller needs, and how much of it fits.</summary>
public sealed record ContextQuery(
    WorkItemId WorkItemId,
    AgentDefinitionId AgentDefinitionId,
    AgentProfileVersion AgentProfileVersion,
    IReadOnlyList<BlackboardView> RequestedViews,
    int TokenBudget);

/// <summary>Who is asking. A reflection principal is never granted holdout material.</summary>
public sealed record AgentExecutionIdentity(
    AgentDefinitionId AgentDefinitionId,
    AgentProfileVersion AgentProfileVersion,
    SessionId SessionId,
    string Role,
    bool IsReflection = false);

/// <summary>The resolved bundle, including exactly what was left out and why.</summary>
public sealed record ContextBundle(
    IReadOnlyList<BlackboardReference> References,
    IReadOnlyList<BlackboardReference> Withheld,
    IReadOnlyList<string> TruncatedArtifactIds,
    bool TruncatedDueToBudget,
    int EstimatedTokens)
{
    public IReadOnlyList<BlackboardView> CoveredViews
        => References.Select(static reference => reference.View).Distinct().ToArray();
}

/// <summary>
/// The logical collaboration interface across the factory's owned stores (F-H.1–F-H.4). It returns
/// references, never a mutable bucket, and it scopes every answer to the asking principal.
/// </summary>
public interface IFactoryBlackboard
{
    ValueTask<ContextBundle> ResolveAsync(
        ContextQuery query,
        AgentExecutionIdentity principal,
        CancellationToken cancellationToken = default);

    ValueTask<BlackboardReference> PublishAsync(
        BlackboardReference artifact,
        AgentExecutionIdentity producer,
        CancellationToken cancellationToken = default);
}
