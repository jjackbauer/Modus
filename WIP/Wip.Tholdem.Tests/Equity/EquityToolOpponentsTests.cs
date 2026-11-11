using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Tholdem.Agents.Equity;
using Wip.Tholdem.Agents.Runtime;
using Wip.Tholdem.Cards;
using Xunit;

namespace Wip.Tholdem.Tests.Equity;

public sealed class EquityToolOpponentsTests
{
    private static CapabilityContext Context()
        => new(new SessionId("equity-tool-opponents-test"), AppContext.BaseDirectory);

    [Fact]
    public async Task EquityTool_GivenDefaultOpponents_MatchesLegacyEstimate()
    {
        var tool = new EquityTool(sampleBudget: 2000);
        var request = new EquityToolRequest(
            new[] { "As", "Ah" },
            Array.Empty<string>(),
            Samples: 2000,
            Seed: 7);

        var result = await tool.ExecuteAsync(request, Context(), CancellationToken.None);
        var legacy = MonteCarloEquity.Estimate(
            TestCards.Parse("As Ah"),
            Array.Empty<Card>(),
            7UL,
            2000);

        Assert.Equal(legacy.Equity, result.Equity);
        Assert.Equal(legacy.Samples, result.Samples);
        Assert.Equal(legacy.Wins, result.Wins);
        Assert.Equal(legacy.Ties, result.Ties);
        Assert.Equal(legacy.Losses, result.Losses);
        Assert.Equal(result.Equity, result.ExpectedPotShare);
        Assert.Equal(1, result.Opponents);
    }

    [Fact]
    public async Task EquityTool_GivenThreeOpponents_ReturnsPotShareAndCounts()
    {
        const int samples = 5000;
        const ulong seed = 11UL;
        var tool = new EquityTool(sampleBudget: 5000);
        var request = new EquityToolRequest(
            new[] { "As", "Ah" },
            new[] { "Kd", "7c", "2s", "9h", "3d" },
            Samples: samples,
            Seed: seed,
            Opponents: 3);

        var result = await tool.ExecuteAsync(request, Context(), CancellationToken.None);
        var estimate = MultiwayEquity.Estimate(
            TestCards.Parse("As Ah"),
            TestCards.Parse("Kd 7c 2s 9h 3d"),
            3,
            seed,
            samples);

        Assert.Equal(3, result.Opponents);
        Assert.Equal(samples, result.Samples);
        Assert.Equal(result.Samples, result.Wins + result.Ties + result.Losses);
        Assert.InRange(result.ExpectedPotShare, 0.0, 1.0);
        Assert.Equal(result.ExpectedPotShare, result.Equity);
        Assert.Equal(estimate.ExpectedPotShare, result.ExpectedPotShare);
        Assert.Equal(estimate.Wins, result.Wins);
        Assert.Equal(estimate.Ties, result.Ties);
        Assert.Equal(estimate.Losses, result.Losses);
    }

    [Fact]
    public async Task EquityTool_GivenSixOpponents_Throws()
    {
        var tool = new EquityTool(sampleBudget: 2000);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await tool.ExecuteAsync(
                new EquityToolRequest(
                    new[] { "As", "Ah" },
                    Array.Empty<string>(),
                    Opponents: 6),
                Context(),
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await tool.ExecuteAsync(
                new EquityToolRequest(
                    new[] { "As", "Ah" },
                    Array.Empty<string>(),
                    Opponents: 0),
                Context(),
                CancellationToken.None));
    }

    [Fact]
    public void EquityTool_GivenCatalog_DeclaresOptionalOpponentsInteger()
    {
        var definitions = TholdemToolCatalog.CreateDefinitions();
        Assert.Equal(4, definitions.Count);

        var schema = definitions[0].ParametersSchema;
        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("opponents", out var opponents));
        Assert.Equal("integer", opponents.GetProperty("type").GetString());
        Assert.Equal(1, opponents.GetProperty("minimum").GetInt32());

        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.DoesNotContain("opponents", required);
    }
}
