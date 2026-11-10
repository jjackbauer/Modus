using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class ZeroSumTests
{
    [Fact]
    public void Run_GivenTwoHundredHandMatch_ExpectedNetChipsSumExactlyZero()
    {
        var report = MatchSimulator.Run(
            new RandomLegalAgent("zero-sum-random"),
            new TightAggressiveAgent(name: "zero-sum-tight"),
            new SimulationOptions(2468UL, 200));

        Assert.Equal(200, report.Agents[0].Hands);
        Assert.Equal(200, report.Agents[1].Hands);
        Assert.Equal(0L, report.Agents[0].NetChips + report.Agents[1].NetChips);
        Assert.True(
            report.Agents[0].NetChips != 0L || report.Agents[1].NetChips != 0L,
            "At least one seat must move chips over two hundred hands.");
    }
}