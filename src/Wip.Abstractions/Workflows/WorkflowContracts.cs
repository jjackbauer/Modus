using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Workflows;

public sealed record WorkflowContext(
    SessionId SessionId,
    string Task,
    string WorktreePath);

public interface IWorkflow<in TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    WorkflowId WorkflowId { get; }

    ValueTask<TResult> ExecuteAsync(TRequest request, WorkflowContext context, CancellationToken cancellationToken);
}

public interface IWorkflowDescriptor
{
    WorkflowId WorkflowId { get; }

    string DisplayName { get; }

    Type RequestType { get; }

    Type ResultType { get; }
}

public sealed record WorkflowDescriptor<TRequest, TResult>(
    WorkflowId WorkflowId,
    string DisplayName) : IWorkflowDescriptor
    where TRequest : notnull
    where TResult : notnull
{
    public Type RequestType => typeof(TRequest);

    public Type ResultType => typeof(TResult);
}
