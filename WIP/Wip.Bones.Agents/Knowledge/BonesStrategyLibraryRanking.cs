using Wip.Bones.Domain;

namespace Wip.Bones.Agents.Knowledge;

public static class BonesStrategyLibraryRanking
{
    public static bool IsEligible(BonesStrategyEffectivenessRecord effectiveness)
        => effectiveness.MatchesPlayed >= 1;

    public static bool IsEligible(BonesStrategyLibraryEntry entry)
        => IsEligible(entry.Effectiveness);

    /// <summary>
    /// Compares two entries for best-strategy selection.
    /// Returns negative when <paramref name="left"/> ranks higher than <paramref name="right"/>.
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

        var leftWinRate = (double)left.Effectiveness.Wins / left.Effectiveness.MatchesPlayed;
        var rightWinRate = (double)right.Effectiveness.Wins / right.Effectiveness.MatchesPlayed;

        var winRateCompare = rightWinRate.CompareTo(leftWinRate);
        if (winRateCompare != 0)
            return winRateCompare;

        var scoreDifferentialCompare = right.Effectiveness.CumulativeScoreDifferential
            .CompareTo(left.Effectiveness.CumulativeScoreDifferential);
        if (scoreDifferentialCompare != 0)
            return scoreDifferentialCompare;

        // Tiebreaker: Script ranks higher than Markdown (Script = 1, Markdown = 2).
        // Lower enum value means higher priority, so compare Markdown vs Script.
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

        return Compare(candidate, incumbent) < 0;
    }

    public static IReadOnlyList<BonesStrategyLibraryEntry> SortDescending(
        IEnumerable<BonesStrategyLibraryEntry> entries)
        => entries
            .Where(IsEligible)
            .OrderBy(entry => entry, Comparer<BonesStrategyLibraryEntry>.Create(Compare))
            .ToArray();
}
