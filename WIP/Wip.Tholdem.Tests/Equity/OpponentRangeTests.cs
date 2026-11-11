using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Cards;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

/// <summary>
/// Contract tests for weighted preflop opponent ranges and the range-conditioned N-opponent Monte
/// Carlo equity estimator. The uniform sampler remains the labelled control.
/// </summary>
public sealed class OpponentRangeTests
{
    [Fact]
    public void Uniform_GivenDefault_Has169ClassesAndStableHash()
    {
        var uniform = OpponentRange.Uniform;

        Assert.Equal("uniform", uniform.RangeId);
        Assert.Equal(169, uniform.Weights.Count);
        Assert.All(uniform.Weights.Values, weight => Assert.Equal(1.0, weight));

        var json = uniform.ToCanonicalJson();
        Assert.StartsWith("{\"schema\":\"tholdem.opponent-range.v1\",\"rangeId\":\"uniform\",\"classes\":[", json);
        Assert.Equal(169, CountOccurrences(json, "\"class\":"));

        var hash = uniform.RangeHash;
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, uniform.RangeHash);
        Assert.Equal(hash, StrategyArtifact.ComputeHash(json));
        Assert.Equal(hash, OpponentRange.Uniform.RangeHash);
    }

    [Fact]
    public void Parse_GivenAcesOnly_WeightsOnlyAces()
    {
        var range = OpponentRange.Parse(
            "aces-only",
            new Dictionary<string, double> { ["AA"] = 1.0 });

        Assert.Equal("aces-only", range.RangeId);
        Assert.Equal(1.0, range.WeightFor(Card.Parse("Ac"), Card.Parse("Ad")));
        Assert.Equal(0.0, range.WeightFor(Card.Parse("Kc"), Card.Parse("Kd")));
        Assert.Equal(0.0, range.WeightFor(Card.Parse("Ac"), Card.Parse("Kd")));
        Assert.Single(range.Weights);

        var json = range.ToCanonicalJson();
        Assert.Equal(1, CountOccurrences(json, "\"class\":"));
        Assert.Contains("\"classes\":[{\"class\":\"AA\",\"weight\":1}]", json);
        Assert.Equal(StrategyArtifact.ComputeHash(json), range.RangeHash);
    }

    [Fact]
    public void Parse_GivenUnknownClassOrNegativeWeight_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            OpponentRange.Parse("unknown", new Dictionary<string, double> { ["AX"] = 1.0 }));
        Assert.Throws<ArgumentException>(() =>
            OpponentRange.Parse("negative", new Dictionary<string, double> { ["AA"] = -0.1 }));
        Assert.Throws<ArgumentException>(() =>
            OpponentRange.Parse("nan", new Dictionary<string, double> { ["AA"] = double.NaN }));
        Assert.Throws<ArgumentException>(() =>
            OpponentRange.Parse("infinite", new Dictionary<string, double> { ["AA"] = double.PositiveInfinity }));
        Assert.Throws<ArgumentException>(() =>
            OpponentRange.Parse(string.Empty, new Dictionary<string, double> { ["AA"] = 1.0 }));
        Assert.Throws<ArgumentException>(() =>
            OpponentRange.Parse("zero", new Dictionary<string, double> { ["AA"] = 0.0 }));
    }

    [Fact]
    public void Estimate_GivenAcesOnlyOpponent_KingKingEquityFalls()
    {
        var hero = Parse("Kh Kd");
        var board = Array.Empty<Card>();
        const int samples = 20000;
        var acesOnly = OpponentRange.Parse(
            "aces-only",
            new Dictionary<string, double> { ["AA"] = 1.0 });

        var uniform = MultiwayEquity.Estimate(hero, board, 1, 20240601UL, samples);
        var conditioned = MultiwayEquity.Estimate(
            hero,
            board,
            new[] { acesOnly },
            20240601UL,
            samples);

        Assert.Equal("uniform", uniform.RangeModelId);
        Assert.Equal("range:" + StrategyArtifact.ComputeHash(acesOnly.RangeHash)[..16], conditioned.RangeModelId);
        Assert.InRange(conditioned.ExpectedPotShare, 0.0, 1.0);
        Assert.InRange(uniform.ExpectedPotShare, 0.0, 1.0);
        Assert.True(
            conditioned.ExpectedPotShare < uniform.ExpectedPotShare - 0.30,
            FormattableString.Invariant(
                $"AA-only pot share {conditioned.ExpectedPotShare:F4} must trail uniform {uniform.ExpectedPotShare:F4} by at least 0.30."));
        Assert.True(
            conditioned.ExpectedPotShare is > 0.10 and < 0.30,
            FormattableString.Invariant(
                $"AA-only pot share for KhKd must sit near 0.18; observed {conditioned.ExpectedPotShare:F4}."));

        var repeated = MultiwayEquity.Estimate(hero, board, new[] { acesOnly }, 20240601UL, samples);
        Assert.Equal(conditioned, repeated);
    }

    [Fact]
    public void Estimate_GivenRangeExhaustedByBoard_Throws()
    {
        var hero = Parse("Ah Ad");
        var board = Parse("Ac As 2h");
        var acesOnly = OpponentRange.Parse(
            "aces-only",
            new Dictionary<string, double> { ["AA"] = 1.0 });

        var error = Assert.Throws<InvalidOperationException>(() =>
            MultiwayEquity.Estimate(hero, board, new[] { acesOnly }, 7UL, 100));

        Assert.Contains("range-exhausted", error.Message);
    }

    [Fact]
    public void Estimate_GivenInvalidRangeList_Throws()
    {
        var hero = Parse("Kh Kd");
        var board = Array.Empty<Card>();

        Assert.ThrowsAny<ArgumentException>(() =>
            MultiwayEquity.Estimate(hero, board, new OpponentRange[] { null! }, 1UL, 10));
        Assert.ThrowsAny<ArgumentException>(() =>
            MultiwayEquity.Estimate(hero, board, Array.Empty<OpponentRange>(), 1UL, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MultiwayEquity.Estimate(
                hero,
                board,
                new[] { OpponentRange.Uniform, OpponentRange.Uniform, OpponentRange.Uniform, OpponentRange.Uniform, OpponentRange.Uniform, OpponentRange.Uniform },
                1UL,
                10));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static Card[] Parse(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card.Parse).ToArray();
}
