using Wip.Abstractions.Artifacts;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Workflow;

/// <summary>
/// Request to run co-learning ponder for all seats that need it.
/// Produced after Observe stage. When AllSeatsLearning is false,
/// only the LearningPlayerId seat is processed (backward compatible).
/// </summary>
public sealed record BonesCoLearningPonderRequest(
    string RepositoryPath,
    BonesPlayerId? LearningPlayerId = null,
    string? ModelId = null,
    bool AllSeatsLearning = true);

/// <summary>
/// Result of co-learning ponder stage — contains per-seat outcomes.
/// </summary>
public sealed record BonesCoLearningPonderResult(
    IReadOnlyList<BonesPonderResult> CompletedPonders,
    IReadOnlyList<BonesCoLearningSeatFailure> FailedPonders);

/// <summary>
/// Request to run co-learning enhance for all seats after a match.
/// When AllSeatsLearning is false, only the LearningPlayerId seat is enhanced.
/// </summary>
public sealed record BonesCoLearningEnhanceRequest(
    string RepositoryPath,
    BonesGameId MatchGameId,
    BonesPlayerId? LearningPlayerId = null,
    int? EvaluationTargetScore = null,
    string? ModelId = null,
    bool AllSeatsLearning = true);

/// <summary>
/// Result of co-learning enhance stage — contains per-seat outcomes.
/// </summary>
public sealed record BonesCoLearningEnhanceResult(
    IReadOnlyList<BonesEnhanceStrategyResult> CompletedEnhances,
    IReadOnlyList<BonesCoLearningSeatFailure> FailedEnhances);

/// <summary>
/// Records a failure for a specific seat in a co-learning stage.
/// </summary>
public sealed record BonesCoLearningSeatFailure(
    int Seat,
    string Reason,
    ArtifactDescriptor? FailureArtifact = null);
