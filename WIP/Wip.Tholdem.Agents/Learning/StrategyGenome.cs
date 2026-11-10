using Wip.Tholdem.Agents.Learning.Skills;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The complete strategy as data: two decision thresholds, a bluff frequency, a bet-size
/// multiplier, the natural-language prompt and an optional set of named deterministic skills.
/// Nothing here is executable content; the genome is interpreted by <see cref="StrategyAgent"/> and
/// persisted verbatim.
/// </summary>
public sealed record StrategyGenome(
    double RaiseThreshold = 0.72,
    double CallThreshold = 0.45,
    double BluffFrequency = 0.12,
    double SizingMultiplier = 1.0,
    string PromptText = StrategyGenome.DefaultPromptText,
    StrategySkillSet? Skills = null)
{
    public const string DefaultPromptText =
        "Play tight-aggressive heads-up: raise strong hands, fold weak hands to large bets, and size value bets by the sizing multiplier.";

    public const double DefaultRaiseThreshold = 0.72;

    public const double DefaultCallThreshold = 0.45;

    public const double DefaultBluffFrequency = 0.12;

    public const double DefaultSizingMultiplier = 1.0;

    public const double MinimumSizingMultiplier = 0.25;

    public const double MaximumSizingMultiplier = 3.0;

    /// <summary>Returns the genome with every parameter inside its documented domain.</summary>
    public StrategyGenome Clamp() => new(
        Math.Clamp(RaiseThreshold, 0.0, 1.0),
        Math.Clamp(CallThreshold, 0.0, 1.0),
        Math.Clamp(BluffFrequency, 0.0, 1.0),
        Math.Clamp(SizingMultiplier, MinimumSizingMultiplier, MaximumSizingMultiplier),
        PromptText,
        Skills?.Clamp());

    /// <summary>
    /// True when the two genomes drive <see cref="StrategyAgent"/> identically. The prompt text is
    /// data that the agent never reads, so a prompt-only change is behaviourally inert and must not
    /// be sent to the evaluator (A7). The effective skill values are compared in addition to the
    /// four numeric fields; an absent skill set is equal only to another absent set.
    /// </summary>
    public bool BehaviorEquals(StrategyGenome? other) =>
        other is not null
        && RaiseThreshold == other.RaiseThreshold
        && CallThreshold == other.CallThreshold
        && BluffFrequency == other.BluffFrequency
        && SizingMultiplier == other.SizingMultiplier
        && SkillsEqual(Skills, other.Skills);

    private static bool SkillsEqual(StrategySkillSet? left, StrategySkillSet? right) =>
        (left is null && right is null)
        || (left is not null && right is not null && left.BehaviorEquals(right));
}
