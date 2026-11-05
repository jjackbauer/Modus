using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Identifiers;
using Wip.Runtime.Runtime;

namespace Wip.Bones.Agents.Workflow;

public sealed class BonesLearningWorkflow
    : IWorkflow<BonesLearningWorkflowRequest, BonesLearningWorkflowResult>
    , IWorkflowDefinition
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

    /// <summary>
    /// Declarative plan (WP-2.5): the four Bones stages with explicit plan-scoped maps and an
    /// initial request factory. The runtime prefers this plan over the legacy static map registry.
    /// </summary>
    public WorkflowPlan Compile() => new(
        [
            WorkflowStageDescriptor.Create<BonesObserveGamesRequest, BonesObserveGamesResult>(WorkflowStageKind.Run),
            WorkflowStageDescriptor.Create<BonesPonderRequest, BonesPonderResult>(WorkflowStageKind.Plan),
            WorkflowStageDescriptor.Create<BonesPlayMatchRequest, BonesPlayMatchResult>(WorkflowStageKind.Run),
            WorkflowStageDescriptor.Create<BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>(WorkflowStageKind.Plan),
        ],
        [
            new StageMap<BonesObserveGamesResult, BonesPonderRequest>(
                WorkflowStageKind.Run,
                WorkflowStageKind.Plan,
                BonesLearningWorkflowMapRuntime.CreatePonderRequest),
            new StageMap<BonesPonderResult, BonesPlayMatchRequest>(
                WorkflowStageKind.Plan,
                WorkflowStageKind.Run,
                BonesLearningWorkflowMapRuntime.CreatePlayMatchRequest),
            new StageMap<BonesPlayMatchResult, BonesEnhanceStrategyRequest>(
                WorkflowStageKind.Run,
                WorkflowStageKind.Plan,
                BonesLearningWorkflowMapRuntime.CreateEnhanceRequest),
        ],
        initialRequest: session => BonesLearningWorkflowMapRuntime.CreateInitialRequest(
            new WorkflowContext(
                session.SessionId,
                session.TaskDescription,
                session.WorktreePath,
                session.RepositoryPath)));

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