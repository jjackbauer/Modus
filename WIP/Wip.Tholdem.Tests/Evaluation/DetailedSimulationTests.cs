using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-one detailed simulation contract: <see cref="MatchSimulator.RunDetailed"/> exposes the
/// report, the per-hand outcomes and the per-hand seeds while the legacy two-agent surface stays
/// byte-identical, and the optional per-hand action cap stops a runaway hand before the next agent
/// turn.
/// </summary>
public sealed class DetailedSimulationTests
{
    [Fact]
    public void RunDetailed_GivenTwoAgents_ReportMatchesLegacyRun()
    {
        var options = new SimulationOptions(12345UL, 5);
        var agents = new IPokerAgent[]
        {
            new TightAggressiveAgent(name: "legacy-seat-0"),
            new TightAggressiveAgent(name: "legacy-seat-1"),
        };

        var legacy = MatchSimulator.Run(agents[0], agents[1], options);
        var detailed = MatchSimulator.RunDetailed(agents, options);

        Assert.Equal(legacy.CanonicalJson, detailed.Report.CanonicalJson);
        Assert.Equal(legacy.SessionId, detailed.Report.SessionId);
        Assert.Equal(legacy.ManifestHash, detailed.Report.ManifestHash);
    }

    [Fact]
    public void RunDetailed_GivenFourAgents_ReturnsPerHandOutcomes()
    {
        var agents = new IPokerAgent[]
        {
            new TightAggressiveAgent(name: "four-seat-0"),
            new TightAggressiveAgent(name: "four-seat-1"),
            new TightAggressiveAgent(name: "four-seat-2"),
            new TightAggressiveAgent(name: "four-seat-3"),
        };

        var simulation = MatchSimulator.RunDetailed(agents, new SimulationOptions(12345UL, 5));

        Assert.Equal(5, simulation.Outcomes.Count);
        Assert.Equal(5, simulation.HandSeeds.Count);
        for (var index = 0; index < simulation.Outcomes.Count; index++)
        {
            var outcome = simulation.Outcomes[index];
            Assert.Equal(index + 1, outcome.HandNumber);
            Assert.Equal(4, outcome.FinalStacks.Count);
            Assert.Equal(4000L, outcome.FinalStacks.Sum());
        }
    }

    [Fact]
    public void RunDetailed_GivenSameSeed_IsByteIdentical()
    {
        var agents = new IPokerAgent[]
        {
            new TightAggressiveAgent(name: "determinism-seat-0"),
            new TightAggressiveAgent(name: "determinism-seat-1"),
            new TightAggressiveAgent(name: "determinism-seat-2"),
            new TightAggressiveAgent(name: "determinism-seat-3"),
        };

        var first = MatchSimulator.RunDetailed(agents, new SimulationOptions(999UL, 8));
        var second = MatchSimulator.RunDetailed(agents, new SimulationOptions(999UL, 8));

        Assert.Equal(first.Report.CanonicalJson, second.Report.CanonicalJson);
        Assert.Equal(first.HandSeeds, second.HandSeeds);
    }

    [Fact]
    public void Run_GivenActionCapExceeded_Throws()
    {
        var agents = new IPokerAgent[]
        {
            new TightAggressiveAgent(name: "cap-seat-0"),
            new TightAggressiveAgent(name: "cap-seat-1"),
        };

        var exception = Assert.Throws<HandActionCapExceededException>(
            () => MatchSimulator.Run(
                agents,
                new SimulationOptions(2UL, 1, actionCapPerHand: 2)));

        Assert.Equal(1, exception.HandNumber);
        Assert.Equal(2, exception.Cap);
    }
}
