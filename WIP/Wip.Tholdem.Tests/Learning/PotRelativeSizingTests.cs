using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class PotRelativeSizingTests
{
    [Fact]
    public void Target_GivenPotFractions_ReturnsDistinctTargets()
    {
        var fractions = new[] { 0.25, 0.5, 0.75, 1.0 };
        var expected = new long[] { 50, 100, 150, 200 };

        for (var index = 0; index < fractions.Length; index++)
        {
            var target = PotRelativeSizing.Target(
                pot: 200,
                amountToCall: 0,
                ownStreetCommitment: 0,
                potFraction: fractions[index],
                minRaiseTo: 1,
                maxRaiseTo: 1000);

            Assert.Equal(expected[index], target);
        }
    }

    [Fact]
    public void Target_GivenMinRaiseAboveTarget_ClampsToMin()
    {
        var target = PotRelativeSizing.Target(20, 0, 0, 0.25, 10, 1000);

        Assert.Equal(10L, target);
    }

    [Fact]
    public void Target_GivenMaxBelowTarget_ClampsToMax()
    {
        var target = PotRelativeSizing.Target(1000, 0, 0, 1.0, 1, 300);

        Assert.Equal(300L, target);
    }

    [Fact]
    public void Target_GivenCallAndOwnCommitment_AddsBoth()
    {
        var target = PotRelativeSizing.Target(100, 50, 20, 0.5, 1, 1000);

        Assert.Equal(145L, target);
    }

    [Fact]
    public void Target_GivenInvalidFraction_Throws()
    {
        foreach (var fraction in new[] { 0.0, -0.1, 2.1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PotRelativeSizing.Target(100, 0, 0, fraction, 1, 1000));
        }
    }
}