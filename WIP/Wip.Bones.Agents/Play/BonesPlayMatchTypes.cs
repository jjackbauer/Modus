using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Play;

public static class BonesPlayMatchCapability
{
    public static readonly CapabilityId Id = new("tool.bones.play-match");
}

public sealed record BonesPlayMatchRequest(
    int Seed,
    int TargetScore,
    string RepositoryPath,
    BonesGameId? GameId = null,
    string ModelId = "bones-play-turn",
    bool AllSeatsLearning = false);

public sealed record BonesPlayMatchResult(
    BonesGameId GameId,
    BonesPlayerId Winner,
    IReadOnlyDictionary<BonesPlayerId, int> CumulativeScores,
    IReadOnlyList<BonesEvent> Transcript,
    int TurnsPlayed,
    ArtifactDescriptor MatchTranscriptArtifact,
    ArtifactDescriptor ExecutionArtifact,
    IReadOnlyDictionary<BonesPlayerId, ArtifactDescriptor> MatchHistoryArtifacts);

public sealed record BonesPlayTurnOption(BonesMoveId MoveId, string Description);

public sealed record BonesPlayTurnMessage(string Role, string Content);

public sealed record BonesPlayTurnRequest(
    IReadOnlyList<BonesPlayTurnMessage> Messages,
    IReadOnlyList<BonesPlayTurnOption> AllowedMoves,
    BonesPlayerId ActiveSeat,
    BonesStrategyId StrategyId);

public sealed record BonesPlayTurnResult(string SelectedMoveId);