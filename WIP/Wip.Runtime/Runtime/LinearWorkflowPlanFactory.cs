using Wip.Abstractions.Workflows;
using Wip.Builder;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Builds an explicit plan for a legacy linear <c>IWorkflow</c> registration (WP-2.4). The six
/// governance stages and their maps are declared here; there is no reflection property heuristic
/// and no process-global registry.
/// </summary>
public static class LinearWorkflowPlanFactory
{
    public static WorkflowPlan Create(WorkflowRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var stages = WorkflowStageDescriptorMapper.CreateLinear(
            registration.RequestType,
            registration.ResultType);

        return new WorkflowPlan(
            stages,
            [
                new DelegateStageMap(
                    WorkflowStageKind.Plan,
                    WorkflowStageKind.Run,
                    typeof(PlanStageResult),
                    registration.RequestType,
                    (_, context) => registration.CreateRunRequest(context)),
                new DelegateStageMap(
                    WorkflowStageKind.Run,
                    WorkflowStageKind.Validate,
                    registration.ResultType,
                    typeof(ValidateStageRequest),
                    (_, context) => new ValidateStageRequest(context.SessionId.Value)),
                new DelegateStageMap(
                    WorkflowStageKind.Validate,
                    WorkflowStageKind.Review,
                    typeof(ValidateStageResult),
                    typeof(ReviewStageRequest),
                    (_, context) => new ReviewStageRequest(context.SessionId.Value)),
                new DelegateStageMap(
                    WorkflowStageKind.Review,
                    WorkflowStageKind.RequireApproval,
                    typeof(ReviewStageResult),
                    typeof(RequireApprovalStageRequest),
                    (_, context) => new RequireApprovalStageRequest(context.SessionId.Value)),
                new DelegateStageMap(
                    WorkflowStageKind.RequireApproval,
                    WorkflowStageKind.Merge,
                    typeof(RequireApprovalStageResult),
                    typeof(MergeStageRequest),
                    (_, context) => new MergeStageRequest(context.SessionId.Value)),
            ]);
    }

    private sealed class DelegateStageMap(
        WorkflowStageKind fromStage,
        WorkflowStageKind toStage,
        Type sourceType,
        Type targetType,
        Func<object, WorkflowContext, object> map) : StageMap
    {
        public override WorkflowStageKind FromStage => fromStage;

        public override WorkflowStageKind ToStage => toStage;

        public override Type SourceType => sourceType;

        public override Type TargetType => targetType;

        public override string SourceContractName => WorkflowStageDescriptor.ResolveContractName(sourceType);

        public override string TargetContractName => WorkflowStageDescriptor.ResolveContractName(targetType);

        public override object Map(object source, WorkflowContext context)
        {
            ArgumentNullException.ThrowIfNull(source);
            return map(source, context);
        }
    }
}