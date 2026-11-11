using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Four-seat contract for the equity-aware policy: legality in a real simulator match, determinism
/// on replay, both aggressive and passive action kinds across twelve hands, and the zero-live-
/// opponent guard. The opponents are the unchanged tight-aggressive baseline.
/// </summary>
public sealed class EquityPolicyAgentTests
{
    private const ulong Seed = 20261109UL;

    [Fact]
    public void ActAsync_GivenFourSeatMatch_OnlyLegalActions()
    {
        var (probe, report) = RunMatch();

        Assert.Equal(12, report.Hands);
        Assert.Equal(4, report.Agents.Count);
        Assert.NotEmpty(probe.Turns);
        foreach (var turn in probe.Turns)
        {
            LearningTestSupport.AssertLegalAction(turn.Observation, turn.Action);
        }
    }

    [Fact]
    public async Task ActAsync_GivenSameObservation_IsDeterministic()
    {
        var (probe, _) = RunMatch();
        var first = new EquityPolicyAgent(EquitySamples: 150, name: "equity-policy-a");
        var second = new EquityPolicyAgent(EquitySamples: 150, name: "equity-policy-b");

        foreach (var turn in probe.Turns)
        {
            var once = await first.ActAsync(turn.Observation, turn.Context, CancellationToken.None);
            var twice = await second.ActAsync(turn.Observation, turn.Context, CancellationToken.None);

            Assert.Equal(once, twice);
            Assert.Equal(turn.Action, once);
            LearningTestSupport.AssertLegalAction(turn.Observation, once);
        }
    }

    [Fact]
    public void ActAsync_GivenTwelveHands_ProducesAggressiveAndPassiveActions()
    {
        var (probe, _) = RunMatch();
        var distinctKinds = probe.Turns.Select(static turn => turn.Action.Kind).Distinct().ToArray();

        Assert.NotEmpty(distinctKinds);
        Assert.Contains(distinctKinds, static kind => kind is ActionKind.Bet or ActionKind.Raise);
        Assert.Contains(distinctKinds, static kind => kind is ActionKind.Check or ActionKind.Call or ActionKind.Fold);
    }

    [Fact]
    public async Task ActAsync_GivenNoLiveOpponents_ReturnsFirstLegal()
    {
        var seats = new[]
        {
            new SeatPublicState(0, 1000, 0, true, false),
            new SeatPublicState(1, 1000, 0, true, false),
            new SeatPublicState(2, 1000, 0, true, false),
            new SeatPublicState(3, 1000, 0, true, false),
        };
        var observation = new PlayerObservation
        {
            HandNumber = 1,
            Street = Street.Preflop,
            ButtonSeat = 0,
            ActingSeat = 0,
            SeatCount = 4,
            Seat = 0,
            Board = Array.Empty<Card>(),
            OwnHoleCards = new[] { Card.Parse("As"), Card.Parse("Ah") },
            OwnStack = 1000,
            OpponentStack = 3000,
            Pot = 30,
            OwnStreetCommitment = 0,
            OpponentStreetCommitment = 30,
            AmountToCall = 0,
            MinRaiseTo = 10,
            MaxRaiseTo = 1000,
            LegalActionKinds = new[] { ActionKind.Check, ActionKind.Bet },
            IsComplete = false,
            Winners = Array.Empty<int>(),
            Seats = seats,
        };
        var agent = new EquityPolicyAgent(EquitySamples: 150, name: "no-live-opponents");

        var action = await agent.ActAsync(observation, new AgentTurnContext(1, 0, 0), CancellationToken.None);

        Assert.Equal(ActionKind.Check, action.Kind);
        Assert.Equal(0L, action.Amount);
    }

    private static (RecordingAgent Probe, SimulationReport Report) RunMatch()
    {
        var probe = new RecordingAgent(new EquityPolicyAgent(EquitySamples: 150, name: "equity-policy"));
        IPokerAgent[] agents =
        {
            probe,
            new TightAggressiveAgent(name: "tight-seat-1"),
            new TightAggressiveAgent(name: "tight-seat-2"),
            new TightAggressiveAgent(name: "tight-seat-3"),
        };

        var report = MatchSimulator.Run(agents, new SimulationOptions(Seed, 12));
        return (probe, report);
    }
}