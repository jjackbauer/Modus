using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workflows;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Workflow;

public sealed class BonesLearningWorkflow : IWorkflowDefinition
{
    public WorkflowId WorkflowId => BonesLearningWorkflowIds.WorkflowId;

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
}