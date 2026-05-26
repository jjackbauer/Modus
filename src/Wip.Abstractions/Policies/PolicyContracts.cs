using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Policies;

public sealed record PolicyContext(
    SessionId SessionId,
    WorkflowId WorkflowId,
    string WorktreePath,
    string OperationName);

public sealed record PolicyDecision(bool IsAllowed, string Reason)
{
    public static PolicyDecision Allow() => new(true, string.Empty);

    public static PolicyDecision Deny(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(reason));

        return new PolicyDecision(false, reason);
    }
}

public interface IPolicy<in TRequest>
    where TRequest : notnull
{
    PolicyId PolicyId { get; }

    ValueTask<PolicyDecision> EvaluateAsync(TRequest request, PolicyContext context, CancellationToken cancellationToken);
}
