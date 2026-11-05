using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Wip.Tools.Repo;
using Xunit;

namespace Wip.Runtime.Tests.Repo;

public sealed class AgentStreamingRuntimeTests
{
    [Fact]
    public async Task RunAgentStreamingAsync_GivenTextDeltas_ExpectedStreamedEventsAndMemoryAppended()
    {
        using var workspace = Workspace.Create();
        var memory = new RecordingAgentMemory();
        var model = new StreamingChatModel(
        [
            [new StreamingChunk("Hello"), new StreamingChunk(" world"), new StreamingChunk(string.Empty, isFinal: true)],
        ]);

        var (orchestrator, builder, provider, session) = await CreateRuntimeAsync(model, workspace, memory);

        var events = await CollectAsync(orchestrator.RunAgentStreamingAsync(
            new AgentExecutionRequest(session.SessionId, "Say hello", "deepseek-chat"),
            builder,
            provider,
            CancellationToken.None));

        Assert.Equal(
            ["Hello", " world"],
            events.Where(static streamEvent => streamEvent.Kind == AgentStreamEventKind.Delta)
                .Select(static streamEvent => streamEvent.Content));
        var completed = Assert.Single(events.Where(static streamEvent => streamEvent.Kind == AgentStreamEventKind.Completed));
        Assert.Equal(AgentExecutionStatus.Completed, completed.Result!.Status);
        Assert.Equal("Hello world", completed.Result.FinalMessage!.Content);

        Assert.Contains(memory.Messages, static message => message.Role == ChatRole.User && message.Content == "Say hello");
        Assert.Contains(memory.Messages, static message => message.Role == ChatRole.Assistant && message.Content == "Hello world");
    }

    [Fact]
    public async Task RunAgentStreamingAsync_GivenToolCallChunk_ExpectedToolResultBeforeFinalAnswer()
    {
        using var workspace = Workspace.Create();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "README.md"), "repository readme");

        var memory = new RecordingAgentMemory();
        var model = new StreamingChatModel(
        [
            [
                new StreamingChunk(
                    string.Empty,
                    toolCall: new ToolCall(
                        "call-1",
                        "tool.repo.read_file",
                        JsonSerializer.SerializeToElement(new { path = "README.md" }))),
                new StreamingChunk(string.Empty, isFinal: true),
            ],
            [new StreamingChunk("Done"), new StreamingChunk(string.Empty, isFinal: true)],
        ]);

        var (orchestrator, builder, provider, session) = await CreateRuntimeAsync(model, workspace, memory);

        var events = await CollectAsync(orchestrator.RunAgentStreamingAsync(
            new AgentExecutionRequest(session.SessionId, "Read the README", "deepseek-chat"),
            builder,
            provider,
            CancellationToken.None));

        var toolCall = Assert.Single(events.Where(static streamEvent => streamEvent.Kind == AgentStreamEventKind.ToolCall));
        Assert.Equal("tool.repo.read_file", toolCall.ToolCall!.Name);

        var toolResult = Assert.Single(events.Where(static streamEvent => streamEvent.Kind == AgentStreamEventKind.ToolResult));
        Assert.False(toolResult.ToolResult!.IsError);
        Assert.Contains("repository readme", toolResult.ToolResult.Content, StringComparison.Ordinal);

        var completed = Assert.Single(events.Where(static streamEvent => streamEvent.Kind == AgentStreamEventKind.Completed));
        Assert.Equal(AgentExecutionStatus.Completed, completed.Result!.Status);
        Assert.Equal("Done", completed.Result.FinalMessage!.Content);
    }

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var streamEvent in stream)
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static async Task<(WipRuntimeOrchestrator Orchestrator, WipBuilder Builder, ServiceProvider Provider, SessionSnapshot Session)>
        CreateRuntimeAsync(IChatModel model, Workspace workspace, IAgentMemory memory)
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddRepoAgentTools();
        services.AddSingleton(model);
        services.AddSingleton(memory);

        var provider = services.BuildServiceProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpPublisher());
        var session = await orchestrator.StartSessionAsync(
            new SessionStartRequest(
                WorkflowId: new WorkflowId("workflow.agent.repo"),
                TaskDescription: "streaming agent task",
                RepositoryPath: workspace.RootPath,
                WorktreePath: workspace.Path),
            CancellationToken.None);

        return (orchestrator, builder, provider, session);
    }

    private sealed class StreamingChatModel(params IReadOnlyList<StreamingChunk>[] turns) : IChatModel
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
            var chunks = turns[Math.Min(_index, turns.Length - 1)];
            _index++;

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }
    }

    private sealed class RecordingAgentMemory : IAgentMemory
    {
        public List<ChatMessage> Messages { get; } = [];

        public ValueTask<AgentMemorySnapshot> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AgentMemorySnapshot(Messages.ToArray()));

        public ValueTask AppendAsync(SessionId sessionId, ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> SummarizeAsync(SessionId sessionId, int maxMessages, CancellationToken cancellationToken)
            => ValueTask.FromResult(string.Join("\n", Messages.TakeLast(maxMessages).Select(static message => message.Content)));
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<SessionId, SessionSnapshot> _sessions = [];

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_sessions.TryGetValue(sessionId, out var snapshot) ? snapshot : null);
    }

    private sealed class Workspace : IDisposable
    {
        private Workspace(string rootPath, string path)
        {
            RootPath = rootPath;
            Path = path;
        }

        public string RootPath { get; }

        public string Path { get; }

        public static Workspace Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wip-agent-stream-{Guid.NewGuid():N}");
            var worktree = System.IO.Path.Combine(root, ".wip", "worktrees", "agent");
            Directory.CreateDirectory(worktree);
            return new Workspace(root, worktree);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
