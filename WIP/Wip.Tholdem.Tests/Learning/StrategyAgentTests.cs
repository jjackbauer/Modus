using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class StrategyAgentTests
{
    private const ulong Seed = 20261032UL;

    [Fact]
    public void ActAsync_GivenFortySimulatedHands_ExpectedOnlyLegalActions()
    {
        var genome = new StrategyGenome(
            RaiseThreshold: 0.55,
            CallThreshold: 0.28,
            BluffFrequency: 0.4,
            SizingMultiplier: 1.75);
        var probe = new RecordingAgent(new StrategyAgent(genome, "strategy-probe"));

        var report = MatchSimulator.Run(
            probe,
            new TightAggressiveAgent(name: "baseline"),
            new SimulationOptions(Seed, 40));

        Assert.Equal(40, report.Hands);
        Assert.Equal(40, report.Agents[0].Hands);
        Assert.True(probe.Turns.Count >= 80, $"Expected at least two decisions per hand, saw {probe.Turns.Count}.");

        // The simulator throws on an illegal action, so reaching this line already proves legality;
        // the explicit replay below also proves it for every recorded observation.
        foreach (var turn in probe.Turns)
        {
            LearningTestSupport.AssertLegalAction(turn.Observation, turn.Action);
        }

        var distinctKinds = probe.Turns.Select(static turn => turn.Action.Kind).Distinct().ToArray();
        Assert.Contains(ActionKind.Fold, distinctKinds);
        Assert.Contains(ActionKind.Check, distinctKinds);
        Assert.Contains(ActionKind.Call, distinctKinds);
        Assert.Contains(distinctKinds, static kind => kind is ActionKind.Bet or ActionKind.Raise);
    }

    [Fact]
    public async Task ActAsync_GivenSameObservationAndContext_ExpectedIdenticalActionsTwice()
    {
        var genome = new StrategyGenome(
            RaiseThreshold: 0.5,
            CallThreshold: 0.25,
            BluffFrequency: 0.5,
            SizingMultiplier: 2.5);
        var probe = new RecordingAgent(new StrategyAgent(genome, "strategy-probe"));
        var report = MatchSimulator.Run(
            probe,
            new TightAggressiveAgent(name: "baseline"),
            new SimulationOptions(Seed, 40));

        Assert.Equal(40, report.Hands);
        var first = new StrategyAgent(genome, "strategy-a");
        var second = new StrategyAgent(genome, "strategy-b");

        foreach (var turn in probe.Turns)
        {
            var once = await first.ActAsync(turn.Observation, turn.Context, CancellationToken.None);
            var twice = await second.ActAsync(turn.Observation, turn.Context, CancellationToken.None);

            Assert.Equal(once, twice);
            Assert.Equal(turn.Action, once);
            LearningTestSupport.AssertLegalAction(turn.Observation, once);
        }
    }
}
