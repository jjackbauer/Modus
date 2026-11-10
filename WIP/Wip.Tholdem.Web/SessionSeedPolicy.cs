namespace Wip.Tholdem.Web;

/// <summary>
/// Mirror of <c>Wip.Tholdem.Host.SessionSeedPolicy</c> for the viewer's <c>POST /api/live</c> entry
/// point. The host and web projects do not reference each other, so the bound is duplicated
/// deliberately; both values must stay equal (the CLI refuses the same seeds).
/// </summary>
public static class SessionSeedPolicy
{
    /// <summary>2^40, the smallest session seed the operator entry points accept by default.</summary>
    public const ulong MinimumSeed = 1_099_511_627_776UL;

    /// <summary>True when <paramref name="seed"/> is at or above <see cref="MinimumSeed"/>.</summary>
    public static bool IsStrong(ulong seed) => seed >= MinimumSeed;
}
