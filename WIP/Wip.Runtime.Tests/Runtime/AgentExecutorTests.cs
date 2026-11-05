using System.Text.Json;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Tools;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class AgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_GivenImmediateFinalAnswer_ExpectedCompletedWithOneStep()
    {
        var model = new ScriptedChatModel(Response(Assistant("done")));
        var executor = new AgentExecutor(model, new RecordingToolInvoker());
        var request = CreateRequest();

        var result = await executor.ExecuteAsync(request, CreateContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Steps);
        Assert.Equal("done", result.FinalMessage!.Content);
        Assert.Equal(1, model.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenToolCallThenFinalAnswer_ExpectedObservationIsFedBack()
    {
        var toolCall = CreateToolCall("call-1", "tool.echo", new { text = "hi" });
        var model = new ScriptedChatModel(
            Response(Assistant(string.Empty, [toolCall])),
            Response(Assistant("final")));
        var invoker = new RecordingToolInvoker(ToolResult.Success("call-1", "echo:hi"));
        var executor = new AgentExecutor(model, invoker);

        var result = await executor.ExecuteAsync(CreateRequest(), CreateContext(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(1, invoker.CallCount);
        var secondRequest = model.Requests[1];
        Assert.Contains(secondRequest.Messages, static message => message.Role == ChatRole.Tool && message.ToolCallId == "call-1");
    }

    [Fact]
    public async Task ExecuteAsync_GivenAlwaysToolCalls_ExpectedMaxStepsStop()
    {
        var call = CreateToolCall("call-1", "tool.echo", new { text = "x" });
        var model = new RepeatingChatModel(Response(Assistant(string.Empty, [call])));
        var executor = new AgentExecutor(model, new RecordingToolInvoker(ToolResult.Success("call-1", "ok")));
        var request = CreateRequest(options: new AgentLoopOptions { MaxSteps = 2 });

        var result = await executor.ExecuteAsync(request, CreateContext(), CancellationToken.None);

        Assert.Equal(AgentExecutionStatus.MaxStepsReached, result.Status);
        Assert.Equal(2, model.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_GivenRepeatedToolFailures_ExpectedToolFailureLimit()
    {
        var call = CreateToolCall("call-1", "tool.echo", new { text = "x" });
        var model = new RepeatingChatModel(Response(Assistant(string.Empty, [call])));
        var executor = new AgentExecutor(model, new RecordingToolInvoker(ToolResult.Error("call-1", "boom")));
        var request = CreateRequest(options: new AgentLoopOptions { MaxSteps = 5, MaxConsecutiveToolFailures = 2 });

        var result = await executor.ExecuteAsync(request, CreateContext(), CancellationToken.None);

        Assert.Equal(AgentExecutionStatus.ToolFailureLimitReached, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_GivenTokenBudget_ExpectedTokenStop()
    {
        var call = CreateToolCall("call-1", "tool.echo", new { text = "x" });
        var model = new RepeatingChatModel(
            Response(Assistant(string.Empty, [call]), new ModelProviderUsage(10, 10)));
        var executor = new AgentExecutor(model, new RecordingToolInvoker(ToolResult.Success("call-1", "ok")));
        var request = CreateRequest(options: new AgentLoopOptions { MaxSteps = 3, MaxTotalTokens = 15 });

        var result = await executor.ExecuteAsync(request, CreateContext(), CancellationToken.None);

        Assert.Equal(AgentExecutionStatus.TokenBudgetExhausted, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_GivenModelError_ExpectedFailedResult()
    {
        var executor = new AgentExecutor(new ThrowingChatModel(), new RecordingToolInvoker());

        var result = await executor.ExecuteAsync(CreateRequest(), CreateContext(), CancellationToken.None);

        Assert.Equal(AgentExecutionStatus.Failed, result.Status);
        Assert.Contains("model-error", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GivenCallerCancellation_ExpectedOperationCanceled()
    {
        var model = new CancellingChatModel();
        var executor = new AgentExecutor(model, new RecordingToolInvoker());
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(CreateRequest(), CreateContext(), cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);
    }

    private static AgentExecutionRequest CreateRequest(AgentLoopOptions? options = null)
        => new(
            new SessionId("session-agent-executor"),
            "answer the question",
            modelId: "model.test",
            systemPrompt: "system",
            options: options);

    private static CapabilityContext CreateContext()
        => new(new SessionId("session-agent-executor"), WorktreePath: Path.GetTempPath());

    private static ChatMessage Assistant(
        string content,
        IReadOnlyList<ToolCall>? toolCalls = null,
        ModelProviderUsage? usage = null)
        => ChatMessage.Assistant(content, toolCalls);

    private static ToolCall CreateToolCall(string id, string name, object arguments)
        => new(id, name, JsonSerializer.SerializeToElement(arguments));

    private sealed class ScriptedChatModel(params ChatResponse[] responses) : IChatModel
    {
        public int CallCount { get; private set; }

        public List<ChatRequest> Requests { get; } = [];

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = responses[Math.Min(CallCount, responses.Length - 1)];
            CallCount++;
            return ValueTask.FromResult(response);
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RepeatingChatModel(ChatResponse response) : IChatModel
    {
        public int CallCount { get; private set; }

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(response);
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingChatModel : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("provider down");

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CancellingChatModel : IChatModel
    {
        public async ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new ChatResponse(ChatMessage.Assistant("late"), "model.test", "test");
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingToolInvoker(ToolResult? result = null) : IToolInvoker
    {
        public int CallCount { get; private set; }

        public ValueTask<ToolResult> InvokeAsync(
            ToolCall call,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(result ?? ToolResult.Success(call.Id, "ok"));
        }
    }

    private static ChatResponse Response(ChatMessage message, ModelProviderUsage? usage = null)
        => new(message, "model.test", "test", usage);
}