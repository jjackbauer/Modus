using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Workflow;

/// <summary>
/// Co-learning ponder stage that runs Ponder for all seats when AllSeatsLearning=true,
/// or falls back to single-seat Ponder for backward compatibility.
/// </summary>
public sealed class BonesCoLearningPonderTool : ITool<BonesCoLearningPonderRequest, BonesCoLearningPonderResult>
{
    private readonly IArtifactStore _artifactStore;
    private readonly BonesStrategyLibrary _strategyLibrary;
    private readonly BonesPonderAgent _ponderAgent;

    public BonesCoLearningPonderTool(
        IArtifactStore artifactStore,
        BonesStrategyLibrary strategyLibrary,
        BonesPonderAgent ponderAgent)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _strategyLibrary = strategyLibrary ?? throw new ArgumentNullException(nameof(strategyLibrary));
        _ponderAgent = ponderAgent ?? throw new ArgumentNullException(nameof(ponderAgent));
    }

    public async ValueTask<BonesCoLearningPonderResult> ExecuteAsync(
        BonesCoLearningPonderRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var orchestrator = new BonesCoLearningOrchestrator(
            _artifactStore,
            _strategyLibrary,
            ponderAgent: _ponderAgent);

        return await orchestrator.RunPonderStageAsync(request, context, cancellationToken);
    }
}

/// <summary>
/// Co-learning enhance stage that runs Enhance for all seats after a match.
/// When AllSeatsLearning=false, falls back to single-seat Enhance for backward compatibility.
/// </summary>
public sealed class BonesCoLearningEnhanceTool : ITool<BonesCoLearningEnhanceRequest, BonesCoLearningEnhanceResult>
{
    private readonly IArtifactStore _artifactStore;
    private readonly BonesStrategyLibrary _strategyLibrary;
    private readonly BonesEnhanceStrategyAgent _enhanceAgent;

    public BonesCoLearningEnhanceTool(
        IArtifactStore artifactStore,
        BonesStrategyLibrary strategyLibrary,
        BonesEnhanceStrategyAgent enhanceAgent)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _strategyLibrary = strategyLibrary ?? throw new ArgumentNullException(nameof(strategyLibrary));
        _enhanceAgent = enhanceAgent ?? throw new ArgumentNullException(nameof(enhanceAgent));
    }

    public async ValueTask<BonesCoLearningEnhanceResult> ExecuteAsync(
        BonesCoLearningEnhanceRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var orchestrator = new BonesCoLearningOrchestrator(
            _artifactStore,
            _strategyLibrary,
            enhanceAgent: _enhanceAgent);

        return await orchestrator.RunEnhanceStageAsync(request, context, cancellationToken);
    }
}

/// <summary>
/// Capability identifiers for co-learning stages.
/// </summary>
public static class BonesCoLearningPonderCapability
{
    public static readonly CapabilityId Id = new("tool.bones.co-learning-ponder");
}

public static class BonesCoLearningEnhanceCapability
{
    public static readonly CapabilityId Id = new("tool.bones.co-learning-enhance");
}
