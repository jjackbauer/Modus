using System.Text;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Mapping;

public sealed class BonesDeepSeekMessageMapperTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.MessageMapper;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekMessageMapper_GivenStrategyAuthoringMessages_ExpectedMapsToDeepSeekChatMessagesInOrder()
    {
        var messages = new[]
        {
            new BonesStrategyAuthoringMessage("system", "You are authoring an initial Block Dominoes strategy for seat 1."),
            new BonesStrategyAuthoringMessage("user", "# Strategy authoring request\n\nPlayer seat: 1"),
        };

        var mapped = BonesDeepSeekMessageMapper.MapStrategyAuthoringMessages(messages);

        Assert.Equal(2, mapped.Count);
        Assert.Equal(
            [
                new DeepSeekChatMessage("system", "You are authoring an initial Block Dominoes strategy for seat 1."),
                new DeepSeekChatMessage("user", "# Strategy authoring request\n\nPlayer seat: 1"),
            ],
            mapped);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekMessageMapper_GivenPlayTurnMessagesWithAllowedMoves_ExpectedUserPromptIncludesMoveEnumeration()
    {
        var moveA = new BonesMoveId("move-play-left");
        var moveB = new BonesMoveId("move-pass");
        var allowedMoves = new[]
        {
            new BonesPlayTurnOption(moveA, "play 3-5 on Left (move id: move-play-left)"),
            new BonesPlayTurnOption(moveB, "pass (move id: move-pass)"),
        };

        var request = new BonesPlayTurnRequest(
            Messages:
            [
                new BonesPlayTurnMessage("system", "Choose exactly one allowed move id from the enumerated options."),
                new BonesPlayTurnMessage("user", BuildPlayTurnUserPrompt(allowedMoves)),
            ],
            AllowedMoves: allowedMoves,
            ActiveSeat: new BonesPlayerId(2),
            StrategyId: new BonesStrategyId("strategy-1"));

        var mapped = BonesDeepSeekMessageMapper.MapPlayTurnMessages(request.Messages);

        Assert.Equal(2, mapped.Count);
        Assert.Equal("system", mapped[0].Role);
        Assert.Equal("user", mapped[1].Role);

        foreach (var option in request.AllowedMoves)
            Assert.Contains(option.MoveId.Value, mapped[1].Content, StringComparison.Ordinal);

        Assert.Contains("## Allowed moves", mapped[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekMessageMapper_GivenEnhancementMessages_ExpectedIncludesPriorStrategyAndOutcomeSections()
    {
        const string priorStrategyMarkdown = "## Rules\n- Prefer high pip tiles early.";
        const string outcomeSummary = "Outcome: Win\nMatch winner seat: 1";

        var userPrompt = new StringBuilder();
        userPrompt.AppendLine("# Strategy enhancement request");
        userPrompt.AppendLine();
        userPrompt.AppendLine("## Prior strategy");
        userPrompt.AppendLine(priorStrategyMarkdown);
        userPrompt.AppendLine();
        userPrompt.AppendLine("## Match outcome summary");
        userPrompt.AppendLine(outcomeSummary);

        var messages = new[]
        {
            new BonesStrategyEnhancementMessage("system", "You are refining Block Dominoes tactics for seat 1."),
            new BonesStrategyEnhancementMessage("user", userPrompt.ToString()),
        };

        var mapped = BonesDeepSeekMessageMapper.MapEnhancementMessages(messages);

        Assert.Equal(2, mapped.Count);
        Assert.Equal("user", mapped[1].Role);
        Assert.Contains("## Prior strategy", mapped[1].Content, StringComparison.Ordinal);
        Assert.Contains(priorStrategyMarkdown, mapped[1].Content, StringComparison.Ordinal);
        Assert.Contains("## Match outcome summary", mapped[1].Content, StringComparison.Ordinal);
        Assert.Contains(outcomeSummary, mapped[1].Content, StringComparison.Ordinal);
    }

    private static string BuildPlayTurnUserPrompt(IReadOnlyList<BonesPlayTurnOption> allowedMoves)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Play turn request");
        builder.AppendLine();
        builder.AppendLine("## Allowed moves");
        builder.AppendLine("Select one move id from this enumerated list:");

        foreach (var option in allowedMoves)
            builder.AppendLine($"- {option.MoveId.Value}: {option.Description}");

        return builder.ToString();
    }
}