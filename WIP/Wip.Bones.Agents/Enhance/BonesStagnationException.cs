namespace Wip.Bones.Agents.Enhance;

/// <summary>
/// Thrown when the model returns a strategy that is byte-identical to the input
/// after exhausting the DistinctOutput retry budget (temperature ramp).
/// </summary>
public sealed class BonesStagnationException : InvalidOperationException
{
    public BonesStagnationException(string message)
        : base(message)
    {
    }

    public BonesStagnationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Number of identical-output detections before giving up.
    /// </summary>
    public int StagnationCount { get; init; }

    /// <summary>
    /// Temperature value at the final attempt.
    /// </summary>
    public double FinalTemperature { get; init; }

    /// <summary>
    /// Strategy ID of the prior (input) strategy that triggered stagnation.
    /// </summary>
    public string? PriorStrategyId { get; init; }
}
