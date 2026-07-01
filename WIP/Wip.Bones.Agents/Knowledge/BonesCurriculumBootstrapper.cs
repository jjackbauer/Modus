using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public enum BonesCurriculumTier
{
    Simplified,
    Intermediate,
    Full,
}

public sealed record BonesCurriculumConfig(int PlayerCount, int TargetScore);

public sealed class BonesCurriculumBootstrapper
{
    private readonly BonesStrategyLibrary _library;

    public BonesCurriculumBootstrapper(BonesStrategyLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public BonesCurriculumTier GetCurrentTier(BonesPlayerId playerId)
    {
        var entry = _library.LoadBestStrategy(playerId);
        if (entry is null)
            return BonesCurriculumTier.Simplified;

        var ranking = _library.GetRanking(playerId);
        if (ranking.Count == 0)
            return BonesCurriculumTier.Simplified;

        var best = ranking[0];
        if (best.Effectiveness.MatchesPlayed == 0)
            return BonesCurriculumTier.Simplified;

        return best.Effectiveness.MatchesPlayed switch
        {
            < 5 => BonesCurriculumTier.Intermediate,
            _ => BonesCurriculumTier.Full,
        };
    }

    public BonesCurriculumTier AdvanceToNextTier(BonesCurriculumTier current)
    {
        return current switch
        {
            BonesCurriculumTier.Simplified => BonesCurriculumTier.Intermediate,
            BonesCurriculumTier.Intermediate => BonesCurriculumTier.Full,
            BonesCurriculumTier.Full => BonesCurriculumTier.Full,
            _ => throw new ArgumentOutOfRangeException(nameof(current), current, "Unknown curriculum tier."),
        };
    }

    public bool HasGraduated(BonesPlayerId playerId)
        => GetCurrentTier(playerId) == BonesCurriculumTier.Full;

    public static BonesCurriculumConfig GetConfig(BonesCurriculumTier tier)
    {
        return tier switch
        {
            BonesCurriculumTier.Simplified => new BonesCurriculumConfig(PlayerCount: 2, TargetScore: 4),
            BonesCurriculumTier.Intermediate => new BonesCurriculumConfig(PlayerCount: 3, TargetScore: 6),
            BonesCurriculumTier.Full => new BonesCurriculumConfig(PlayerCount: 4, TargetScore: 8),
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown curriculum tier."),
        };
    }
}
