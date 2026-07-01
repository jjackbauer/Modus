using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Multi-dimensional process reward components computed from match results
/// and strategy history. Goes beyond binary win/loss to provide richer
/// incentive signals.
/// </summary>
public sealed record BonesProcessRewards(
    double ScoreDeltaReward,
    double CompilationSuccessReward,
    double DiversityNoveltyReward,
    double AggregateReward);

/// <summary>
/// Computes multi-dimensional rewards beyond win/loss:
/// score delta reward, compilation success reward, diversity/novelty reward.
/// </summary>
public sealed class BonesProcessRewardCalculator
{
    /// <summary>
    /// Default reward granted when a strategy compiles successfully.
    /// </summary>
    public const double DefaultCompilationSuccessReward = 0.5;

    /// <summary>
    /// Similarity threshold above which a candidate source is considered
    /// a near-duplicate of a previously rejected strategy.
    /// </summary>
    public const double DuplicateSimilarityThreshold = 0.85;

    /// <summary>
    /// Penalty applied when a candidate is a near-duplicate of a rejected strategy.
    /// </summary>
    public const double DefaultDiversityPenalty = -1.0;

    /// <summary>
    /// Computes process rewards for a candidate strategy given evaluation match results,
    /// compilation status, and optional history of previously rejected strategies.
    /// </summary>
    /// <param name="learningPlayerId">The player being evaluated.</param>
    /// <param name="candidateStrategyId">The candidate strategy identifier.</param>
    /// <param name="matchResults">Match results from evaluation games.</param>
    /// <param name="incumbentBaselineAverageScore">The incumbent's historical average score for comparison.</param>
    /// <param name="isCompilable">Whether the candidate strategy compiles successfully.</param>
    /// <param name="candidateSource">The candidate strategy source text; used for diversity checks.</param>
    /// <param name="previouslyRejectedSources">Source texts of previously rejected strategies to check for duplicates.</param>
    /// <returns><see cref="BonesProcessRewards"/> with individual reward components and aggregate.</returns>
    public BonesProcessRewards ComputeRewards(
        BonesPlayerId learningPlayerId,
        BonesStrategyId candidateStrategyId,
        IReadOnlyList<BonesMatchResult> matchResults,
        double incumbentBaselineAverageScore,
        bool isCompilable,
        string? candidateSource = null,
        IReadOnlyList<string>? previouslyRejectedSources = null)
    {
        var scoreDeltaReward = ComputeScoreDeltaReward(
            learningPlayerId,
            matchResults,
            incumbentBaselineAverageScore);

        var compilationSuccessReward = ComputeCompilationSuccessReward(
            isCompilable);

        var diversityNoveltyReward = ComputeDiversityNoveltyReward(
            candidateSource,
            previouslyRejectedSources);

        var aggregateReward = scoreDeltaReward + compilationSuccessReward + diversityNoveltyReward;

        return new BonesProcessRewards(
            scoreDeltaReward,
            compilationSuccessReward,
            diversityNoveltyReward,
            aggregateReward);
    }

    private static double ComputeScoreDeltaReward(
        BonesPlayerId learningPlayerId,
        IReadOnlyList<BonesMatchResult> matchResults,
        double incumbentBaselineAverageScore)
    {
        if (matchResults.Count == 0)
            return 0;

        double candidateTotalScore = 0;
        var matchesWithPlayer = 0;

        foreach (var matchResult in matchResults)
        {
            if (matchResult.CumulativeScores.TryGetValue(learningPlayerId, out var score))
            {
                candidateTotalScore += score;
                matchesWithPlayer++;
            }
        }

        if (matchesWithPlayer == 0)
            return 0;

        var candidateAverageScore = candidateTotalScore / matchesWithPlayer;
        return candidateAverageScore - incumbentBaselineAverageScore;
    }

    private static double ComputeCompilationSuccessReward(bool isCompilable)
        => isCompilable ? DefaultCompilationSuccessReward : 0;

    private static double ComputeDiversityNoveltyReward(
        string? candidateSource,
        IReadOnlyList<string>? previouslyRejectedSources)
    {
        if (string.IsNullOrWhiteSpace(candidateSource) || previouslyRejectedSources is null || previouslyRejectedSources.Count == 0)
            return 0;

        foreach (var rejectedSource in previouslyRejectedSources)
        {
            if (string.IsNullOrWhiteSpace(rejectedSource))
                continue;

            var similarity = ComputeNormalizedSimilarity(candidateSource, rejectedSource);
            if (similarity >= DuplicateSimilarityThreshold)
                return DefaultDiversityPenalty;
        }

        return 0;
    }

    /// <summary>
    /// Computes a normalized similarity score between two strings using
    /// Levenshtein distance. Returns a value in [0, 1] where 1.0 means
    /// identical and 0.0 means completely different.
    /// </summary>
    internal static double ComputeNormalizedSimilarity(string left, string right)
    {
        var distance = ComputeLevenshteinDistance(left, right);
        var maxLength = Math.Max(left.Length, right.Length);
        if (maxLength == 0)
            return 1.0;

        return 1.0 - (double)distance / maxLength;
    }

    /// <summary>
    /// Computes the Levenshtein (edit) distance between two strings.
    /// Uses the classic dynamic programming approach with O(n*m) time
    /// and O(min(n,m)) space.
    /// </summary>
    internal static int ComputeLevenshteinDistance(string left, string right)
    {
        // Ensure left is the shorter string for space optimization
        if (left.Length > right.Length)
        {
            (left, right) = (right, left);
        }

        var previous = new int[left.Length + 1];
        var current = new int[left.Length + 1];

        for (var i = 0; i <= left.Length; i++)
        {
            previous[i] = i;
        }

        for (var j = 1; j <= right.Length; j++)
        {
            current[0] = j;

            for (var i = 1; i <= left.Length; i++)
            {
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;

                current[i] = Math.Min(
                    previous[i] + 1,                // deletion
                    Math.Min(
                        current[i - 1] + 1,          // insertion
                        previous[i - 1] + substitutionCost)); // substitution
            }

            (previous, current) = (current, previous);
        }

        return previous[left.Length];
    }
}
