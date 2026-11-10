using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class PotOddsEquityAgentTests
{
    [Fact]
    public async Task ActAsync_GivenAcesFacingSmallCall_ExpectedCall()
    {
        var seed = AgentTestSupport.FindSeedForHoleCards(0, "As", "Ah");
        var state = HeadsUpHandEngine.Start(seed);
        var observation = PlayerObservation.ForSeat(state, 0);
        Assert.Equal(5L, observation.AmountToCall);

        var agent = new PotOddsEquityAgent(name: "pot-odds-aces");
        var action = await agent.ActAsync(observation, new AgentTurnContext(1, 0, 0), CancellationToken.None);

        Assert.Equal(ActionKind.Call, action.Kind);
        Assert.Equal(0L, action.Amount);
    }

    [Fact]
    public async Task ActAsync_GivenSevenTwoOffsuitFacingOverbet_ExpectedFold()
    {
        var seed = AgentTestSupport.FindSeedForHoleCards(0, "7h", "2d");
        var state = HeadsUpHandEngine.Start(seed);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(200));
        var observation = PlayerObservation.ForSeat(state, 0);
        Assert.Equal(190L, observation.AmountToCall);
        Assert.Equal(210L, observation.Pot);

        var agent = new PotOddsEquityAgent(name: "pot-odds-seven-two");
        var action = await agent.ActAsync(observation, new AgentTurnContext(1, 0, 1), CancellationToken.None);

        Assert.Equal(ActionKind.Fold, action.Kind);
        Assert.Equal(0L, action.Amount);
    }

    [Fact]
    public void Run_GivenTwoHundredSimulatedHands_ExpectedOnlyLegalActions()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-pot-odds");
        try
        {
            var report = MatchSimulator.Run(
                new PotOddsEquityAgent(name: "pot-odds-seat-0"),
                new RandomLegalAgent("pot-odds-opponent"),
                new SimulationOptions(556UL, 200, HistoryDirectory: directory));

            Assert.Equal(200, report.Agents[0].Hands);
            Assert.Equal(200, report.Agents[1].Hands);
            var records = AgentTestSupport.ReadAllRecords(directory);
            Assert.Equal(200, records.Length);
            foreach (var record in records)
            {
                var replayed = HeadsUpHandEngine.Replay(record);
                Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
                Assert.Equal(2000L, replayed.Stacks.Sum());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}