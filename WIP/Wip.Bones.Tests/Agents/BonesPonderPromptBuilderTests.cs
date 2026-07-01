using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesPonderPromptBuilderTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.PonderScriptAuthoringPrompt;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPonderPromptBuilder_GivenObservations_ExpectedUserPromptRequiresIbonesPlayerSlotCSharpClass()
    {
        var playerId = new BonesPlayerId(1);
        var observations = new[]
        {
            new BonesObservationTranscript(
                new BonesGameId("observe-1"),
                playerId,
                "Seat 1 watched a Left play open 6-4."),
        };

        var request = BonesPonderPromptBuilder.BuildAuthoringRequest(
            playerId,
            observations,
            ["bones-player-1-observation-observe-1"]);

        var systemPrompt = request.Messages.Single(message => message.Role == "system").Content;
        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, systemPrompt, StringComparison.Ordinal);
        Assert.Contains("compilable C#", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not output markdown rules", systemPrompt, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(BonesPonderPromptBuilder.ScriptOutputFormatHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("```csharp", userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, userPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Rules", userPrompt, StringComparison.Ordinal);
        Assert.Contains("observe-1", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPonderPromptBuilder_GivenObservations_ExpectedUserPromptIncludesScriptApiReferenceExcerpt()
    {
        var playerId = new BonesPlayerId(2);
        var request = BonesPonderPromptBuilder.BuildAuthoringRequest(
            playerId,
            [],
            []);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.StateTypeName, userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.ChooseMoveMethodName, userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.LegalMoveConstraint, userPrompt, StringComparison.Ordinal);
        Assert.Contains("Wip.Bones.Domain", userPrompt, StringComparison.Ordinal);
        Assert.Contains("public sealed class SeatStrategy : IBonesPlayerSlot", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPonderPromptBuilder_GivenObservations_ExpectedSystemPromptStatesDeterministicChooseMoveContract()
    {
        var playerId = new BonesPlayerId(3);
        var request = BonesPonderPromptBuilder.BuildAuthoringRequest(playerId, [], []);

        var systemPrompt = request.Messages.Single(message => message.Role == "system").Content;

        Assert.Contains(BonesStrategyScriptApiReference.ChooseMoveMethodName, systemPrompt, StringComparison.Ordinal);
        Assert.Contains("deterministically", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no further LLM", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BonesStrategyScriptApiReference.LegalMoveConstraint, systemPrompt, StringComparison.Ordinal);
    }
}