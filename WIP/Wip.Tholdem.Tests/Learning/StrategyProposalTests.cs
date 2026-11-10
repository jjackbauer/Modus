using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class StrategyProposalTests
{
    [Fact]
    public void Propose_GivenDefaultGenome_ExpectedExactlyOneChangedParameterByOneStep()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.58,
            BbPer100: 14.5,
            AggressionFactor: 1.8,
            ShowdownRate: 0.3,
            Vpip: 0.0);
        var proposer = new DeterministicStrategyProposer();

        var proposal = proposer.Propose(incumbent, analysis, iteration: 1);

        Assert.NotEqual(incumbent, proposal);
        Assert.Equal("Play tight-aggressive heads-up: raise strong hands, fold weak hands to large bets, and size value bets by the sizing multiplier.", proposal.PromptText);

        var changed = new List<string>();
        if (proposal.RaiseThreshold != incumbent.RaiseThreshold) changed.Add("raiseThreshold");
        if (proposal.CallThreshold != incumbent.CallThreshold) changed.Add("callThreshold");
        if (proposal.BluffFrequency != incumbent.BluffFrequency) changed.Add("bluffFrequency");
        if (proposal.SizingMultiplier != incumbent.SizingMultiplier) changed.Add("sizingMultiplier");

        Assert.Single(changed);
        Assert.Equal(DeterministicStrategyProposer.Step, Math.Abs(
            changed[0] switch
            {
                "raiseThreshold" => proposal.RaiseThreshold - incumbent.RaiseThreshold,
                "callThreshold" => proposal.CallThreshold - incumbent.CallThreshold,
                "bluffFrequency" => proposal.BluffFrequency - incumbent.BluffFrequency,
                _ => proposal.SizingMultiplier - incumbent.SizingMultiplier,
            }), 12);

        // Determinism: the same inputs always propose the same genome.
        Assert.Equal(proposal, proposer.Propose(incumbent, analysis, iteration: 1));
        // The iteration parity is part of the selection, so the other parity proposes differently.
        Assert.NotEqual(proposal, proposer.Propose(incumbent, analysis, iteration: 2));
    }

    [Fact]
    public void Propose_GivenClampedBoundaryGenome_ExpectedAChangedGenomeEveryTime()
    {
        var proposer = new DeterministicStrategyProposer();
        var boundaryGenomes = new[]
        {
            new StrategyGenome(RaiseThreshold: 1.0, CallThreshold: 0.0, BluffFrequency: 0.0, SizingMultiplier: 0.25),
            new StrategyGenome(RaiseThreshold: 0.0, CallThreshold: 1.0, BluffFrequency: 1.0, SizingMultiplier: 3.0),
            new StrategyGenome(RaiseThreshold: 0.98, CallThreshold: 0.99, BluffFrequency: 0.0, SizingMultiplier: 2.98),
        };
        var analyses = new[]
        {
            new SessionAnalysis(120, 0.2, -85.0, 0.2, 0.0, 0.0),
            new SessionAnalysis(120, 0.9, 120.0, 4.0, 1.0, 0.0),
        };

        foreach (var genome in boundaryGenomes)
        {
            foreach (var analysis in analyses)
            {
                for (var iteration = 1; iteration <= 3; iteration++)
                {
                    var proposal = proposer.Propose(genome, analysis, iteration);
                    Assert.NotEqual(genome, proposal);
                    Assert.InRange(proposal.RaiseThreshold, 0.0, 1.0);
                    Assert.InRange(proposal.CallThreshold, 0.0, 1.0);
                    Assert.InRange(proposal.BluffFrequency, 0.0, 1.0);
                    Assert.InRange(proposal.SizingMultiplier, 0.25, 3.0);
                }
            }
        }
    }
}
