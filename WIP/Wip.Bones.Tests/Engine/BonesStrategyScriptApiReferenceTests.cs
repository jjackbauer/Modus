using Wip.Bones.Engine;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesStrategyScriptApiReferenceTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.ScriptApiReference;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptApiReference_GivenContractExcerpt_ExpectedContainsIBonesPlayerSlot()
    {
        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, BonesStrategyScriptApiReference.ContractExcerpt);
        Assert.Equal(nameof(IBonesPlayerSlot), BonesStrategyScriptApiReference.InterfaceName);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptApiReference_GivenContractExcerpt_ExpectedContainsBonesRoundState()
    {
        Assert.Contains(BonesStrategyScriptApiReference.StateTypeName, BonesStrategyScriptApiReference.ContractExcerpt);
        Assert.Contains(BonesStrategyScriptApiReference.LegalMovesParameterType, BonesStrategyScriptApiReference.ContractExcerpt);
        Assert.Contains(BonesStrategyScriptApiReference.GetLegalMovesSource, BonesStrategyScriptApiReference.ContractExcerpt);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptApiReference_GivenContractExcerpt_ExpectedDocumentsLegalMoveConstraint()
    {
        Assert.Contains(BonesStrategyScriptApiReference.LegalMoveConstraint, BonesStrategyScriptApiReference.ContractExcerpt);
        Assert.Contains("exactly one", BonesStrategyScriptApiReference.ContractExcerpt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("throw", BonesStrategyScriptApiReference.ContractExcerpt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptApiReference_GivenContractExcerpt_ExpectedDocumentsBonesMoveMembers()
    {
        var excerpt = BonesStrategyScriptApiReference.ContractExcerpt;

        Assert.Contains("BonesMove: MoveId, PlayerId, Tile", excerpt);
        Assert.Contains("BonesTile: LowPip, HighPip", excerpt);
        Assert.Contains(BonesStrategyScriptApiReference.MoveSelectionConstraint, excerpt);
        Assert.DoesNotContain("TileToPlay", excerpt);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptApiReference_GivenContractExcerpt_ExpectedDocumentsBoardEndAndForbiddenMembers()
    {
        var excerpt = BonesStrategyScriptApiReference.ContractExcerpt;

        Assert.Contains("BonesBoardEnd: Pip", excerpt);
        Assert.Contains("BonesPipCount: Value", excerpt);
        Assert.Contains("BonesBoard.GetAdjacent", excerpt);
        Assert.Contains("BonesBoardEnd.LowPip", excerpt);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesStrategyScriptApiReference_GivenAllowedNamespaces_ExpectedListsDomainIdentifiersAndEngine()
    {
        Assert.Equal(3, BonesStrategyScriptApiReference.AllowedNamespaces.Count);
        Assert.Contains("Wip.Bones.Domain", BonesStrategyScriptApiReference.AllowedNamespaces);
        Assert.Contains("Wip.Bones.Identifiers", BonesStrategyScriptApiReference.AllowedNamespaces);
        Assert.Contains("Wip.Bones.Engine", BonesStrategyScriptApiReference.AllowedNamespaces);

        foreach (var allowedNamespace in BonesStrategyScriptApiReference.AllowedNamespaces)
        {
            Assert.Contains(allowedNamespace, BonesStrategyScriptApiReference.ContractExcerpt);
        }
    }
}