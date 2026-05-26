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

public interface IWorkflowDescriptor<TRequest, TResult> : IWorkflowDescriptor
    where TRequest : notnull
    where TResult : notnull
{
}

public sealed record WorkflowDescriptor<TRequest, TResult> : IWorkflowDescriptor<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    public WorkflowDescriptor(WorkflowId workflowId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(displayName));

        if (typeof(TRequest) == typeof(object))
            throw new ArgumentException("Request type cannot be object.", nameof(TRequest));

        if (typeof(TResult) == typeof(object))
            throw new ArgumentException("Result type cannot be object.", nameof(TResult));

        WorkflowId = workflowId;
        DisplayName = displayName;
    }

    public WorkflowId WorkflowId { get; }

    public string DisplayName { get; }

    public Type RequestType => typeof(TRequest);

    public Type ResultType => typeof(TResult);
}
