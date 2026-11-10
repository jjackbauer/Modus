namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// The bounded inputs of one ablation run. The bounds are enforced by
/// <see cref="HarnessAblationRunner"/> before any simulation starts: one to ten iterations, one to
/// two thousand training hands, one to one hundred evaluation seeds and one to fifty evaluation
/// hands per match.
/// </summary>
/// <remarks>
/// <see cref="WallClockCap"/> may shorten the fixed ten-minute budget (for a fast refusal test) but
/// never lengthen it. The elapsed time itself is never recorded in the canonical report.
/// </remarks>
public sealed record HarnessAblationOptions(
    ulong Seed,
    int Iterations,
    int TrainingHands,
    int EvaluationSeeds,
    int EvaluationHandsPerMatch,
    string DataDirectory)
{
    /// <summary>The wall-clock budget for the whole run, defaulting to the fixed ten-minute cap.</summary>
    public TimeSpan WallClockCap { get; init; } = HarnessAblationRunner.DefaultWallClockCap;
}

/// <summary>
/// The stable refusal of the ablation runner: an out-of-bound option, a missing data directory or an
/// exhausted wall-clock budget. <see cref="Code"/> is a fixed machine-readable value; it never
/// contains a path, a clock or a random value.
/// </summary>
public sealed class HarnessAblationException : InvalidOperationException
{
    /// <summary>An option is outside the documented bound (or the data directory is blank).</summary>
    public const string InvalidOptionCode = "ablation-option-out-of-range";

    /// <summary>The wall-clock budget was exhausted before the next configuration started.</summary>
    public const string WallClockExceededCode = "ablation-wall-clock-exceeded";

    public HarnessAblationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
