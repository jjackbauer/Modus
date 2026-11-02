using Wip.Bones.Domain;

namespace Wip.Bones.Agents.Knowledge;

public static class BonesStrategyLibraryRanking
{
    /// <summary>
    /// Bayesian prior: (Wins + 10) / (Matches + 20) gives a 50% prior
    /// weighted equivalent to 20 matches.
    /// </summary>
    private const double PriorWins = 10;
    private const double PriorMatches = 20;

    /// <summary>
    /// Minimum matches played before a strategy is eligible for library upsert.
    /// </summary>
    public static bool IsEligible(BonesStrategyEffectivenessRecord effectiveness)
        => effectiveness.MatchesPlayed >= 1;

    public static bool IsEligible(BonesStrategyLibraryEntry entry)
        => IsEligible(entry.Effectiveness);

    private static double BayesianWR(BonesStrategyEffectivenessRecord e)
        => (e.Wins + PriorWins) / (e.MatchesPlayed + PriorMatches);

    /// <summary>
    /// Composite ranking score: Bayesian win rate × √matches.
    /// This makes match count a dominant factor — 20-match entries
    /// massively outrank 1-match entries regardless of raw win rate.
    ///
    ///   W:1/1   → (11/21) × √1  = 0.52
    ///   W:12/20 → (22/40) × √20 = 2.46  ← dominates
    ///   W:11/20 → (21/40) × √20 = 2.35
    /// </summary>
    private static double CompositeScore(BonesStrategyEffectivenessRecord e)
        => BayesianWR(e) * Math.Sqrt(e.MatchesPlayed);

    /// <summary>
    /// Compares two entries for best-strategy selection.
    /// Returns negative when <paramref name="left"/> ranks higher than <paramref name="right"/>.
    /// Primary sort: composite score (Bayesian WR × √matches).
    /// </summary>
    public static int Compare(BonesStrategyLibraryEntry left, BonesStrategyLibraryEntry right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftEligible = IsEligible(left);
        var rightEligible = IsEligible(right);

        if (!leftEligible && !rightEligible)
            return 0;

        if (!leftEligible)
            return 1;

        if (!rightEligible)
            return -1;

        // Primary: composite score = Bayesian WR × √matches
        var leftScore = CompositeScore(left.Effectiveness);
        var rightScore = CompositeScore(right.Effectiveness);
        var scoreCompare = rightScore.CompareTo(leftScore);
        if (scoreCompare != 0)
            return scoreCompare;

        // Secondary: raw win rate (when composite scores are tied)
        var leftWinRate = (double)left.Effectiveness.Wins / left.Effectiveness.MatchesPlayed;
        var rightWinRate = (double)right.Effectiveness.Wins / right.Effectiveness.MatchesPlayed;
        var winRateCompare = rightWinRate.CompareTo(leftWinRate);
        if (winRateCompare != 0)
            return winRateCompare;

        // Tertiary: prefer more matches played (more data = more reliable)
        var matchesCompare = right.Effectiveness.MatchesPlayed
            .CompareTo(left.Effectiveness.MatchesPlayed);
        if (matchesCompare != 0)
            return matchesCompare;

        // Score differential
        var scoreDifferentialCompare = right.Effectiveness.CumulativeScoreDifferential
            .CompareTo(left.Effectiveness.CumulativeScoreDifferential);
        if (scoreDifferentialCompare != 0)
            return scoreDifferentialCompare;

        // Tiebreaker: Script ranks higher than Markdown
        if (left.Kind != right.Kind)
        {
            const int scriptHigher = -1;
            const int markdownHigher = 1;
            return left.Kind == BonesStrategyKind.Script ? scriptHigher : markdownHigher;
        }

        return right.LastUpdatedUtc.CompareTo(left.LastUpdatedUtc);
    }

    public static bool RanksHigher(BonesStrategyLibraryEntry candidate, BonesStrategyLibraryEntry? incumbent)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!IsEligible(candidate))
            return false;

        if (incumbent is null)
            return true;

        // Data-significance gate: a candidate with very few matches cannot
        // displace a well-established incumbent. This prevents a 1-match
        // play-tool upsert from overwriting a 20-match evaluation entry.
        var candM = candidate.Effectiveness.MatchesPlayed;
        var incM = incumbent.Effectiveness.MatchesPlayed;
        if (candM <= 2 && incM >= candM * 5)
            return false;

        return Compare(candidate, incumbent) < 0;
    }

    public static IReadOnlyList<BonesStrategyLibraryEntry> SortDescending(
        IEnumerable<BonesStrategyLibraryEntry> entries)
        => entries
            .Where(IsEligible)
            .OrderBy(entry => entry, Comparer<BonesStrategyLibraryEntry>.Create(Compare))
            .ToArray();
}
