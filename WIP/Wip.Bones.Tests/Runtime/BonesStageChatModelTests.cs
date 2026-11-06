using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Runtime;

public sealed class BonesStageChatModelTests
{
    private const string ConfiguredModel = "deepseek-flash";
    private const string StageModelId = "bones-strategy-author";

    private static readonly CapabilityContext Context = new(new SessionId("session-stage-model"), "worktree");

    [Fact]
    public async Task CompleteAsync_GivenUnsupportedStageModelId_ExpectedRewritesToConfiguredModel()
    {
        var inner = new CapturingChatModel();
        var model = new BonesStageChatModel(inner, ConfiguredModel);

        await model.CompleteAsync(CreateRequest(StageModelId), Context, CancellationToken.None);

        var forwarded = Assert.IsType<ChatRequest>(inner.CompleteRequest);
        Assert.Equal(ConfiguredModel, forwarded.ModelId);
    }

    [Fact]
    public async Task StreamAsync_GivenUnsupportedStageModelId_ExpectedRewritesToConfiguredModel()
    {
        var inner = new CapturingChatModel();
        var model = new BonesStageChatModel(inner, ConfiguredModel);

        await foreach (var _ in model.StreamAsync(CreateRequest(StageModelId), Context, CancellationToken.None))
        {
        }

        var forwarded = Assert.IsType<ChatRequest>(inner.StreamRequest);
        Assert.Equal(ConfiguredModel, forwarded.ModelId);
    }

    [Theory]
    [InlineData("deepseek-flash")]
    [InlineData("deepseek-v4-pro")]
    [InlineData("deepseek-v4-flash")]
    public async Task CompleteAsync_GivenSupportedModelId_ExpectedPreservesModelId(string supportedModelId)
    {
        var inner = new CapturingChatModel();
        var model = new BonesStageChatModel(inner, ConfiguredModel);

        await model.CompleteAsync(CreateRequest(supportedModelId), Context, CancellationToken.None);

        var forwarded = Assert.IsType<ChatRequest>(inner.CompleteRequest);
        Assert.Equal(supportedModelId, forwarded.ModelId);
    }

    [Fact]
    public async Task StreamAsync_GivenSupportedModelId_ExpectedPreservesModelId()
    {
        var inner = new CapturingChatModel();
        var model = new BonesStageChatModel(inner, ConfiguredModel);

        await foreach (var _ in model.StreamAsync(CreateRequest("deepseek-v4-pro"), Context, CancellationToken.None))
        {
        }

        var forwarded = Assert.IsType<ChatRequest>(inner.StreamRequest);
        Assert.Equal("deepseek-v4-pro", forwarded.ModelId);
    }

    [Fact]
    public async Task CompleteAsync_GivenRewrittenRequest_ExpectedMessagesToolsAndOptionsUnchanged()
    {
        var inner = new CapturingChatModel();
        var model = new BonesStageChatModel(inner, ConfiguredModel);
        var request = CreateRequest(StageModelId);
        var context = Context;
        using var cancellation = new CancellationTokenSource();

        await model.CompleteAsync(request, context, cancellation.Token);

        var forwarded = Assert.IsType<ChatRequest>(inner.CompleteRequest);
        Assert.Equal(2, forwarded.Messages.Count);
        Assert.Equal(ChatRole.System, forwarded.Messages[0].Role);
        Assert.Equal("system prompt", forwarded.Messages[0].Content);
        Assert.Equal(ChatRole.User, forwarded.Messages[1].Role);
        Assert.Equal("user prompt", forwarded.Messages[1].Content);

        var tool = Assert.Single(forwarded.Tools);
        Assert.Equal("tool.bones.strategy-compile", tool.Name);
        Assert.Equal("Compiles a strategy.", tool.Description);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
        Assert.Equal("scriptSource", tool.ParametersSchema.GetProperty("properties").EnumerateObject().Single().Name);

        Assert.Equal(512, forwarded.MaxTokens);
        Assert.Equal(0.25, forwarded.Temperature);
        Assert.Equal("correlation-7", forwarded.CorrelationId);

        Assert.Same(context, inner.ReceivedContext);
        Assert.Equal(cancellation.Token, inner.ReceivedCancellationToken);
    }

    [Fact]
    public void Constructor_GivenNullInnerModel_ExpectedThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new BonesStageChatModel(null!, ConfiguredModel));

        Assert.Equal("inner", exception.ParamName);
    }

    private static ChatRequest CreateRequest(string modelId)
        => new(
            modelId: modelId,
            messages: [ChatMessage.System("system prompt"), ChatMessage.User("user prompt")],
            tools:
            [
                new ToolDefinition(
                    "tool.bones.strategy-compile",
                    "Compiles a strategy.",
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        properties = new { scriptSource = new { type = "string" } },
                    })),
            ],
            maxTokens: 512,
            temperature: 0.25,
            correlationId: "correlation-7");

    private sealed class CapturingChatModel : IChatModel
    {
        public ChatRequest? CompleteRequest { get; private set; }

        public ChatRequest? StreamRequest { get; private set; }

        public CapabilityContext? ReceivedContext { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CompleteRequest = request;
            ReceivedContext = context;
            ReceivedCancellationToken = cancellationToken;
            return ValueTask.FromResult(CreateResponse(request));
        }

        public async IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamRequest = request;
            ReceivedContext = context;
            ReceivedCancellationToken = cancellationToken;
            yield return new StreamingChunk("answer", isFinal: true);
            await Task.CompletedTask;
        }

        private static ChatResponse CreateResponse(ChatRequest request)
            => new(ChatMessage.Assistant("answer"), request.ModelId, "capturing");
    }
}