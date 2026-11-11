using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-one protected seed domains: seeds are deterministic, purpose-separated, immutable and
/// content-addressed, and concrete seed overlap between domains is refused.
/// </summary>
public sealed class SeedDomainTests
{
    private const string HoldoutTwoSeedsJson =
        "{\"schema\":\"tholdem.seed-domain.v1\",\"domainId\":\"holdout-a\",\"purpose\":\"Holdout\",\"baseSeed\":123,\"count\":2,\"seeds\":[1302179516557818905,9576118770318801856]}";

    [Fact]
    public void Seeds_GivenCount_AreDeterministicDistinctAndImmutable()
    {
        var first = Domain("holdout-a", SeedPurpose.Holdout, 12345UL, 5);
        var second = Domain("holdout-a", SeedPurpose.Holdout, 12345UL, 5);

        Assert.Equal(first.Seeds.ToArray(), second.Seeds.ToArray());
        Assert.Equal(5, first.Seeds.Count);
        Assert.Equal(5, first.Seeds.Distinct().Count());
        Assert.NotSame(first.Seeds, first.Seeds);

        var initial = first.Seeds.ToArray();
        var returned = Assert.IsAssignableFrom<ulong[]>(first.Seeds);
        returned[0] ^= 1UL;

        Assert.NotEqual(initial[0], returned[0]);
        Assert.Equal(initial, first.Seeds);
    }

    [Fact]
    public void Seeds_GivenDifferentPurpose_Differ()
    {
        var training = Domain("holdout-a", SeedPurpose.Training, 12345UL, 4);
        var holdout = Domain("holdout-a", SeedPurpose.Holdout, 12345UL, 4);

        Assert.False(training.Seeds.SequenceEqual(holdout.Seeds));
    }

    [Fact]
    public void ValidateDisjoint_GivenIdenticalDomains_ThrowsWithSharedSeed()
    {
        var left = Domain("holdout", SeedPurpose.Holdout, 99UL, 4);
        var right = Domain("holdout", SeedPurpose.Holdout, 99UL, 4);

        var exception = Assert.Throws<SeedDomainOverlapException>(
            () => SeedDomain.ValidateDisjoint(new[] { left, right }));

        Assert.Equal(left.Seeds[0], exception.Seed);
        Assert.Equal("holdout", exception.LeftDomainId);
        Assert.Equal("holdout", exception.RightDomainId);
    }

    [Fact]
    public void ValidateDisjoint_GivenSameBaseDifferentPurposes_DoesNotThrow()
    {
        var domains = new[]
        {
            Domain("training", SeedPurpose.Training, 12345UL, 6),
            Domain("selection", SeedPurpose.Selection, 12345UL, 6),
            Domain("holdout", SeedPurpose.Holdout, 12345UL, 6),
            Domain("audit", SeedPurpose.Audit, 12345UL, 6),
        };

        SeedDomain.ValidateDisjoint(domains);

        Assert.Equal(24, domains.SelectMany(domain => domain.Seeds).Distinct().Count());
    }

    [Fact]
    public void DomainHash_GivenSameSeeds_IsStable()
    {
        var first = Domain("holdout-a", SeedPurpose.Holdout, 123UL, 2);
        var second = Domain("holdout-a", SeedPurpose.Holdout, 123UL, 2);

        Assert.Equal(HoldoutTwoSeedsJson, first.ToCanonicalJson());
        Assert.Equal(first.DomainHash, second.DomainHash);
        Assert.Equal(64, first.DomainHash.Length);
        Assert.Equal(StrategyArtifact.ComputeHash(first.ToCanonicalJson()), first.DomainHash);
        Assert.NotEqual(first.DomainHash, Domain("holdout-a", SeedPurpose.Holdout, 124UL, 2).DomainHash);
    }

    private static SeedDomain Domain(string domainId, SeedPurpose purpose, ulong baseSeed, int count) =>
        new(domainId, purpose, baseSeed, count);
}
