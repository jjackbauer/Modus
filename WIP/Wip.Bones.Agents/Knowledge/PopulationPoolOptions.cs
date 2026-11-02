namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Configuration options for the population-based strategy pool.
/// Supports environment variable overrides via BonesHost__Population__ prefix.
/// </summary>
public sealed record PopulationPoolOptions
{
    public const int DefaultPoolSize = 100;
    public const int DefaultMaxPopulation = 200;
    public const int EvaluationCycleMultiplier = 8;

    public static readonly PopulationPoolOptions Default = new(DefaultPoolSize, DefaultMaxPopulation);

    public PopulationPoolOptions(int poolSize, int maxPopulation)
    {
        if (poolSize < 4)
            throw new ArgumentOutOfRangeException(nameof(poolSize), poolSize, "Pool size must be at least 4.");
        if (maxPopulation < poolSize)
            throw new ArgumentOutOfRangeException(nameof(maxPopulation), maxPopulation,
                "Max population must be at least the pool size.");

        PoolSize = poolSize;
        MaxPopulation = maxPopulation;
        EvaluationCycleGameCount = poolSize * EvaluationCycleMultiplier;
    }

    /// <summary>
    /// Target number of strategies in the active pool. Default: 16.
    /// </summary>
    public int PoolSize { get; }

    /// <summary>
    /// Maximum number of strategies the pool can grow to before pruning. Default: 32.
    /// </summary>
    public int MaxPopulation { get; }

    /// <summary>
    /// Number of games between evaluation cycles. Computed as PoolSize × 8. Default: 800.
    /// </summary>
    public int EvaluationCycleGameCount { get; }

    /// <summary>
    /// Creates options from environment variables using the
    /// BonesHost__Population__ prefix convention.
    /// </summary>
    public static PopulationPoolOptions FromEnvironment()
    {
        var poolSizeStr = Environment.GetEnvironmentVariable("BonesHost__Population__PoolSize");
        var maxPopStr = Environment.GetEnvironmentVariable("BonesHost__Population__MaxPopulation");

        var poolSize = int.TryParse(poolSizeStr, out var ps) && ps >= 4 ? ps : DefaultPoolSize;
        var maxPopulation = int.TryParse(maxPopStr, out var mp) && mp >= poolSize ? mp : DefaultMaxPopulation;

        return new PopulationPoolOptions(poolSize, maxPopulation);
    }
}
