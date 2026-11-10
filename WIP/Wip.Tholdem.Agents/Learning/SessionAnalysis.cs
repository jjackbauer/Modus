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
    double Vpip);

/// <summary>
/// Reads the hidden-information-free aggregate a <see cref="SimulationReport"/> already exposes. The
/// report never carries action-level counts, so <see cref="SessionAnalysis.Vpip"/> is reported as
/// zero (not measured) rather than fabricated from unrelated metrics; the proposer never uses it.
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
        return new SessionAnalysis(
            metrics.Hands,
            metrics.WinRate,
            metrics.BigBlindsPer100,
            metrics.AggressionFactor,
            metrics.ShowdownRate,
            Vpip: 0.0);
    }
}
