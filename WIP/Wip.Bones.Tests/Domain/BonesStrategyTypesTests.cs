using System.Text.Json;
using Wip.Bones.Domain;
using Xunit;

namespace Wip.Bones.Tests.Domain;

public sealed class BonesStrategyTypesTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.StrategyKindEnums;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyKind_GivenDefinedMembers_ExpectedExposeScriptAndMarkdown()
    {
        Assert.Equal(1, (int)BonesStrategyKind.Script);
        Assert.Equal(2, (int)BonesStrategyKind.Markdown);
        Assert.True(Enum.IsDefined(BonesStrategyKind.Script));
        Assert.True(Enum.IsDefined(BonesStrategyKind.Markdown));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPromotionStatus_GivenDefinedMembers_ExpectedExposeAllPromotionStates()
    {
        Assert.Equal(1, (int)BonesPromotionStatus.Active);
        Assert.Equal(2, (int)BonesPromotionStatus.Candidate);
        Assert.Equal(3, (int)BonesPromotionStatus.Rejected);
        Assert.Equal(4, (int)BonesPromotionStatus.Superseded);
        Assert.True(Enum.IsDefined(BonesPromotionStatus.Active));
        Assert.True(Enum.IsDefined(BonesPromotionStatus.Candidate));
        Assert.True(Enum.IsDefined(BonesPromotionStatus.Rejected));
        Assert.True(Enum.IsDefined(BonesPromotionStatus.Superseded));
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(BonesStrategyKind.Script, "\"Script\"")]
    [InlineData(BonesStrategyKind.Markdown, "\"Markdown\"")]
    public void BonesStrategyKind_GivenSerializationRoundTrip_PreservesValue(BonesStrategyKind kind, string expectedJson)
    {
        var json = JsonSerializer.Serialize(kind);

        Assert.Equal(expectedJson, json);
        Assert.Equal(kind, JsonSerializer.Deserialize<BonesStrategyKind>(json));
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(BonesPromotionStatus.Active, "\"Active\"")]
    [InlineData(BonesPromotionStatus.Candidate, "\"Candidate\"")]
    [InlineData(BonesPromotionStatus.Rejected, "\"Rejected\"")]
    [InlineData(BonesPromotionStatus.Superseded, "\"Superseded\"")]
    public void BonesPromotionStatus_GivenSerializationRoundTrip_PreservesValue(
        BonesPromotionStatus status,
        string expectedJson)
    {
        var json = JsonSerializer.Serialize(status);

        Assert.Equal(expectedJson, json);
        Assert.Equal(status, JsonSerializer.Deserialize<BonesPromotionStatus>(json));
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(99)]
    [InlineData(0)]
    [InlineData(-1)]
    public void BonesStrategyKind_GivenInvalidNumericValue_ExpectedTryParseReturnsFalse(int value)
    {
        Assert.False(BonesStrategyKindParser.TryParse(value, out _));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BonesStrategyKind>(value.ToString()));
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData("\"Unknown\"")]
    [InlineData("\"active\"")]
    [InlineData("\"\"")]
    public void BonesPromotionStatus_GivenInvalidStringValue_ExpectedTryParseAndJsonDeserializationFail(string json)
    {
        var text = json.Trim('"');
        Assert.False(BonesPromotionStatusParser.TryParse(text, out _));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BonesPromotionStatus>(json));
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData("Script", BonesStrategyKind.Script)]
    [InlineData("Markdown", BonesStrategyKind.Markdown)]
    public void BonesStrategyKind_GivenValidName_ExpectedTryParseReturnsTrue(string name, BonesStrategyKind expected)
    {
        Assert.True(BonesStrategyKindParser.TryParse(name, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData("Active", BonesPromotionStatus.Active)]
    [InlineData("Candidate", BonesPromotionStatus.Candidate)]
    [InlineData("Rejected", BonesPromotionStatus.Rejected)]
    [InlineData("Superseded", BonesPromotionStatus.Superseded)]
    public void BonesPromotionStatus_GivenValidName_ExpectedTryParseReturnsTrue(string name, BonesPromotionStatus expected)
    {
        Assert.True(BonesPromotionStatusParser.TryParse(name, out var status));
        Assert.Equal(expected, status);
    }
}