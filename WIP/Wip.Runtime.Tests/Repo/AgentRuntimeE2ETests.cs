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

public sealed class AgentRuntimeE2ETests
{
    [Fact]
    public async Task RunAgentAsync_GivenWriteCapableRepoAgent_ExpectedScopedEditInsideWorktree()
    {
        using var workspace = Workspace.Create();
        var writeCall = new ToolCall(
            "call-write",
            "tool.repo.write_file",
            JsonSerializer.SerializeToElement(new { path = "docs/agent-note.md", content = "written by the agent" }));
        var model = new ScriptedChatModel(
            new ChatResponse(
                ChatMessage.Assistant(string.Empty, [writeCall]),
                "model.test",
                "test"),
            new ChatResponse(ChatMessage.Assistant("done"), "model.test", "test"));

        var (orchestrator, builder, provider, session) = await CreateRuntimeAsync(model, workspace);
        await using var _ = provider;

        var result = await orchestrator.RunAgentAsync(
            new AgentExecutionRequest(session.SessionId, "write a note", "model.test"),
            builder,
            provider,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("written by the agent", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "docs", "agent-note.md")));
    }

    [Fact]
    public async Task RunAgentAsync_GivenTraversalWrite_ExpectedBlockedAndNoEscapeFile()
    {
        using var workspace = Workspace.Create();
        var escapeCall = new ToolCall(
            "call-escape",
            "tool.repo.write_file",
            JsonSerializer.SerializeToElement(new { path = "../escape.txt", content = "nope" }));
        var model = new ScriptedChatModel(
            new ChatResponse(ChatMessage.Assistant(string.Empty, [escapeCall]), "model.test", "test"),
            new ChatResponse(ChatMessage.Assistant("blocked"), "model.test", "test"));

        var (orchestrator, builder, provider, session) = await CreateRuntimeAsync(model, workspace);
        await using var _ = provider;

        var result = await orchestrator.RunAgentAsync(
            new AgentExecutionRequest(session.SessionId, "escape", "model.test"),
            builder,
            provider,
            CancellationToken.None);

        var step = result.Steps[0];
        var observation = Assert.Single(step.ToolResults);
        Assert.True(observation.IsError);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "..", "escape.txt")));
        Assert.False(File.Exists(Path.Combine(workspace.RootPath, "escape.txt")));
    }

    [Fact]
    public async Task RunAgentAsync_GivenDeleteGitConfigCall_ExpectedBlockedAndFileIntact()
    {
        using var workspace = Workspace.Create();
        var gitDirectory = Path.Combine(workspace.Path, ".git");
        Directory.CreateDirectory(gitDirectory);
        var configPath = Path.Combine(gitDirectory, "config");
        await File.WriteAllTextAsync(configPath, "sensitive");

        var deleteCall = new ToolCall(
            "call-delete",
            "tool.repo.delete_file",
            JsonSerializer.SerializeToElement(new { path = ".git/config" }));
        var model = new ScriptedChatModel(
            new ChatResponse(ChatMessage.Assistant(string.Empty, [deleteCall]), "model.test", "test"),
            new ChatResponse(ChatMessage.Assistant("blocked"), "model.test", "test"));

        var (orchestrator, builder, provider, session) = await CreateRuntimeAsync(model, workspace);
        await using var _ = provider;

        var result = await orchestrator.RunAgentAsync(
            new AgentExecutionRequest(session.SessionId, "delete git config", "model.test"),
            builder,
            provider,
            CancellationToken.None);

        var observation = Assert.Single(result.Steps[0].ToolResults);
        Assert.True(observation.IsError);
        Assert.True(File.Exists(configPath));
        Assert.Equal("sensitive", await File.ReadAllTextAsync(configPath));
    }

    [Fact]
    public async Task RunAgentAsync_GivenAbsoluteWriteCall_ExpectedBlockedAndNothingWritten()
    {
        using var workspace = Workspace.Create();
        var outside = Path.Combine(workspace.RootPath, "outside.txt");
        var writeCall = new ToolCall(
            "call-absolute",
            "tool.repo.write_file",
            JsonSerializer.SerializeToElement(new { path = outside, content = "escape" }));
        var model = new ScriptedChatModel(
            new ChatResponse(ChatMessage.Assistant(string.Empty, [writeCall]), "model.test", "test"),
            new ChatResponse(ChatMessage.Assistant("blocked"), "model.test", "test"));

        var (orchestrator, builder, provider, session) = await CreateRuntimeAsync(model, workspace);
        await using var _ = provider;

        var result = await orchestrator.RunAgentAsync(
            new AgentExecutionRequest(session.SessionId, "absolute write", "model.test"),
            builder,
            provider,
            CancellationToken.None);

        Assert.True(Assert.Single(result.Steps[0].ToolResults).IsError);
        Assert.False(File.Exists(outside));
    }
    private static async Task<(WipRuntimeOrchestrator Orchestrator, WipBuilder Builder, ServiceProvider Provider, SessionSnapshot Session)>
        CreateRuntimeAsync(IChatModel model, Workspace workspace)
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddRepoAgentTools();
        services.AddSingleton(model);

        var provider = services.BuildServiceProvider();
        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpPublisher());
        var session = await orchestrator.StartSessionAsync(
            new SessionStartRequest(
                WorkflowId: new WorkflowId("workflow.agent.repo"),
                TaskDescription: "agent repo task",
                RepositoryPath: workspace.RootPath,
                WorktreePath: workspace.Path),
            CancellationToken.None);

        return (orchestrator, builder, provider, session);
    }

    private sealed class ScriptedChatModel(params ChatResponse[] responses) : IChatModel
    {
        private int _index;

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            var response = responses[Math.Min(_index, responses.Length - 1)];
            _index++;
            return ValueTask.FromResult(response);
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
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
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wip-agent-e2e-{Guid.NewGuid():N}");
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

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}