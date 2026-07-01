using Wip.Bones.Agents.Script;
using Wip.Bones.Engine;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesStrategyScriptResponseParserTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.ScriptResponseParser;

    private const string ValidScriptBody =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        }
        """;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptResponseParser_GivenFencedCsharpBlock_ExpectedExtractsCompleteSource()
    {
        var response = $"""
            Here is the strategy script:

            ```csharp
            {ValidScriptBody}
            ```
            """;

        var parsed = BonesStrategyScriptResponseParser.TryParseScriptSource(response);

        Assert.NotNull(parsed);
        Assert.Contains("public sealed class SeatStrategy : IBonesPlayerSlot", parsed, StringComparison.Ordinal);
        Assert.Contains("return legalMoves[0];", parsed, StringComparison.Ordinal);
        Assert.DoesNotContain("```", parsed, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptResponseParser_GivenResponseWithoutIbonesPlayerSlot_ExpectedReturnsNullForRejection()
    {
        var response = """
            ```csharp
            public sealed class SeatStrategy
            {
                public int PickTile() => 0;
            }
            ```
            """;

        var parsed = BonesStrategyScriptResponseParser.TryParseScriptSource(response);

        Assert.Null(parsed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptResponseParser_GivenUnfencedSourceWithInterface_ExpectedReturnsNullForRejection()
    {
        var parsed = BonesStrategyScriptResponseParser.TryParseScriptSource(ValidScriptBody);

        Assert.Null(parsed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptResponseParser_GivenCsFenceTag_ExpectedExtractsCompleteSource()
    {
        var response = $"""
            ```cs
            {ValidScriptBody}
            ```
            """;

        var parsed = BonesStrategyScriptResponseParser.TryParseScriptSource(response);

        Assert.NotNull(parsed);
        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, parsed, StringComparison.Ordinal);
    }
}