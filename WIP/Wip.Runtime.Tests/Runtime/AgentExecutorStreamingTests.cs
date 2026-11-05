using System.Runtime.CompilerServices;
using System.Text.Json;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Tools;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class AgentExecutorStreamingTests
{
    [Fact]
    public async Task ExecuteStreamingAsync_GivenTextDeltas_ExpectedDeltasThenCompleted()
    {
        var model = new ScriptedStreamingChatModel([new StreamingChunk("Hel"), new StreamingChunk("lo", isFinal: true)]);
        var executor = new AgentExecutor(model, new RecordingToolInvoker());

        var events = await CollectAsync(executor.ExecuteStreamingAsync(CreateRequest(), CreateContext(), CancellationToken.None));

        Assert.Equal(
            [AgentStreamEventKind.Delta, AgentStreamEventKind.Delta, AgentStreamEventKind.Completed],
            events.Select(static item => item.Kind).ToArray());
        Assert.Equal("Hel", events[0].Content);
        Assert.Equal("Hello", events[^1].Result!.FinalMessage!.Content);
        Assert.True(events[^1].Result!.Succeeded);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_GivenToolCallStream_ExpectedToolResultThenCompleted()
    {
        var call = new ToolCall(
            "call-1",
            "tool.echo",
            JsonSerializer.SerializeToElement(new { text = "hi" }));
        var model = new ScriptedStreamingChatModel(
            [new StreamingChunk(string.Empty, toolCall: call)],
            [new StreamingChunk("done", isFinal: true)]);
        var invoker = new RecordingToolInvoker(ToolResult.Success("call-1", "echo:hi"));
        var executor = new AgentExecutor(model, invoker);

        var events = await CollectAsync(executor.ExecuteStreamingAsync(CreateRequest(), CreateContext(), CancellationToken.None));

        Assert.Equal(
            [AgentStreamEventKind.ToolCall, AgentStreamEventKind.ToolResult, AgentStreamEventKind.Delta, AgentStreamEventKind.Completed],
            events.Select(static item => item.Kind).ToArray());
        Assert.Equal("call-1", events[0].ToolCall!.Id);
        Assert.Equal("echo:hi", events[1].ToolResult!.Content);
        Assert.Equal(1, invoker.CallCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_GivenModelError_ExpectedFailedEvent()
    {
        var executor = new AgentExecutor(new ThrowingStreamingChatModel(), new RecordingToolInvoker());

        var events = await CollectAsync(executor.ExecuteStreamingAsync(CreateRequest(), CreateContext(), CancellationToken.None));

        var failed = Assert.Single(events);
        Assert.Equal(AgentStreamEventKind.Failed, failed.Kind);
        Assert.Contains("model-error", failed.Result!.Error, StringComparison.Ordinal);
    }

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var item in stream)
        {
            events.Add(item);
        }

        return events;
    }

    private static AgentExecutionRequest CreateRequest()
        => new(new SessionId("session-streaming"), "stream", "model.test");

    private static CapabilityContext CreateContext()
        => new(new SessionId("session-streaming"), WorktreePath: Path.GetTempPath());

    private sealed class ScriptedStreamingChatModel(params IReadOnlyList<StreamingChunk>[] turns) : IChatModel
    {
        private int _index;

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var turn = turns[Math.Min(_index, turns.Length - 1)];
            _index++;
            foreach (var chunk in turn)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class ThrowingStreamingChatModel : IChatModel
    {
        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("provider down");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
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
}