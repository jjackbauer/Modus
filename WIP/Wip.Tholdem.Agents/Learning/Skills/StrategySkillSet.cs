using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Agents.Learning.Skills;

/// <summary>
/// The bounded preflop decision thresholds. The values are data only: the agent reads them and never
/// executes them.
/// </summary>
public sealed record PreflopSkillParameters(
    double RaiseThreshold = StrategyGenome.DefaultRaiseThreshold,
    double CallThreshold = StrategyGenome.DefaultCallThreshold)
{
    public const double MinimumThreshold = 0.0;

    public const double MaximumThreshold = 1.0;

    /// <summary>Returns the parameters with both thresholds inside [0, 1].</summary>
    public PreflopSkillParameters Clamp() => new(
        Math.Clamp(RaiseThreshold, MinimumThreshold, MaximumThreshold),
        Math.Clamp(CallThreshold, MinimumThreshold, MaximumThreshold));

    /// <summary>True when both thresholds are finite and already inside their domain.</summary>
    public bool IsWithinDomain() =>
        IsThreshold(RaiseThreshold) && IsThreshold(CallThreshold);

    private static bool IsThreshold(double value) =>
        double.IsFinite(value) && value >= MinimumThreshold && value <= MaximumThreshold;
}

/// <summary>
/// The bounded postflop decision thresholds. The values are data only: the agent reads them and
/// never executes them.
/// </summary>
public sealed record PostflopSkillParameters(
    double RaiseThreshold = StrategyGenome.DefaultRaiseThreshold,
    double CallThreshold = StrategyGenome.DefaultCallThreshold)
{
    public const double MinimumThreshold = 0.0;

    public const double MaximumThreshold = 1.0;

    /// <summary>Returns the parameters with both thresholds inside [0, 1].</summary>
    public PostflopSkillParameters Clamp() => new(
        Math.Clamp(RaiseThreshold, MinimumThreshold, MaximumThreshold),
        Math.Clamp(CallThreshold, MinimumThreshold, MaximumThreshold));

    /// <summary>True when both thresholds are finite and already inside their domain.</summary>
    public bool IsWithinDomain() =>
        IsThreshold(RaiseThreshold) && IsThreshold(CallThreshold);

    private static bool IsThreshold(double value) =>
        double.IsFinite(value) && value >= MinimumThreshold && value <= MaximumThreshold;
}

/// <summary>
/// The bounded bet-size multiplier used to turn the minimum legal raise into a target amount. The
/// value is data only.
/// </summary>
public sealed record SizingSkillParameters(
    double Multiplier = StrategyGenome.DefaultSizingMultiplier)
{
    public const double MinimumMultiplier = StrategyGenome.MinimumSizingMultiplier;

    public const double MaximumMultiplier = StrategyGenome.MaximumSizingMultiplier;

    /// <summary>Returns the multiplier inside [0.25, 3.0].</summary>
    public SizingSkillParameters Clamp() => new(
        Math.Clamp(Multiplier, MinimumMultiplier, MaximumMultiplier));

    /// <summary>True when the multiplier is finite and already inside its domain.</summary>
    public bool IsWithinDomain() =>
        double.IsFinite(Multiplier) && Multiplier >= MinimumMultiplier && Multiplier <= MaximumMultiplier;
}

/// <summary>
/// The bounded bluff frequency applied only when checking or betting is free (no amount to call).
/// The value is data only.
/// </summary>
public sealed record BluffSkillParameters(
    double Frequency = StrategyGenome.DefaultBluffFrequency)
{
    public const double MinimumFrequency = 0.0;

    public const double MaximumFrequency = 1.0;

    /// <summary>Returns the frequency inside [0, 1].</summary>
    public BluffSkillParameters Clamp() => new(
        Math.Clamp(Frequency, MinimumFrequency, MaximumFrequency));

    /// <summary>True when the frequency is finite and already inside its domain.</summary>
    public bool IsWithinDomain() =>
        double.IsFinite(Frequency) && Frequency >= MinimumFrequency && Frequency <= MaximumFrequency;
}

/// <summary>
/// The four named deterministic skills the optional strategy surface is built from. Every field is a
/// bounded number; there is no code, text or ambient input in the set.
/// </summary>
public sealed record StrategySkillSet(
    PreflopSkillParameters Preflop,
    PostflopSkillParameters Postflop,
    SizingSkillParameters Sizing,
    BluffSkillParameters Bluff)
{
    /// <summary>The documented default set, equal to the default genome's numeric behaviour.</summary>
    public static StrategySkillSet Default { get; } = new();

    public StrategySkillSet()
        : this(
            new PreflopSkillParameters(),
            new PostflopSkillParameters(),
            new SizingSkillParameters(),
            new BluffSkillParameters())
    {
    }

    /// <summary>
    /// Derives the skill set that drives the agent exactly like the genome's four numeric fields. A
    /// genome without skills and a genome with this derived set make the same decisions; that is the
    /// default-equivalence contract of R-09.
    /// </summary>
    public static StrategySkillSet FromGenome(StrategyGenome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);

        return new StrategySkillSet(
            new PreflopSkillParameters(genome.RaiseThreshold, genome.CallThreshold),
            new PostflopSkillParameters(genome.RaiseThreshold, genome.CallThreshold),
            new SizingSkillParameters(genome.SizingMultiplier),
            new BluffSkillParameters(genome.BluffFrequency)).Clamp();
    }

    /// <summary>Returns the set with every skill inside its documented domain.</summary>
    public StrategySkillSet Clamp() => new(
        Preflop.Clamp(),
        Postflop.Clamp(),
        Sizing.Clamp(),
        Bluff.Clamp());

    /// <summary>True when every skill is finite and already inside its documented domain.</summary>
    public bool IsWithinDomain() =>
        Preflop.IsWithinDomain()
        && Postflop.IsWithinDomain()
        && Sizing.IsWithinDomain()
        && Bluff.IsWithinDomain();

    /// <summary>True when both sets drive the agent identically.</summary>
    public bool BehaviorEquals(StrategySkillSet? other) =>
        other is not null
        && Preflop == other.Preflop
        && Postflop == other.Postflop
        && Sizing == other.Sizing
        && Bluff == other.Bluff;

    /// <summary>The number of skill fields that differ between the two sets.</summary>
    public int BehavioralChangeCount(StrategySkillSet other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var changed = 0;
        if (Preflop.RaiseThreshold != other.Preflop.RaiseThreshold)
        {
            changed++;
        }

        if (Preflop.CallThreshold != other.Preflop.CallThreshold)
        {
            changed++;
        }

        if (Postflop.RaiseThreshold != other.Postflop.RaiseThreshold)
        {
            changed++;
        }

        if (Postflop.CallThreshold != other.Postflop.CallThreshold)
        {
            changed++;
        }

        if (Sizing.Multiplier != other.Sizing.Multiplier)
        {
            changed++;
        }

        if (Bluff.Frequency != other.Bluff.Frequency)
        {
            changed++;
        }

        return changed;
    }
}