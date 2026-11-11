using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Wip.Tholdem.ModelHarness;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// OBS-005/DIA-005: VPIP is measured from preflop voluntary chips, carries its denominator and is
/// missing (null), not zero, when the report never measured it.
/// </summary>
public sealed class VpipObservationTests
{
    private const int Hands = 10;
    private const ulong Seed = 20261110UL;

    [Fact]
    public void RunDetailed_GivenFoldEveryHandSeat0_ReportsMeasuredZeroVpip()
    {
        var simulation = MatchSimulator.RunDetailed(
            [new FoldCheckAgent("folder"), new CallCheckAgent("caller")],
            new SimulationOptions(Seed, Hands));

        Assert.Equal(2, simulation.Report.Vpip.Count);
        var summary = simulation.Report.Vpip[0];
        Assert.Equal(10, summary.Hands);
        Assert.Equal(0, summary.VoluntaryPreflopHands);
        Assert.Equal(0.0, summary.Vpip!.Value);
    }

    [Fact]
    public void RunDetailed_GivenCallingSeat0_ReportsPositiveVpip()
    {
        var simulation = MatchSimulator.RunDetailed(
            [new CallCheckAgent("caller"), new CallCheckAgent("caller-two")],
            new SimulationOptions(Seed, Hands));

        Assert.Equal(2, simulation.Report.Vpip.Count);
        var summary = simulation.Report.Vpip[0];
        Assert.Equal(10, summary.Hands);
        Assert.True(summary.VoluntaryPreflopHands > 0);
        Assert.True(summary.Vpip > 0.0);
        Assert.True(summary.Vpip <= 1.0);
    }

    [Fact]
    public void StrategyAnalyzer_GivenMeasuredReport_CopiesVpipAndDenominator()
    {
        var report = FoldEveryHandReport();
        var summary = report.Vpip[0];

        var analysis = StrategyAnalyzer.Analyze(report);

        Assert.Equal(0.0, analysis.Vpip!.Value);
        Assert.Equal(summary.Vpip, analysis.Vpip);
        Assert.Equal(10, analysis.VpipHands);
        Assert.Equal(0, analysis.VpipVoluntaryHands);
        Assert.Equal(summary.Hands, analysis.VpipHands);
        Assert.Equal(summary.VoluntaryPreflopHands, analysis.VpipVoluntaryHands);
    }

    [Fact]
    public void StrategyAnalyzer_GivenReportWithoutVpip_ReturnsNull()
    {
        var report = new SimulationReport(
            "hand-built-session",
            7UL,
            10,
            "manifest-hash",
            "{}",
            [
                new AgentMetrics(
                    "hand-built",
                    10,
                    5,
                    0.5,
                    new ConfidenceInterval(0.2, 0.8),
                    0,
                    0.0,
                    new ConfidenceInterval(-1.0, 1.0),
                    1.0,
                    0.2),
            ]);

        Assert.Empty(report.Vpip);
        var analysis = StrategyAnalyzer.Analyze(report);

        Assert.Null(analysis.Vpip);
        Assert.Equal(0, analysis.VpipHands);
        Assert.Equal(0, analysis.VpipVoluntaryHands);
    }

    [Fact]
    public void LlmPromptBuilder_GivenNullAndMeasuredZero_WritesNullAndZeroWithDenominators()
    {
        var missing = AnalysisJson(new SessionAnalysis(10, 0.5, 0.0, 1.0, 0.2, null));
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("vpip").ValueKind);
        Assert.Equal(0, missing.GetProperty("vpipHands").GetInt32());
        Assert.Equal(0, missing.GetProperty("vpipVoluntary").GetInt32());

        var measured = AnalysisJson(new SessionAnalysis(10, 0.5, 0.0, 1.0, 0.2, 0.0)
        {
            VpipHands = 10,
            VpipVoluntaryHands = 0,
        });
        Assert.Equal(0.0, measured.GetProperty("vpip").GetDouble());
        Assert.Equal(10, measured.GetProperty("vpipHands").GetInt32());
        Assert.Equal(0, measured.GetProperty("vpipVoluntary").GetInt32());
    }

    private static SimulationReport FoldEveryHandReport() =>
        MatchSimulator.Run(
            new FoldCheckAgent("folder"),
            new CallCheckAgent("caller"),
            new SimulationOptions(Seed, Hands));

    private static JsonElement AnalysisJson(SessionAnalysis analysis)
    {
        var context = new LlmPromptContext(
            1,
            new StrategyGenome(),
            analysis,
            [],
            [],
            []);
        using var document = JsonDocument.Parse(LlmPromptBuilder.BuildUserPrompt(context));
        return document.RootElement.GetProperty("analysis").Clone();
    }

    /// <summary>Folds whenever folding is legal and checks otherwise; never puts in a voluntary chip.</summary>
    private sealed class FoldCheckAgent : IPokerAgent
    {
        public FoldCheckAgent(string name) => Name = name;

        public string Name { get; }

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                observation.LegalActionKinds.Contains(ActionKind.Fold)
                    ? PokerAction.Fold()
                    : PokerAction.Check());
        }
    }

    /// <summary>Calls every bet and checks otherwise; never raises.</summary>
    private sealed class CallCheckAgent : IPokerAgent
    {
        public CallCheckAgent(string name) => Name = name;

        public string Name { get; }

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.LegalActionKinds.Contains(ActionKind.Call))
            {
                return ValueTask.FromResult(PokerAction.Call());
            }

            return ValueTask.FromResult(PokerAction.Check());
        }
    }
}