using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Wip.Tools.Repo;
using Xunit;

namespace Wip.Runtime.Tests.Repo;

/// <summary>
/// WP-3.6 non-negotiable: the real DeepSeek HTTP adapter drives a write-capable repository agent.
/// The model server is a local fake at the HTTP boundary; the agent, tool invoker, executor and
/// worktree guard are the real implementations.
/// </summary>
public sealed class AgentRealModelHttpBoundaryE2ETests
{
    [Fact]
    public async Task RunAgentAsync_GivenFakeDeepSeekServer_ExpectedRepositoryAnswerScopedEditArtifactsAndNoMerge()
    {
        await using var server = new FakeDeepSeekServer();
        using var workspace = Workspace.Create();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "marker.txt"), "the needle is in this repository");

        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddRepoAgentTools();
        services.AddDeepSeekChatModel(new DeepSeekModelProviderOptions(
            baseUrl: server.BaseUrl,
            apiKey: "test-key",
            timeout: TimeSpan.FromSeconds(30)));
        var memory = new RecordingAgentMemory();
        services.AddSingleton<IAgentMemory>(memory);
        using var provider = services.BuildServiceProvider();

        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new NoOpPublisher());
        var session = await orchestrator.StartSessionAsync(
            new SessionStartRequest(
                WorkflowId: new WorkflowId("workflow.agent.repo"),
                TaskDescription: "find the needle and write a note",
                RepositoryPath: workspace.RootPath,
                WorktreePath: workspace.Path),
            CancellationToken.None);

        var result = await orchestrator.RunAgentAsync(
            new AgentExecutionRequest(
                session.SessionId,
                "Find the needle marker in the repository and write it to docs/agent-note.md.",
                "deepseek-chat"),
            builder,
            provider,
            CancellationToken.None);

        Assert.Equal(AgentExecutionStatus.Completed, result.Status);
        Assert.NotNull(result.FinalMessage);
        Assert.Contains("agent-note.md", result.FinalMessage!.Content, StringComparison.Ordinal);
        Assert.NotNull(result.Usage);
        // AgentExecutor records the accumulated token total in InputTokens and leaves OutputTokens 0.
        Assert.True(result.Usage!.InputTokens > 0);

        // Repository question answered through the real search tool.
        Assert.True(result.Steps.Count >= 2, $"Expected at least two tool turns, observed {result.Steps.Count}.");
        Assert.Contains(
            result.Steps[0].ToolResults,
            static toolResult => toolResult.Content.Contains("needle", StringComparison.Ordinal));

        // Scoped edit inside the isolated worktree only.
        var notePath = Path.Combine(workspace.Path, "docs", "agent-note.md");
        Assert.True(File.Exists(notePath), $"Expected the agent to write '{notePath}'.");
        Assert.Equal("agent-written", await File.ReadAllTextAsync(notePath));
        Assert.False(
            File.Exists(Path.Combine(workspace.RootPath, "docs", "agent-note.md")),
            "The edit must not escape the worktree.");

        // Governed artefacts and telemetry, and no automatic merge/approval.
        Assert.Contains(
            memory.Messages,
            static message => message.Role == ChatRole.User && message.Content.Contains("needle", StringComparison.Ordinal));
        Assert.Contains(
            memory.Messages,
            static message => message.Role == ChatRole.Assistant && message.Content.Contains("agent-note.md", StringComparison.Ordinal));
        Assert.True(
            File.Exists(Path.Combine(workspace.RootPath, ".wip", "sessions", session.SessionId.Value, "session-state.json")),
            "The session state telemetry file must be persisted.");
        Assert.Empty(Directory.GetFiles(session.ArtifactDirectory, "*merge*", SearchOption.AllDirectories));
        var finalSession = await orchestrator.GetSessionAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(finalSession);
        Assert.NotEqual(SessionState.Merged, finalSession!.State);
        Assert.NotEqual(SessionApprovalStatus.Approved, finalSession.ApprovalStatus);

        Assert.Equal(3, server.RequestCount);
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

    private sealed class FakeDeepSeekServer : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _loop;
        private int _requestCount;

        public FakeDeepSeekServer()
        {
            var port = GetFreePort();
            BaseUrl = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(BaseUrl.ToString());
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public Uri BaseUrl { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        private async Task AcceptLoopAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                string requestBody;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                Interlocked.Increment(ref _requestCount);
                var response = BuildResponse(requestBody);
                var payload = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
            catch
            {
                try
                {
                    context.Response.Abort();
                }
                catch
                {
                    // Best effort on the fake transport only.
                }
            }
        }

        private static string BuildResponse(string requestBody)
        {
            using var document = JsonDocument.Parse(requestBody);
            var toolMessageCount = document.RootElement
                .GetProperty("messages")
                .EnumerateArray()
                .Count(static message => message.GetProperty("role").GetString() == "tool");

            object payload = toolMessageCount switch
            {
                0 => ToolCallResponse(
                    "call-search",
                    "tool.repo.search",
                    """{"pattern":"needle","maxResults":10}"""),
                1 => ToolCallResponse(
                    "call-write",
                    "tool.repo.write_file",
                    """{"path":"docs/agent-note.md","content":"agent-written"}"""),
                _ => new
                {
                    model = "deepseek-chat",
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            message = new
                            {
                                role = "assistant",
                                content = "The repository contains the needle and docs/agent-note.md was written.",
                            },
                            finish_reason = "stop",
                        },
                    },
                    usage = new { prompt_tokens = 12, completion_tokens = 8 },
                },
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static object ToolCallResponse(string id, string name, string arguments)
            => new
            {
                model = "deepseek-chat",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new
                        {
                            role = "assistant",
                            content = (string?)null,
                            tool_calls = new[]
                            {
                                new
                                {
                                    id,
                                    type = "function",
                                    function = new { name, arguments },
                                },
                            },
                        },
                        finish_reason = "tool_calls",
                    },
                },
                usage = new { prompt_tokens = 10, completion_tokens = 6 },
            };

        private static int GetFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // Listener may already be closed.
            }

            try
            {
                await _loop;
            }
            catch
            {
                // Accept loop exits through cancellation.
            }

            _cancellation.Dispose();
        }
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
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wip-agent-http-{Guid.NewGuid():N}");
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
