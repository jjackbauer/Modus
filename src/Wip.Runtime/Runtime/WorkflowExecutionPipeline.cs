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

        var stages = WorkflowStageDescriptorMapper.CreateLinear(workflow.RequestType, workflow.ResultType);

        var mapAdapters =
            new WorkflowStageMapAdapterDescriptor[]
            {
                CreateMapAdapter(WorkflowStageKind.Plan, WorkflowStageKind.Run, typeof(PlanStageResult), workflow.RequestType),
                CreateMapAdapter(WorkflowStageKind.Run, WorkflowStageKind.Validate, workflow.ResultType, typeof(ValidateStageRequest)),
                CreateMapAdapter(WorkflowStageKind.Validate, WorkflowStageKind.Review, typeof(ValidateStageResult), typeof(ReviewStageRequest)),
                CreateMapAdapter(WorkflowStageKind.Review, WorkflowStageKind.RequireApproval, typeof(ReviewStageResult), typeof(RequireApprovalStageRequest)),
                CreateMapAdapter(WorkflowStageKind.RequireApproval, WorkflowStageKind.Merge, typeof(RequireApprovalStageResult), typeof(MergeStageRequest))
            };

        return new LinearWorkflowStageCompilation(stages, mapAdapters);
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
