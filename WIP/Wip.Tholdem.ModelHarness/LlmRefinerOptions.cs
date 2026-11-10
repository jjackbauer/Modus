namespace Wip.Tholdem.ModelHarness;

/// <summary>Bounded configuration for one live refinement policy.</summary>
public sealed record LlmRefinerOptions
{
    public LlmRefinerOptions(
        string ModelId,
        int MaxTokens,
        double Temperature,
        TimeSpan Timeout,
        int MaxRepairRetries,
        int MaxCallsPerRun,
        TimeSpan? MaxWallClock = null)
    {
        if (string.IsNullOrWhiteSpace(ModelId))
        {
            throw new ArgumentException("A model id is required.", nameof(ModelId));
        }

        if (MaxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTokens), MaxTokens, "MaxTokens must be positive.");
        }

        if (!double.IsFinite(Temperature) || Temperature < 0.0 || Temperature > 2.0)
        {
            throw new ArgumentOutOfRangeException(nameof(Temperature), Temperature, "Temperature must be in [0, 2].");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "Timeout must be positive.");
        }

        if (MaxRepairRetries is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRepairRetries), MaxRepairRetries, "MaxRepairRetries must be 0 or 1.");
        }

        if (MaxCallsPerRun <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCallsPerRun), MaxCallsPerRun, "MaxCallsPerRun must be positive.");
        }

        if (MaxWallClock is not null && MaxWallClock.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxWallClock), MaxWallClock, "MaxWallClock must be positive when supplied.");
        }

        this.ModelId = ModelId;
        this.MaxTokens = MaxTokens;
        this.Temperature = Temperature;
        this.Timeout = Timeout;
        this.MaxRepairRetries = MaxRepairRetries;
        this.MaxCallsPerRun = MaxCallsPerRun;
        this.MaxWallClock = MaxWallClock;
    }

    public string ModelId { get; init; }

    public int MaxTokens { get; init; }

    public double Temperature { get; init; }

    public TimeSpan Timeout { get; init; }

    public int MaxRepairRetries { get; init; }

    public int MaxCallsPerRun { get; init; }

    public TimeSpan? MaxWallClock { get; init; }
}
