using System.Collections.Concurrent;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Modus.Hosting;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;
using Xunit;

namespace Wip.Shell.Tests.Interactive;

public sealed class WipShellCommandLoopTests
{
    [Fact]
    public async Task ShellProcess_GivenStartup_RemainsInteractiveUntilExitCommand()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("help\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("wip> ", output, StringComparison.Ordinal);
        Assert.Contains("Available commands:", output, StringComparison.Ordinal);
        Assert.True(CountOccurrences(output, "wip> ") >= 2);
    }

    [Fact]
    public async Task PromptRenderer_GivenAttachedSession_ShowsSessionScopedPrompt()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var orchestrator = BuildOrchestrator();
            using var reader = new StringReader($"start workflow.linear {repositoryPath} {Path.Combine(repositoryPath, ".wip", "worktrees", "abc")}" + "\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("Session started:", output, StringComparison.Ordinal);
            Assert.Contains("wip[", output, StringComparison.Ordinal);
            Assert.Contains("]> ", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task CommandDispatcher_GivenSessionOnlyCommandWithoutActiveSession_ReturnsGuidanceForSessionStartOrAttach()
    {
        var orchestrator = BuildOrchestrator();
        using var reader = new StringReader("status\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("requires an active session", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start <workflow-id> <repository-path> <worktree-path>", output, StringComparison.Ordinal);
        Assert.Contains("attach <repository-path> <session-id>", output, StringComparison.Ordinal);
        Assert.True(CountOccurrences(output, "wip> ") >= 2);
    }

    [Fact]
    public async Task PluginsCommand_GivenLoadedDiagnostics_PrintsPluginsCapabilitiesAndPermissions()
    {
        var orchestrator = BuildOrchestrator();
        var bridge = new DiagnosticsBridge(new RunManifest(
            DateTimeOffset.UtcNow,
            [
                new PluginManifestEntry(
                    PluginId: "plugin.test",
                    PluginName: "TestPlugin",
                    PluginVersion: "1.2.3",
                    AssemblyName: "Plugin.Test",
                    AssemblyVersion: "1.2.3.0",
                    Capabilities: ["tool.exec", "validator.dotnet"],
                    RequiredPermissions: ["RegisterOperation", "SubscribeEvents"])
            ],
            Array.Empty<WorkflowManifestEntry>()),
            ["Failed to activate plugin type 'BrokenPlugin'."]);

        using var reader = new StringReader("plugins\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Loaded plugins:", output, StringComparison.Ordinal);
        Assert.Contains("plugin.test [TestPlugin] v1.2.3", output, StringComparison.Ordinal);
        Assert.Contains("capabilities: tool.exec, validator.dotnet", output, StringComparison.Ordinal);
        Assert.Contains("permissions: RegisterOperation, SubscribeEvents", output, StringComparison.Ordinal);
        Assert.Contains("Plugin diagnostics:", output, StringComparison.Ordinal);
        Assert.Contains("Failed to activate plugin type 'BrokenPlugin'.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowsCommand_GivenLoadedDiagnostics_PrintsRegisteredWorkflows()
    {
        var orchestrator = BuildOrchestrator();
        var bridge = new DiagnosticsBridge(new RunManifest(
            DateTimeOffset.UtcNow,
            Array.Empty<PluginManifestEntry>(),
            [
                new WorkflowManifestEntry(
                    WorkflowId: "workflow.safe-change",
                    DisplayName: "Safe Change",
                    RequestType: "Tests.WorkflowRequest",
                    ResultType: "Tests.WorkflowResult")
            ]),
            Array.Empty<string>());

        using var reader = new StringReader("workflows\nexit\n");
        using var writer = new StringWriter();
        var loop = new WipShellCommandLoop(orchestrator, reader, writer, bridge);

        var exitCode = await loop.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Registered workflows:", output, StringComparison.Ordinal);
        Assert.Contains("workflow.safe-change [Safe Change]", output, StringComparison.Ordinal);
        Assert.Contains("request=Tests.WorkflowRequest", output, StringComparison.Ordinal);
        Assert.Contains("result=Tests.WorkflowResult", output, StringComparison.Ordinal);
    }

    private static WipRuntimeOrchestrator BuildOrchestrator()
        => new(new InMemorySessionStore(), new NoOpSessionEventPublisher());

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var snapshot))
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class NoOpSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class DiagnosticsBridge : IModusWipBridge
    {
        private readonly RunManifest _runManifest;
        private readonly IReadOnlyList<string> _diagnostics;

        public DiagnosticsBridge(RunManifest runManifest, IReadOnlyList<string> diagnostics)
        {
            _runManifest = runManifest;
            _diagnostics = diagnostics;
        }

        public ValueTask<int> LoadPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(_runManifest.Plugins.Count);

        public ValueTask StopPluginsAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public RunManifest GetRunManifest()
            => _runManifest;

        public IReadOnlyList<string> GetLoadDiagnostics()
            => _diagnostics;
    }
}
