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

        // OBS-005: the analyzer copies the measured seat-zero VPIP and its denominator. The previous
        // pinned 0.0 asserted the missing-data lie for a report that now measures it.
        Assert.Equal(report.Vpip[0].Vpip, analysis.Vpip);
        Assert.Equal(report.Vpip[0].Hands, analysis.VpipHands);
        Assert.Equal(report.Vpip[0].VoluntaryPreflopHands, analysis.VpipVoluntaryHands);
        Assert.Equal(analysis, StrategyAnalyzer.Analyze(report));
        Assert.NotEqual(report.Agents[1].WinRate, analysis.WinRate);
    }
}
