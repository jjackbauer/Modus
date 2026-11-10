using System.Globalization;

namespace Wip.Tholdem.Host;

/// <summary>
/// The seed policy of the operator entry points. A session seed below <see cref="MinimumSeed"/> has
/// a seed space small enough to enumerate from one observation of the deal, so <c>play</c>,
/// <c>train</c> and <c>evaluate</c> refuse it unless the operator explicitly opts in for a
/// reproducible demo or test. A weak seed is never silently accepted.
/// </summary>
public static class SessionSeedPolicy
{
    /// <summary>
    /// 2^40. Below this bound the seed space is practical to search from a single observation, so a
    /// deterministic deal is no longer secret.
    /// </summary>
    public const ulong MinimumSeed = 1_099_511_627_776UL;

    /// <summary>True when <paramref name="seed"/> is at or above <see cref="MinimumSeed"/>.</summary>
    public static bool IsStrong(ulong seed) => seed >= MinimumSeed;

    /// <summary>
    /// Throws a usage error for a weak seed unless the caller passed <c>--allow-weak-seed</c>.
    /// </summary>
    internal static void RequireStrongOrExplicitOptIn(ulong seed, bool allowWeakSeed)
    {
        if (allowWeakSeed || IsStrong(seed))
        {
            return;
        }

        throw new CliUsageException(
            "seed "
            + seed.ToString(CultureInfo.InvariantCulture)
            + " is below the minimum "
            + MinimumSeed.ToString(CultureInfo.InvariantCulture)
            + "; use --allow-weak-seed for reproducible demos");
    }
}
