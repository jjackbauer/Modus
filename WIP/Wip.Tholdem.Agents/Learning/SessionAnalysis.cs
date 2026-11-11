using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The measured shape of one training session for the strategy under measurement. Every field other
/// than <see cref="Vpip"/> is read straight from the simulator's own metrics for seat zero.
/// </summary>
public sealed record SessionAnalysis(
    int Hands,
    double WinRate,
    double BbPer100,
    double AggressionFactor,
    double ShowdownRate,
    double? Vpip)
{
    /// <summary>The number of hands in the VPIP denominator; zero means VPIP is missing.</summary>
    public int VpipHands { get; init; }

    /// <summary>The number of those hands in which the seat voluntarily played preflop.</summary>
    public int VpipVoluntaryHands { get; init; }
}

/// <summary>
/// Reads the hidden-information-free aggregate a <see cref="SimulationReport"/> already exposes. The
/// seat-zero <see cref="VpipSummary"/> is copied verbatim, including its denominator; a report with
/// no summary yields <see langword="null"/> VPIP and zero counts rather than a fabricated zero.
/// </summary>
public static class StrategyAnalyzer
{
    public static SessionAnalysis Analyze(SimulationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Agents.Count == 0)
        {
            throw new ArgumentException("A simulation report must carry at least one agent's metrics.", nameof(report));
        }

        var metrics = report.Agents[0];
        var vpip = report.Vpip.Count > 0 ? report.Vpip[0] : null;
        return new SessionAnalysis(
            metrics.Hands,
            metrics.WinRate,
            metrics.BigBlindsPer100,
            metrics.AggressionFactor,
            metrics.ShowdownRate,
            Vpip: vpip?.Vpip)
        {
            VpipHands = vpip?.Hands ?? 0,
            VpipVoluntaryHands = vpip?.VoluntaryPreflopHands ?? 0,
        };
    }
}
