using System.Text;
using Wip.Tholdem.Agents.Learning;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class StrategyArtifactTests
{
    private const string DefaultPrompt =
        "Play tight-aggressive heads-up: raise strong hands, fold weak hands to large bets, and size value bets by the sizing multiplier.";

    [Fact]
    public void Create_GivenSameGenomeAndIteration_ExpectedIdenticalIdVersionAndHash()
    {
        var genome = new StrategyGenome(
            RaiseThreshold: 0.68,
            CallThreshold: 0.41,
            BluffFrequency: 0.2,
            SizingMultiplier: 1.5,
            PromptText: "raise the top of the range");

        var first = StrategyArtifact.Create(genome, 3, null);
        var second = StrategyArtifact.Create(genome, 3, null);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
        Assert.Equal(1, first.Version);
        Assert.Null(first.ParentId);
        Assert.Equal(3, first.CreatedAtIteration);
        Assert.Matches("^[0-9a-f]{64}$", first.Hash);
        Assert.Equal(64, first.Hash.Length);
        Assert.Equal("tholdem-strategy-v1-" + first.Hash[..8], first.Id);
        Assert.Equal(first.Hash, LearningTestSupport.Sha256Hex(Encoding.UTF8.GetBytes(first.ToHashInputJson())));

        var different = StrategyArtifact.Create(genome with { SizingMultiplier = 1.6 }, 3, null);
        Assert.NotEqual(first.Hash, different.Hash);
        Assert.NotEqual(first.Id, different.Id);
    }

    [Fact]
    public void Create_GivenParentChain_ExpectedIncrementedVersionAndParentLink()
    {
        var root = StrategyArtifact.Create(new StrategyGenome(), 0, null);
        var second = StrategyArtifact.Create(new StrategyGenome(RaiseThreshold: 0.7), 1, root);
        var third = StrategyArtifact.Create(new StrategyGenome(RaiseThreshold: 0.68), 2, second);

        Assert.Equal(1, root.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(3, third.Version);
        Assert.Equal(root.Id, second.ParentId);
        Assert.Equal(second.Id, third.ParentId);
        Assert.StartsWith("tholdem-strategy-v2-", second.Id);
        Assert.StartsWith("tholdem-strategy-v3-", third.Id);
        Assert.NotEqual(root.Hash, second.Hash);
        Assert.NotEqual(second.Hash, third.Hash);

        // The parent link is part of the hashed payload: a different parent with the same genome and
        // version must produce a different content address.
        var alternateRoot = StrategyArtifact.Create(new StrategyGenome(CallThreshold: 0.44), 0, null);
        var sibling = StrategyArtifact.Create(new StrategyGenome(CallThreshold: 0.4), 1, root);
        var otherParent = StrategyArtifact.Create(new StrategyGenome(CallThreshold: 0.4), 1, alternateRoot);
        Assert.Equal(sibling.Version, otherParent.Version);
        Assert.NotEqual(sibling.ParentId, otherParent.ParentId);
        Assert.NotEqual(sibling.Hash, otherParent.Hash);
    }

    [Fact]
    public void ToCanonicalJson_GivenDefaultGenome_ExpectedFixedOrderParametersAndNoTimestamp()
    {
        var artifact = StrategyArtifact.Create(new StrategyGenome(), 4, null);

        var expected =
            "{\"id\":\"" + artifact.Id + "\",\"version\":1,\"parentId\":null,\"createdAtIteration\":4,"
            + "\"parameters\":{\"raiseThreshold\":0.72,\"callThreshold\":0.45,\"bluffFrequency\":0.12,\"sizingMultiplier\":1},"
            + "\"promptText\":\"" + DefaultPrompt + "\"}";

        Assert.Equal(expected, artifact.ToCanonicalJson());

        var payload =
            "{\"version\":1,\"parentId\":null,\"createdAtIteration\":4,"
            + "\"parameters\":{\"raiseThreshold\":0.72,\"callThreshold\":0.45,\"bluffFrequency\":0.12,\"sizingMultiplier\":1},"
            + "\"promptText\":\"" + DefaultPrompt + "\"}";

        Assert.Equal(payload, artifact.ToHashInputJson());
        Assert.Equal(LearningTestSupport.Sha256Hex(LearningTestSupport.Utf8(payload)), artifact.Hash);

        // No clocks: the content address of the same genome at the same iteration never moves.
        Assert.DoesNotContain("timestamp", artifact.ToCanonicalJson(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAt\"", artifact.ToCanonicalJson(), StringComparison.Ordinal);
        Assert.Equal(artifact, StrategyArtifact.Create(new StrategyGenome(), 4, null));
    }
}
