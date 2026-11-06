using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Runtime;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Runtime;

public sealed class BonesChatModelPlayTurnProviderTests
{
    private static readonly CapabilityContext Context = new(new SessionId("session-play-turn"), "worktree");
    private static readonly BonesPlayerId ActiveSeat = new(2);
    private static readonly BonesStrategyId StrategyId = new("seat-2-v1");

    private static readonly BonesPlayTurnOption[] AllowedMoves =
    [
        new(new BonesMoveId("move-play-left"), "play 3-4 on Left (move id: move-play-left)"),
        new(new BonesMoveId("move-pass"), "pass (move id: move-pass)"),
    ];

    [Fact]
    public async Task ExecuteAsync_GivenModelAnswerContainingLegalMove_ExpectedParsesMoveId()
    {
        var chatModel = new ScriptedChatModel("move-play-left");
        var provider = new BonesChatModelPlayTurnProvider(chatModel);

        var response = await provider.ExecuteAsync(
            CreateRequest("bones-play-turn"),
            Context,
            CancellationToken.None);

        Assert.Equal("move-play-left", response.Payload.SelectedMoveId);
        Assert.Equal("scripted", response.ProviderId);
        Assert.Equal("bones-play-turn", response.ModelId);
        Assert.Equal("correlation-play", response.CorrelationId);
    }

    [Fact]
    public async Task ExecuteAsync_GivenRequestMessages_ExpectedForwardsRolesAndContentInOrder()
    {
        var chatModel = new ScriptedChatModel("move-pass");
        var provider = new BonesChatModelPlayTurnProvider(chatModel);

        await provider.ExecuteAsync(CreateRequest("deepseek-flash"), Context, CancellationToken.None);

        var forwarded = Assert.IsType<ChatRequest>(chatModel.LastRequest);
        Assert.Equal("deepseek-flash", forwarded.ModelId);
        Assert.Equal(2, forwarded.Messages.Count);
        Assert.Equal(ChatRole.System, forwarded.Messages[0].Role);
        Assert.Equal("You are seat 2.", forwarded.Messages[0].Content);
        Assert.Equal(ChatRole.User, forwarded.Messages[1].Role);
        Assert.Equal("# Play turn request", forwarded.Messages[1].Content);
        Assert.Empty(forwarded.Tools);
    }

    [Fact]
    public async Task ExecuteAsync_GivenIllegalModelAnswer_ExpectedEmptySelectedMoveId()
    {
        var chatModel = new ScriptedChatModel("move-illegal");
        var provider = new BonesChatModelPlayTurnProvider(chatModel);

        var response = await provider.ExecuteAsync(
            CreateRequest("bones-play-turn"),
            Context,
            CancellationToken.None);

        Assert.Equal(string.Empty, response.Payload.SelectedMoveId);
    }

    [Fact]
    public async Task ExecuteAsync_GivenJsonMoveIdAnswer_ExpectedParsesMoveId()
    {
        var chatModel = new ScriptedChatModel("{\"moveId\":\"move-pass\"}");
        var provider = new BonesChatModelPlayTurnProvider(chatModel);

        var response = await provider.ExecuteAsync(
            CreateRequest("bones-play-turn"),
            Context,
            CancellationToken.None);

        Assert.Equal("move-pass", response.Payload.SelectedMoveId);
    }

    [Fact]
    public void Constructor_GivenNullChatModel_ExpectedThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new BonesChatModelPlayTurnProvider(null!));

        Assert.Equal("chatModel", exception.ParamName);
    }

    private static ModelProviderRequest<BonesPlayTurnRequest> CreateRequest(string modelId)
        => new(
            payload: new BonesPlayTurnRequest(
                Messages:
                [
                    new BonesPlayTurnMessage("system", "You are seat 2."),
                    new BonesPlayTurnMessage("user", "# Play turn request"),
                ],
                AllowedMoves: AllowedMoves,
                ActiveSeat: ActiveSeat,
                StrategyId: StrategyId),
            modelId: modelId,
            correlationId: "correlation-play");

    private sealed class ScriptedChatModel(string answer) : IChatModel
    {
        public ChatRequest? LastRequest { get; private set; }

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return ValueTask.FromResult(new ChatResponse(
                ChatMessage.Assistant(answer),
                request.ModelId,
                "scripted",
                new ModelProviderUsage(10, 5),
                request.CorrelationId));
        }

        public async IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            await Task.CompletedTask;
            yield return new StreamingChunk(answer, isFinal: true);
        }
    }
}