using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Enhance;

public static class BonesEnhanceStrategyCapability
{
    public static readonly CapabilityId Id = new("agent.bones.enhance-strategy");
}

public sealed record BonesEnhanceStrategyRequest(
    BonesPlayerId PlayerId,
    string RepositoryPath,
    BonesGameId? MatchGameId = null,
    string ModelId = "bones-strategy-enhance",
    int? EvaluationSeed = null,
    int? EvaluationTargetScore = null,
    bool AllSeatsLearning = false);

public sealed record BonesEnhanceStrategyResult(
    BonesStrategyId PriorStrategyId,
    BonesStrategyId StrategyId,
    BonesStrategyDocument StrategyDocument,
    ArtifactDescriptor StrategyArtifact,
    int MatchHistoriesLoaded,
    ArtifactDescriptor ExecutionArtifact,
    BonesPromotionDecisionOutcome PromotionOutcome,
    BonesStrategyId ActiveStrategyId,
    ArtifactDescriptor PromotionDecisionArtifact);

public sealed record BonesStrategyEnhancementMessage(string Role, string Content);

public sealed record BonesStrategyEnhancementRequest(
    IReadOnlyList<BonesStrategyEnhancementMessage> Messages,
    IReadOnlyList<string> MatchHistoryArtifactPaths,
    BonesStrategyId PriorStrategyId);

public sealed record BonesStrategyEnhancementResult(string Markdown);

/// <summary>
/// Tracks the outcome of a single enhancement iteration for meta-prompt optimization.
/// </summary>
/// <param name="WinRateDelta">Change in win rate compared to the previous iteration.</param>
/// <param name="ScoreDelta">Change in average score differential compared to the previous iteration.</param>
/// <param name="PromotionOutcome">Whether the enhanced strategy was promoted or rejected.</param>
/// <param name="IterationNumber">The global iteration number (1-based).</param>
public sealed record BonesEnhancementIterationOutcome(
    double WinRateDelta,
    double ScoreDelta,
    BonesPromotionDecisionOutcome PromotionOutcome,
    int IterationNumber);