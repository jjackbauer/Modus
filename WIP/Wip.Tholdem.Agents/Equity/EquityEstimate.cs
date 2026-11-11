using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// A seeded equity estimate against a fixed number of uniformly random opponents: win and tie
/// probabilities, expected pot share (a tie splits the pot equally), sample counts and a normal
/// confidence interval for the expected pot share.
/// </summary>
public sealed record EquityEstimate(
    double WinProbability,
    double TieProbability,
    double ExpectedPotShare,
    int Samples,
    int OpponentCount,
    string Method,
    int Wins,
    int Ties,
    int Losses,
    double StandardError,
    ConfidenceInterval ExpectedPotShareInterval)
{
    /// <summary>
    /// Identifier of the opponent-range model the estimate was conditioned on. The uniform sampler
    /// keeps the default <c>"uniform"</c>; a range-conditioned estimate records
    /// <c>"range:" + first16(StrategyArtifact.ComputeHash(joined range hashes))</c>.
    /// </summary>
    public string RangeModelId { get; init; } = "uniform";
}
