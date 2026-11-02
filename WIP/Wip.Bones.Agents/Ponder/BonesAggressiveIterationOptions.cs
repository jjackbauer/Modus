namespace Wip.Bones.Agents.Ponder;

public sealed class BonesAggressiveIterationOptions
{
    public double GameCountMultiplier { get; set; } = 1.0;

    public double TargetScoreMultiplier { get; set; } = 1.0;

    public double? ExplorationTemperature { get; set; }

    public bool EnableCrossSeatTesting { get; set; }

    public bool EnableSeatRotation { get; set; }

    public int ConcurrentGamesPerSession { get; set; } = 1;

    public int EnhanceIterationsPerMatch { get; set; } = 1;

    public int MinimumDistinctStrategiesPerSeat { get; set; } = 1;

    /// <summary>
    /// When enabled, the enhance agent detects byte-identical output
    /// vs input and retries with progressively higher temperature.
    /// Default false for backward compatibility; enable in aggressive mode.
    /// </summary>
    public bool RequireDistinctOutput { get; set; } = false;

    /// <summary>
    /// Maximum number of temperature-ramp retries when the model returns
    /// output identical to the input strategy source. Default 3.
    /// </summary>
    public int DistinctOutputMaxRetries { get; set; } = 3;

    /// <summary>
    /// When enabled, strategies are periodically redeployed to different seats
    /// during play matches so each strategy is exercised against varied opponents.
    /// </summary>
    public bool CrossSeatRotation { get; set; }

    /// <summary>
    /// Maximum length of opponent source excerpt included in enhancement prompts.
    /// Default 4000 characters; was previously a hardcoded 500.
    /// </summary>
    public int OpponentSourceExcerptMaxLength { get; set; } = 4000;

    /// <summary>
    /// When enabled, the global MaxGamesPerRun budget is divided among parallel sessions.
    /// Each session gets MaxGamesPerRun / ParallelSessionCount games.
    /// When disabled (default), all sessions share a single global budget.
    /// </summary>
    public bool PerSessionBudget { get; set; }
}
