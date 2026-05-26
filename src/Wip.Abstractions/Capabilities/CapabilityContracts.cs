using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Capabilities;

public enum CapabilityKind
{
    Agent = 1,
    Tool = 2,
    Validator = 3
}

public sealed record CapabilityContext(SessionId SessionId, string WorktreePath);

public interface ICapability<in TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    ValueTask<TResult> ExecuteAsync(TRequest request, CapabilityContext context, CancellationToken cancellationToken);
}

public interface IAgent<in TRequest, TResult> : ICapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
}

public interface ITool<in TRequest, TResult> : ICapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
}

public interface IValidator<in TRequest, TResult> : ICapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
}
