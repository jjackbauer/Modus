using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Identifiers;
using Wip.Runtime.Runtime;

namespace Wip.Bones.Agents.Workflow;

public sealed class BonesLearningWorkflow : IWorkflow<BonesLearningWorkflowRequest, BonesLearningWorkflowResult>
{
    public WorkflowId WorkflowId => BonesLearningWorkflowIds.WorkflowId;

    public ValueTask<BonesLearningWorkflowResult> ExecuteAsync(
        BonesLearningWorkflowRequest request,
        WorkflowContext context,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(
            new BonesLearningWorkflowResult(
                GamesObserved: request.GameCount,
                StrategyId: new BonesStrategyId("workflow-dispatch"),
                MatchGameId: new BonesGameId("workflow-dispatch"),
                EnhancedStrategyId: new BonesStrategyId("workflow-dispatch-enhanced")));

    public static LinearWorkflowStageCompilation CompileLinearStages()
        => TypedLinearWorkflowBuilder
            .StartWith<BonesObserveGamesRequest, BonesObserveGamesResult>(WorkflowStageKind.Run)
            .Then<BonesPonderRequest, BonesPonderResult>(WorkflowStageKind.Plan)
            .Map<BonesObserveGamesResult, BonesPonderRequest>()
            .UseTool<BonesPlayMatchRequest, BonesPlayMatchResult>()
            .Map<BonesPonderResult, BonesPlayMatchRequest>()
            .Then<BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>(WorkflowStageKind.Plan)
            .Map<BonesPlayMatchResult, BonesEnhanceStrategyRequest>()
            .Build();
}