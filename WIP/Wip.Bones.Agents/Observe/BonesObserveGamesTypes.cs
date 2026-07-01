using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Observe;

public static class BonesObserveGamesCapability
{
    public static readonly CapabilityId Id = new("tool.bones.observe-games");
}

public sealed record BonesObserveGamesRequest(
    int GameCount,
    int Seed,
    int TargetScore,
    IReadOnlyList<BonesPlayerId> ObserverPlayerIds,
    string RepositoryPath);

public sealed record BonesObserveGamesResult(
    int GamesObserved,
    int TranscriptsPersisted,
    IReadOnlyDictionary<BonesPlayerId, int> ObservationsPerPlayer,
    ArtifactDescriptor ExecutionArtifact);