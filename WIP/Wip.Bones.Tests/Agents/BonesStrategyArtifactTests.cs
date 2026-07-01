using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesStrategyArtifactTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.StrategyArtifact;

    private const string ValidScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class Seat2ScriptStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves) =>
                legalMoves[0];
        }
        """;

    private const string ValidMarkdownSource =
        "# Strategy\nPrefer highest double on opening lead.";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyArtifact_GivenScriptKindWithValidSource_ConstructsWithScriptKindAndSource()
    {
        var strategyId = new BonesStrategyId("seat-2-script-v1");
        var playerId = new BonesPlayerId(2);

        var artifact = new BonesStrategyArtifact(
            strategyId,
            playerId,
            BonesStrategyKind.Script,
            ValidScriptSource,
            BonesPromotionStatus.Active);

        Assert.Equal(strategyId, artifact.StrategyId);
        Assert.Equal(playerId, artifact.PlayerId);
        Assert.Equal(BonesStrategyKind.Script, artifact.Kind);
        Assert.Equal(ValidScriptSource, artifact.Source);
        Assert.Equal(BonesPromotionStatus.Active, artifact.PromotionStatus);
    }

    [Theory]
    [Trait("ChecklistItem", ChecklistItem)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BonesStrategyArtifact_GivenScriptKindWithEmptySource_ExpectedDeterministicValidationFailure(string? source)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new BonesStrategyArtifact(
                new BonesStrategyId("seat-2-script-v1"),
                new BonesPlayerId(2),
                BonesStrategyKind.Script,
                source!,
                BonesPromotionStatus.Active));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyArtifact_GivenMarkdownKind_ExpectedRetainsLegacyMarkdownPlayPath()
    {
        var strategyId = new BonesStrategyId("opening-lead-v1");
        var playerId = new BonesPlayerId(3);

        var artifact = new BonesStrategyArtifact(
            strategyId,
            playerId,
            BonesStrategyKind.Markdown,
            ValidMarkdownSource,
            BonesPromotionStatus.Active);

        Assert.Equal(BonesStrategyKind.Markdown, artifact.Kind);
        Assert.Equal(ValidMarkdownSource, artifact.Source);

        var legacyDocument = new BonesStrategyDocument(strategyId, playerId, ValidMarkdownSource);
        var fromLegacy = BonesStrategyArtifact.FromLegacyDocument(legacyDocument);

        Assert.Equal(legacyDocument.StrategyId, fromLegacy.StrategyId);
        Assert.Equal(legacyDocument.PlayerId, fromLegacy.PlayerId);
        Assert.Equal(BonesStrategyKind.Markdown, fromLegacy.Kind);
        Assert.Equal(legacyDocument.Markdown, fromLegacy.Source);
        Assert.Equal(BonesPromotionStatus.Active, fromLegacy.PromotionStatus);
    }
}