using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class TightAggressiveAgentTests
{
    [Fact]
    public async Task ActAsync_GivenAcesPreflopWithRaiseAvailable_ExpectedRaiseNotFold()
    {
        var seed = AgentTestSupport.FindSeedForHoleCards(0, "As", "Ah");
        var state = HeadsUpHandEngine.Start(seed);
        var observation = PlayerObservation.ForSeat(state, 0);
        Assert.Contains(ActionKind.Raise, observation.LegalActionKinds);

        var agent = new TightAggressiveAgent(name: "tha-aces");
        var action = await agent.ActAsync(observation, new AgentTurnContext(1, 0, 0), CancellationToken.None);

        Assert.Equal(ActionKind.Raise, action.Kind);
        Assert.InRange(action.Amount, observation.MinRaiseTo, observation.MaxRaiseTo);
        Assert.Contains(action.Kind, observation.LegalActionKinds);
    }

    [Fact]
    public async Task ActAsync_GivenSevenTwoOffsuitFacingLargeBet_ExpectedFold()
    {
        var seed = AgentTestSupport.FindSeedForHoleCards(0, "7h", "2d");
        var state = HeadsUpHandEngine.Start(seed);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        state = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(100));
        var observation = PlayerObservation.ForSeat(state, 0);
        Assert.Equal(90L, observation.AmountToCall);
        Assert.Contains(ActionKind.Fold, observation.LegalActionKinds);

        var agent = new TightAggressiveAgent(name: "tha-seven-two");
        var action = await agent.ActAsync(observation, new AgentTurnContext(1, 0, 1), CancellationToken.None);

        Assert.Equal(ActionKind.Fold, action.Kind);
        Assert.Equal(0L, action.Amount);
    }

    [Fact]
    public void Run_GivenTwoHundredSimulatedHands_ExpectedOnlyLegalActions()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-tight-aggressive");
        try
        {
            var report = MatchSimulator.Run(
                new TightAggressiveAgent(name: "tha-seat-0"),
                new TightAggressiveAgent(name: "tha-seat-1"),
                new SimulationOptions(555UL, 200, HistoryDirectory: directory));

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