using Wip.Tholdem.Agents;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class SeedDerivationTests
{
    [Fact]
    public void Derive_GivenPinnedAcesContext_ExpectedExactSplitmix64Literal()
    {
        var value = SeedDerivation.Derive(42UL, 1, 0, 0, 0UL);

        Assert.Equal(13149409904026330690UL, value);
        Assert.Equal(0xB67C16A39F7E2642UL, value);
        Assert.Equal(value, SeedDerivation.Derive(42UL, 1, 0, 0, 0UL));
    }

    [Fact]
    public void Derive_GivenTenThousandContextCombinations_ExpectedAllValuesDistinct()
    {
        var seen = new HashSet<ulong>();
        var combinations = 0;
        for (var hand = 1; hand <= 50; hand++)
        {
            for (var seat = 0; seat <= 1; seat++)
            {
                for (var action = 0; action <= 9; action++)
                {
                    for (ulong stream = 0; stream <= 9; stream++)
                    {
                        var value = SeedDerivation.Derive(42UL, hand, seat, action, stream);
                        Assert.Equal(value, SeedDerivation.Derive(42UL, hand, seat, action, stream));
                        seen.Add(value);
                        combinations++;
                    }
                }
            }
        }

        Assert.Equal(10000, combinations);
        Assert.Equal(10000, seen.Count);
    }
}