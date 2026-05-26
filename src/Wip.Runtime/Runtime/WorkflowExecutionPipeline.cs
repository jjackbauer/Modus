using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;

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
    Type ResultType)
{
    public static WorkflowStageDescriptor Create<TRequest, TResult>(WorkflowStageKind stage)
        where TRequest : notnull
        where TResult : notnull
        => new(stage, typeof(TRequest), typeof(TResult));
}

public sealed record WorkflowStageExecution(
    WorkflowStageDescriptor Descriptor,
    SessionState StateAfterStage,
    bool AppliedTransition);

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
            new WorkflowStageDescriptor(WorkflowStageKind.Run, runRequestType, runResultType),
            WorkflowStageDescriptor.Create<ValidateStageRequest, ValidateStageResult>(WorkflowStageKind.Validate),
            WorkflowStageDescriptor.Create<ReviewStageRequest, ReviewStageResult>(WorkflowStageKind.Review),
            WorkflowStageDescriptor.Create<RequireApprovalStageRequest, RequireApprovalStageResult>(WorkflowStageKind.RequireApproval),
            WorkflowStageDescriptor.Create<MergeStageRequest, MergeStageResult>(WorkflowStageKind.Merge)
        ];
    }
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
