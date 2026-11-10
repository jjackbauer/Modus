using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class StrategyAnalyzerTests
{
    [Fact]
    public void Analyze_GivenSimulationReport_ExpectedSeatZeroMetricsAndDocumentedVpip()
    {
        var agent = new StrategyAgent(new StrategyGenome(0.62, 0.34, 0.18, 1.2), "strategy-analyzed");
        var report = MatchSimulator.Run(
            agent,
            new TightAggressiveAgent(name: "baseline"),
            new SimulationOptions(515151UL, 30));

        var analysis = StrategyAnalyzer.Analyze(report);
        var metrics = report.Agents[0];

        Assert.Equal(30, metrics.Hands);
        Assert.Equal(metrics.Hands, analysis.Hands);
        Assert.Equal(metrics.WinRate, analysis.WinRate);
        Assert.Equal(metrics.BigBlindsPer100, analysis.BbPer100);
        Assert.Equal(metrics.AggressionFactor, analysis.AggressionFactor);
        Assert.Equal(metrics.ShowdownRate, analysis.ShowdownRate);

        // The aggregated report carries no voluntary-participation counts, so the analyzer reports
        // zero rather than fabricating a hand-level statistic.
        Assert.Equal(0.0, analysis.Vpip);
        Assert.Equal(analysis, StrategyAnalyzer.Analyze(report));
        Assert.NotEqual(report.Agents[1].WinRate, analysis.WinRate);
    }
}
