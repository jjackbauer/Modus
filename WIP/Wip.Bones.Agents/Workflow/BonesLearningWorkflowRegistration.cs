using Microsoft.Extensions.DependencyInjection;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Builder;

namespace Wip.Bones.Agents.Workflow;

public static class BonesLearningWorkflowExtensions
{
    public static WipBuilder AddBonesLearningWorkflow(this WipBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        BonesLearningWorkflowMapRuntime.Register();

        builder.AddWorkflow<BonesLearningWorkflow, BonesLearningWorkflowRequest, BonesLearningWorkflowResult>(
            BonesLearningWorkflowIds.WorkflowId,
            "Bones learning workflow");

        builder.AddTool<BonesObserveGamesTool, BonesObserveGamesRequest, BonesObserveGamesResult>(
            BonesObserveGamesCapability.Id,
            "Bones observe games tool");

        // Register single-seat agents as DI services first (they are dependencies of co-learning agents)
        builder.Services.AddSingleton<BonesPonderAgent>();
        builder.Services.AddSingleton<BonesEnhanceStrategyAgent>();

        // Register co-learning ponder agent under the ponder capability (handles both modes)
        builder.AddAgent<BonesCoLearningPonderAgent, BonesPonderRequest, BonesPonderResult>(
            BonesPonderCapability.Id,
            "Bones co-learning ponder agent");

        builder.AddTool<BonesPlayMatchTool, BonesPlayMatchRequest, BonesPlayMatchResult>(
            BonesPlayMatchCapability.Id,
            "Bones play match tool");

        // Register co-learning enhance agent under the enhance capability (handles both modes)
        builder.AddAgent<BonesCoLearningEnhanceAgent, BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>(
            BonesEnhanceStrategyCapability.Id,
            "Bones co-learning enhance agent");

        return builder;
    }
}

public static class BonesLearningWorkflowRegistration
{
    public static WipBuilder RegisterWorkflowAndCapabilities(WipBuilder builder)
        => builder.AddBonesLearningWorkflow();
}