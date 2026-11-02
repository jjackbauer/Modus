using Wip.Bones.Engine;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesSeedDerivationTests
{
    [Fact]
    public void Derive_GivenFixedInputs_ExpectedProcessStableValues()
    {
        // Hard-coded values are the cross-process contract. They were produced by this
        // implementation and must never change when HashCode randomization changes.
        Assert.Equal(634228473, BonesSeedDerivation.Derive(900001, 0));
        Assert.Equal(203577849, BonesSeedDerivation.Derive(900001, 1));
        Assert.Equal(442671693, BonesSeedDerivation.Derive(900001, 2));
        Assert.Equal(1335371332, BonesSeedDerivation.Derive(900001, 3));
        Assert.Equal(2010818576, BonesSeedDerivation.Derive(900001, 4));
    }

    [Fact]
    public void Derive_GivenSameInputs_ExpectedSameSeed()
    {
        Assert.Equal(
            BonesSeedDerivation.Derive(4242, 7, 3, 9),
            BonesSeedDerivation.Derive(4242, 7, 3, 9));
    }

    [Fact]
    public void Derive_GivenDifferentPartOrder_ExpectedDifferentSeed()
    {
        Assert.NotEqual(
            BonesSeedDerivation.Derive(4242, 7, 3),
            BonesSeedDerivation.Derive(4242, 3, 7));
    }

    [Fact]
    public void Derive_GivenNegativeInputs_ExpectedNonNegativeSeed()
    {
        for (var seed = -50; seed <= 50; seed++)
        {
            for (var part = -5; part <= 5; part++)
            {
                Assert.InRange(BonesSeedDerivation.Derive(seed, part), 0, int.MaxValue);
            }
        }
    }

    [Fact]
    public void DeriveWithLabel_GivenSameLabelAndParts_ExpectedSameSeed()
    {
        Assert.Equal(
            BonesSeedDerivation.DeriveWithLabel(0, "seat-1-v3", 2, 3),
            BonesSeedDerivation.DeriveWithLabel(0, "seat-1-v3", 2, 3));
    }

    [Fact]
    public void DeriveWithLabel_GivenDifferentLabel_ExpectedDifferentSeed()
    {
        Assert.NotEqual(
            BonesSeedDerivation.DeriveWithLabel(0, "seat-1-v3"),
            BonesSeedDerivation.DeriveWithLabel(0, "seat-1-v4"));
    }
}
