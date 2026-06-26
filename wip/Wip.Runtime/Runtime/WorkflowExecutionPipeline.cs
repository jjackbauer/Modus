using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;

namespace Wip.Runtime.Runtime;

public enum WorkflowStageKind
{
    Plan = 1,
    Run = 2,
    Validate = 3,
    Review = 4,
    RequireApproval = 5,
    Merge = 6
}

public sealed record WorkflowStageDescriptor(
    WorkflowStageKind Stage,
    Type RequestType,
    Type ResultType,
    string RequestContractName,
    string ResultContractName)
{
    public static WorkflowStageDescriptor Create<TRequest, TResult>(WorkflowStageKind stage)
        where TRequest : notnull
        where TResult : notnull
        => new(
            stage,
            typeof(TRequest),
            typeof(TResult),
            ResolveContractName(typeof(TRequest)),
            ResolveContractName(typeof(TResult)));

    public static string ResolveContractName(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        return contractType.FullName ?? contractType.Name;
    }
}

public sealed record WorkflowStageMapAdapterDescriptor(
    WorkflowStageKind FromStage,
    WorkflowStageKind ToStage,
    Type SourceType,
    Type TargetType,
    string SourceContractName,
    string TargetContractName);

public sealed record LinearWorkflowStageCompilation(
    IReadOnlyList<WorkflowStageDescriptor> StageDescriptors,
    IReadOnlyList<WorkflowStageMapAdapterDescriptor> MapAdapters)
{
    public string? ResolveMappedInputContractName(WorkflowStageKind stage)
        => MapAdapters.FirstOrDefault(adapter => adapter.ToStage == stage)?.TargetContractName;
}

public sealed record WorkflowStageExecution(
    WorkflowStageDescriptor Descriptor,
    SessionState StateAfterStage,
    bool AppliedTransition)
{
    public string? MappedInputContractName { get; init; }
}

public sealed record WorkflowExecutionResult(
    WorkflowId WorkflowId,
    IReadOnlyList<WorkflowStageExecution> Stages);

public static class WorkflowStageDescriptorMapper
{
    public static IReadOnlyList<WorkflowStageDescriptor> CreateLinear(Type runRequestType, Type runResultType)
    {
        ArgumentNullException.ThrowIfNull(runRequestType);
        ArgumentNullException.ThrowIfNull(runResultType);

        return
        [
            WorkflowStageDescriptor.Create<PlanStageRequest, PlanStageResult>(WorkflowStageKind.Plan),
            new WorkflowStageDescriptor(
                WorkflowStageKind.Run,
                runRequestType,
                runResultType,
                WorkflowStageDescriptor.ResolveContractName(runRequestType),
                WorkflowStageDescriptor.ResolveContractName(runResultType)),
            WorkflowStageDescriptor.Create<ValidateStageRequest, ValidateStageResult>(WorkflowStageKind.Validate),
            WorkflowStageDescriptor.Create<ReviewStageRequest, ReviewStageResult>(WorkflowStageKind.Review),
            WorkflowStageDescriptor.Create<RequireApprovalStageRequest, RequireApprovalStageResult>(WorkflowStageKind.RequireApproval),
            WorkflowStageDescriptor.Create<MergeStageRequest, MergeStageResult>(WorkflowStageKind.Merge)
        ];
    }
}

public static class WorkflowBuilderStageCompiler
{
    public static LinearWorkflowStageCompilation CompileLinear(WorkflowRegistration workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return TypedLinearWorkflowBuilder
            .StartWith(WorkflowStageKind.Plan, typeof(PlanStageRequest), typeof(PlanStageResult))
            .UseTool(workflow.RequestType, workflow.ResultType)
            .Map(typeof(PlanStageResult), workflow.RequestType)
            .ValidateWith(typeof(ValidateStageRequest), typeof(ValidateStageResult))
            .Map(workflow.ResultType, typeof(ValidateStageRequest))
            .Then(WorkflowStageKind.Review, typeof(ReviewStageRequest), typeof(ReviewStageResult))
            .Map(typeof(ValidateStageResult), typeof(ReviewStageRequest))
            .RequireHumanApproval(typeof(RequireApprovalStageRequest), typeof(RequireApprovalStageResult))
            .Map(typeof(ReviewStageResult), typeof(RequireApprovalStageRequest))
            .Then(WorkflowStageKind.Merge, typeof(MergeStageRequest), typeof(MergeStageResult))
            .Map(typeof(RequireApprovalStageResult), typeof(MergeStageRequest))
            .Build();
    }

    private static WorkflowStageMapAdapterDescriptor CreateMapAdapter(
        WorkflowStageKind fromStage,
        WorkflowStageKind toStage,
        Type sourceType,
        Type targetType)
        => new(
            fromStage,
            toStage,
            sourceType,
            targetType,
            WorkflowStageDescriptor.ResolveContractName(sourceType),
            WorkflowStageDescriptor.ResolveContractName(targetType));
}

public sealed class TypedLinearWorkflowBuilder
{
    private readonly List<WorkflowStageDescriptor> _stages = [];
    private readonly List<WorkflowStageMapAdapterDescriptor> _mapAdapters = [];

    private TypedLinearWorkflowBuilder()
    {
    }

    public static TypedLinearWorkflowBuilder StartWith<TRequest, TResult>(WorkflowStageKind stage)
        where TRequest : notnull
        where TResult : notnull
        => new TypedLinearWorkflowBuilder().AddStartStage(stage, typeof(TRequest), typeof(TResult));

    public static TypedLinearWorkflowBuilder StartWith(WorkflowStageKind stage, Type requestType, Type resultType)
        => new TypedLinearWorkflowBuilder().AddStartStage(stage, requestType, resultType);

    public TypedLinearWorkflowBuilder Then<TRequest, TResult>(WorkflowStageKind stage)
        where TRequest : notnull
        where TResult : notnull
        => Then(stage, typeof(TRequest), typeof(TResult));

    public TypedLinearWorkflowBuilder Then(WorkflowStageKind stage, Type requestType, Type resultType)
    {
        EnsureNoObjectPayloadContracts(requestType, resultType, nameof(Then));
        EnsureStarted(nameof(Then));
        _stages.Add(CreateStageDescriptor(stage, requestType, resultType));
        return this;
    }

    public TypedLinearWorkflowBuilder UseTool<TRequest, TResult>()
        where TRequest : notnull
        where TResult : notnull
        => UseTool(typeof(TRequest), typeof(TResult));

    public TypedLinearWorkflowBuilder UseTool(Type requestType, Type resultType)
        => Then(WorkflowStageKind.Run, requestType, resultType);

    public TypedLinearWorkflowBuilder ValidateWith<TRequest, TResult>()
        where TRequest : notnull
        where TResult : notnull
        => ValidateWith(typeof(TRequest), typeof(TResult));

    public TypedLinearWorkflowBuilder ValidateWith(Type requestType, Type resultType)
        => Then(WorkflowStageKind.Validate, requestType, resultType);

    public TypedLinearWorkflowBuilder RequireHumanApproval<TRequest, TResult>()
        where TRequest : notnull
        where TResult : notnull
        => RequireHumanApproval(typeof(TRequest), typeof(TResult));

    public TypedLinearWorkflowBuilder RequireHumanApproval(Type requestType, Type resultType)
        => Then(WorkflowStageKind.RequireApproval, requestType, resultType);

    public TypedLinearWorkflowBuilder Map<TSource, TTarget>()
        where TSource : notnull
        where TTarget : notnull
        => Map(typeof(TSource), typeof(TTarget));

    public TypedLinearWorkflowBuilder Map(Type sourceType, Type targetType)
    {
        EnsureNoObjectMapContracts(sourceType, targetType, nameof(Map));
        if (_stages.Count < 2)
        {
            throw new InvalidOperationException("Map requires at least two configured stages.");
        }

        var fromStage = _stages[^2];
        var toStage = _stages[^1];
        _mapAdapters.Add(new WorkflowStageMapAdapterDescriptor(
            fromStage.Stage,
            toStage.Stage,
            sourceType,
            targetType,
            WorkflowStageDescriptor.ResolveContractName(sourceType),
            WorkflowStageDescriptor.ResolveContractName(targetType)));

        return this;
    }

    public LinearWorkflowStageCompilation Build()
    {
        EnsureStarted(nameof(Build));
        return new LinearWorkflowStageCompilation(_stages.ToArray(), _mapAdapters.ToArray());
    }

    private static void EnsureNoObjectPayloadContracts(Type requestType, Type resultType, string method)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(resultType);

        if (requestType == typeof(object) || resultType == typeof(object))
        {
            throw new InvalidOperationException($"{method} requires concrete request/result contracts and rejects object payload contracts.");
        }
    }

    private static void EnsureNoObjectMapContracts(Type sourceType, Type targetType, string method)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);

        if (sourceType == typeof(object) || targetType == typeof(object))
        {
            throw new InvalidOperationException($"{method} requires concrete map contracts and rejects object payload contracts.");
        }
    }

    private void EnsureEmptyBeforeStart()
    {
        if (_stages.Count > 0)
        {
            throw new InvalidOperationException("StartWith can only be called once at the beginning of a linear workflow path.");
        }
    }

    private TypedLinearWorkflowBuilder AddStartStage(WorkflowStageKind stage, Type requestType, Type resultType)
    {
        EnsureNoObjectPayloadContracts(requestType, resultType, nameof(StartWith));
        EnsureEmptyBeforeStart();
        _stages.Add(CreateStageDescriptor(stage, requestType, resultType));
        return this;
    }

    private void EnsureStarted(string method)
    {
        if (_stages.Count == 0)
        {
            throw new InvalidOperationException($"{method} requires StartWith to be called first.");
        }
    }

    private static WorkflowStageDescriptor CreateStageDescriptor(WorkflowStageKind stage, Type requestType, Type resultType)
        => new(
            stage,
            requestType,
            resultType,
            WorkflowStageDescriptor.ResolveContractName(requestType),
            WorkflowStageDescriptor.ResolveContractName(resultType));
}

public static class WorkflowStageStateMapper
{
    public static SessionState ToSessionState(WorkflowStageKind stage)
        => stage switch
        {
            WorkflowStageKind.Plan => SessionState.Editing,
            WorkflowStageKind.Run => SessionState.Editing,
            WorkflowStageKind.Validate => SessionState.Validating,
            WorkflowStageKind.Review => SessionState.AwaitingApproval,
            WorkflowStageKind.RequireApproval => SessionState.Approved,
            WorkflowStageKind.Merge => SessionState.Merged,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported workflow stage.")
        };
}

public sealed record PlanStageRequest(string SessionId);

public sealed record PlanStageResult(bool Planned);

public sealed record ValidateStageRequest(string SessionId);

public sealed record ValidateStageResult(bool IsValid);

public sealed record ReviewStageRequest(string SessionId);

public sealed record ReviewStageResult(bool IsReviewed);

public sealed record RequireApprovalStageRequest(string SessionId);

public sealed record RequireApprovalStageResult(bool IsApproved);

public sealed record MergeStageRequest(string SessionId);

public sealed record MergeStageResult(bool IsMerged);
