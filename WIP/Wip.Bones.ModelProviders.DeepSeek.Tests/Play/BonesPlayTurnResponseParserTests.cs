using Wip.Bones.Agents.Play;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Play;

public sealed class BonesPlayTurnResponseParserTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.PlayTurnResponseParser;

    private static readonly BonesMoveId MovePlayLeft = new("move-play-left");
    private static readonly BonesMoveId MovePass = new("move-pass");

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPlayTurnResponseParser_GivenExactMoveIdLine_ExpectedReturnsMatchingAllowedMoveId()
    {
        var allowedMoves = CreateAllowedMoves(includePass: true);

        var parsed = BonesPlayTurnResponseParser.Parse("move-play-left", allowedMoves);

        Assert.Equal("move-play-left", parsed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPlayTurnResponseParser_GivenJsonMoveIdField_ExpectedReturnsMatchingAllowedMoveId()
    {
        var allowedMoves = CreateAllowedMoves(includePass: true);

        var parsed = BonesPlayTurnResponseParser.Parse("{\"moveId\":\"move-play-left\"}", allowedMoves);

        Assert.Equal("move-play-left", parsed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPlayTurnResponseParser_GivenUnknownMoveId_ExpectedReturnsNullForRetry()
    {
        var allowedMoves = CreateAllowedMoves(includePass: true);

        var parsed = BonesPlayTurnResponseParser.Parse("move-illegal", allowedMoves);

        Assert.Null(parsed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesPlayTurnResponseParser_GivenPassOnlyLegalSet_ExpectedAcceptsPassToken()
    {
        var allowedMoves = new[]
        {
            new BonesPlayTurnOption(MovePass, "pass (move id: move-pass)"),
        };

        var parsed = BonesPlayTurnResponseParser.Parse("pass", allowedMoves);

        Assert.Equal("move-pass", parsed);
    }

    private static BonesPlayTurnOption[] CreateAllowedMoves(bool includePass)
    {
        var moves = new List<BonesPlayTurnOption>
        {
            new(MovePlayLeft, "play 3-5 on Left (move id: move-play-left)"),
        };

        if (includePass)
            moves.Add(new BonesPlayTurnOption(MovePass, "pass (move id: move-pass)"));

        return moves.ToArray();
    }
}