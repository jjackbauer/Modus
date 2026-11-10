using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Tholdem.Agents.Runtime;
using Xunit;

namespace Wip.Tholdem.Tests.Runtime;

public sealed class ToolContractTests
{
    private static CapabilityContext Context()
        => new(new SessionId("tool-contract-test"), AppContext.BaseDirectory);

    [Fact]
    public async Task PotOddsTool_GivenCallAmounts_ExpectedContractedPotOddsAndValidation()
    {
        var tool = new PotOddsTool();

        var thirds = await tool.ExecuteAsync(new PotOddsRequest(100, 50), Context(), CancellationToken.None);
        Assert.Equal(1.0 / 3.0, thirds.PotOdds, 6);
        Assert.Equal(1.0 / 3.0, thirds.BreakEvenEquity, 6);
        Assert.Equal(50L, thirds.CallAmount);

        var noCall = await tool.ExecuteAsync(new PotOddsRequest(200, 0), Context(), CancellationToken.None);
        Assert.Equal(0d, noCall.PotOdds);
        Assert.Equal(0d, noCall.BreakEvenEquity);
        Assert.Equal(0L, noCall.CallAmount);

        var empty = await tool.ExecuteAsync(new PotOddsRequest(0, 0), Context(), CancellationToken.None);
        Assert.Equal(0d, empty.PotOdds);

        var certain = await tool.ExecuteAsync(new PotOddsRequest(0, 25), Context(), CancellationToken.None);
        Assert.Equal(1d, certain.PotOdds, 6);
        Assert.Equal(25L, certain.CallAmount);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await tool.ExecuteAsync(new PotOddsRequest(-1, 5), Context(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await tool.ExecuteAsync(new PotOddsRequest(10, -5), Context(), CancellationToken.None));
    }

    [Fact]
    public void TholdemToolCatalog_GivenDefinitionsAndDescriptors_ExpectedContractedNamesSchemasAndPermissions()
    {
        var names = new[] { "equity-calculator", "pot-odds", "hand-history-query", "opponent-statistics" };

        var definitions = TholdemToolCatalog.CreateDefinitions();
        Assert.Equal(4, definitions.Count);
        Assert.Equal(names, definitions.Select(definition => definition.Name).ToArray());
        Assert.All(definitions, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Description)));

        foreach (var definition in definitions)
        {
            Assert.Equal("object", definition.ParametersSchema.GetProperty("type").GetString());
            Assert.False(definition.ParametersSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(JsonValueKind.Object, definition.ParametersSchema.GetProperty("properties").ValueKind);
            Assert.Equal(JsonValueKind.Array, definition.ParametersSchema.GetProperty("required").ValueKind);
        }

        var equity = definitions[0].ParametersSchema.GetProperty("properties");
        Assert.Equal("array", equity.GetProperty("holeCards").GetProperty("type").GetString());
        Assert.Equal("array", equity.GetProperty("board").GetProperty("type").GetString());
        Assert.Equal("integer", equity.GetProperty("samples").GetProperty("type").GetString());
        Assert.Equal("integer", equity.GetProperty("seed").GetProperty("type").GetString());
        Assert.Equal(
            new[] { "holeCards" },
            definitions[0].ParametersSchema.GetProperty("required").EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());

        var potOdds = definitions[1].ParametersSchema.GetProperty("properties");
        Assert.Equal("integer", potOdds.GetProperty("pot").GetProperty("type").GetString());
        Assert.Equal("integer", potOdds.GetProperty("amountToCall").GetProperty("type").GetString());
        Assert.Equal(
            new[] { "pot", "amountToCall" },
            definitions[1].ParametersSchema.GetProperty("required").EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());

        Assert.Equal(
            "integer",
            definitions[2].ParametersSchema.GetProperty("properties").GetProperty("limit").GetProperty("type").GetString());
        Assert.Empty(definitions[3].ParametersSchema.GetProperty("properties").EnumerateObject());

        var descriptors = TholdemToolCatalog.CreateDescriptors();
        Assert.Equal(4, descriptors.Count);
        Assert.Equal(names, descriptors.Select(descriptor => descriptor.CapabilityId.Value).ToArray());
        Assert.All(descriptors, descriptor => Assert.Equal(CapabilityKind.Tool, descriptor.Kind));
        Assert.Empty(descriptors[0].RequiredPermissions);
        Assert.Empty(descriptors[1].RequiredPermissions);
        Assert.Equal(new[] { "artifact.read" }, descriptors[2].RequiredPermissions.Select(permission => permission.Value).ToArray());
        Assert.Equal(new[] { "artifact.read" }, descriptors[3].RequiredPermissions.Select(permission => permission.Value).ToArray());
        Assert.Equal(typeof(EquityTool), descriptors[0].CapabilityType);
        Assert.Equal(typeof(PotOddsTool), descriptors[1].CapabilityType);
        Assert.Equal(typeof(HandHistoryTool), descriptors[2].CapabilityType);
        Assert.Equal(typeof(OpponentStatsTool), descriptors[3].CapabilityType);
        Assert.Equal(typeof(EquityToolRequest), descriptors[0].RequestType);
        Assert.Equal(typeof(EquityToolResult), descriptors[0].ResultType);
        Assert.Equal(typeof(PotOddsRequest), descriptors[1].RequestType);
        Assert.Equal(typeof(PotOddsResult), descriptors[1].ResultType);
        Assert.Equal(typeof(HandHistoryRequest), descriptors[2].RequestType);
        Assert.Equal(typeof(HandHistoryResult), descriptors[2].ResultType);
        Assert.Equal(typeof(OpponentStatsRequest), descriptors[3].RequestType);
        Assert.Equal(typeof(OpponentStatsResult), descriptors[3].ResultType);
    }
}
