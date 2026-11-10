using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class SimulatorFailureTests
{
    [Fact]
    public void Run_GivenIllegalAction_ExpectedExceptionNamingAgentAndHandWithoutFallback()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-illegal-action");
        try
        {
            var exception = Assert.Throws<IllegalAgentActionException>(() => MatchSimulator.Run(
                new IllegalActionAgent(),
                new RandomLegalAgent("legal-opponent"),
                new SimulationOptions(31UL, 5, HistoryDirectory: directory)));

            Assert.Equal("illegal-action-agent", exception.AgentName);
            Assert.Equal(1, exception.HandNumber);
            Assert.Equal(ActionKind.Raise, exception.Action.Kind);
            Assert.Equal(1L, exception.Action.Amount);
            Assert.Contains("illegal-action-agent", exception.Message);
            Assert.Contains("hand 1", exception.Message);
            Assert.IsType<IllegalActionException>(exception.InnerException);
            Assert.Empty(Directory.GetFiles(directory));
            Assert.False(File.Exists(Path.Combine(directory, "session.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenThrowingAgent_ExpectedExceptionNamingAgentAndHandWithoutFallback()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-throwing-agent");
        try
        {
            var exception = Assert.Throws<IllegalAgentActionException>(() => MatchSimulator.Run(
                new ThrowingAgent(),
                new RandomLegalAgent("legal-opponent"),
                new SimulationOptions(32UL, 5, HistoryDirectory: directory)));

            Assert.Equal("throwing-agent", exception.AgentName);
            Assert.Equal(1, exception.HandNumber);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("agent exploded", exception.InnerException!.Message);
            Assert.Contains("throwing-agent", exception.Message);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class IllegalActionAgent : IPokerAgent
    {
        public string Name => "illegal-action-agent";

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PokerAction.RaiseTo(1));
    }

    private sealed class ThrowingAgent : IPokerAgent
    {
        public string Name => "throwing-agent";

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("agent exploded");
    }
}