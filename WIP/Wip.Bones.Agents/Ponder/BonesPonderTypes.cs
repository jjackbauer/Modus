using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Ponder;

public static class BonesPonderCapability
{
    public static readonly CapabilityId Id = new("agent.bones.ponder");
}

public sealed record BonesPonderRequest(
    BonesPlayerId PlayerId,
    string RepositoryPath,
    string ModelId = "bones-strategy-author",
    bool AllSeatsLearning = false);

public sealed record BonesPonderResult(
    BonesStrategyId StrategyId,
    BonesStrategyDocument StrategyDocument,
    BonesStrategyArtifact AuthoredStrategy,
    ArtifactDescriptor StrategyArtifact,
    int ObservationsLoaded,
    ArtifactDescriptor ExecutionArtifact);

public sealed record BonesStrategyAuthoringMessage(string Role, string Content);

public sealed record BonesStrategyAuthoringRequest(
    IReadOnlyList<BonesStrategyAuthoringMessage> Messages,
    IReadOnlyList<string> ObservationArtifactPaths);

public sealed record BonesStrategyAuthoringResult(string Markdown);
