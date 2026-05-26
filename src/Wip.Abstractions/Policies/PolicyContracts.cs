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

public interface IPolicy<in TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    PolicyId PolicyId { get; }

    ValueTask<TResult> EvaluateAsync(TRequest request, PolicyContext context, CancellationToken cancellationToken);
}

public interface IPolicy<in TRequest> : IPolicy<TRequest, PolicyDecision>
    where TRequest : notnull
{
}

public interface IPolicyDescriptor
{
    PolicyId PolicyId { get; }

    Type PolicyType { get; }

    Type RequestType { get; }

    Type ResultType { get; }
}

public interface IPolicyDescriptor<TRequest, TResult> : IPolicyDescriptor
    where TRequest : notnull
    where TResult : notnull
{
}

public sealed record PolicyDescriptor<TRequest, TResult> : IPolicyDescriptor<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    public PolicyDescriptor(
        PolicyId policyId,
        Type policyType)
    {
        ArgumentNullException.ThrowIfNull(policyType);

        if (typeof(TRequest) == typeof(object))
            throw new ArgumentException("Request type cannot be object.", nameof(TRequest));

        if (typeof(TResult) == typeof(object))
            throw new ArgumentException("Result type cannot be object.", nameof(TResult));

        if (!typeof(IPolicy<TRequest, TResult>).IsAssignableFrom(policyType))
            throw new ArgumentException(
                $"{policyType} must implement {typeof(IPolicy<TRequest, TResult>)}.",
                nameof(policyType));

        PolicyId = policyId;
        PolicyType = policyType;
    }

    public PolicyId PolicyId { get; }

    public Type PolicyType { get; }

    public Type RequestType => typeof(TRequest);

    public Type ResultType => typeof(TResult);
}

public static class PolicyDescriptor
{
    public static PolicyDescriptor<TRequest, TResult> For<TPolicy, TRequest, TResult>(PolicyId policyId)
        where TPolicy : class, IPolicy<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        return new PolicyDescriptor<TRequest, TResult>(policyId, typeof(TPolicy));
    }
}
