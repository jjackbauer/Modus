using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Adaptation.Improvement;

/// <summary>The verdict for an attempt to change evaluation machinery.</summary>
public sealed record GateWriteDecision(bool Allowed, string? Reason)
{
    public static GateWriteDecision Permit() => new(true, null);

    public static GateWriteDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Guards the evaluation machinery from the agents being evaluated (F-J.6, F-N.2). A reflection
/// principal may read gate results but may never rewrite the hard gates, holdout labels or its own
/// permission ceiling, because doing so would let a candidate choose the bar it is judged against.
/// </summary>
public sealed class GateWriteGuard
{
    public const string ReflectionCannotWriteCategory = "reflection-cannot-write-evaluation-machinery";

    /// <summary>Hard gates are only writable by an independent, non-reflection authority.</summary>
    public GateWriteDecision TryWriteHardGates(bool actorIsReflection, IReadOnlyList<EvaluationGate> gates)
    {
        ArgumentNullException.ThrowIfNull(gates);

        return actorIsReflection
            ? GateWriteDecision.Deny(ReflectionCannotWriteCategory + ":hard-gates")
            : GateWriteDecision.Permit();
    }

    /// <summary>Holdout labels are never writable by a reflection principal.</summary>
    public GateWriteDecision TryWriteHoldouts(bool actorIsReflection, string holdoutSetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holdoutSetId);

        return actorIsReflection
            ? GateWriteDecision.Deny(ReflectionCannotWriteCategory + ":holdouts")
            : GateWriteDecision.Permit();
    }

    /// <summary>A principal can never widen its own permission ceiling.</summary>
    public GateWriteDecision TryWidenOwnCeiling(bool actorIsReflection, string requestedPermission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPermission);

        return actorIsReflection
            ? GateWriteDecision.Deny(ReflectionCannotWriteCategory + ":ceiling")
            : GateWriteDecision.Deny(ReflectionCannotWriteCategory + ":ceiling-is-an-independent-control");
    }
}
