using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Tools.Shell.Shell;
using Xunit;

namespace Wip.Tools.Shell.Tests.Shell;

public sealed class ShellCommandToolTests : IAsyncLifetime
{
    private string _worktreePath = string.Empty;

    public Task InitializeAsync()
    {
        _worktreePath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_worktreePath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_worktreePath))
                Directory.Delete(_worktreePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_GivenOutsideWorktreePath_BlocksExecutionAndWritesArtifactLog()
    {
        var artifactStore = new InMemoryArtifactStore();
        var tool = new ShellCommandTool(new AllowAllPolicy(), artifactStore);
        var context = new CapabilityContext(new SessionId("session-outside"), _worktreePath);

        var result = await tool.ExecuteAsync(
            new ShellCommandRequest(
                Command: EchoCommand("never-runs"),
                WorkflowId: new WorkflowId("wf-shell"),
                RelativeWorkingDirectory: ".."),
            context,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains("outside the active worktree", result.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1, result.ExitCode);
        Assert.Single(artifactStore.Descriptors);
        Assert.Contains("\"IsBlocked\":true", artifactStore.Payloads.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GivenDangerousCommandPolicyDenial_ReturnsBlockedResultWithReason()
    {
        var artifactStore = new InMemoryArtifactStore();
        var tool = new ShellCommandTool(new DenyDeletePolicy(), artifactStore);
        var context = new CapabilityContext(new SessionId("session-policy"), _worktreePath);

        var result = await tool.ExecuteAsync(
            new ShellCommandRequest(
                Command: "rm -rf .",
                WorkflowId: new WorkflowId("wf-shell")),
            context,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Equal("Dangerous command denied by policy.", result.BlockReason);
        Assert.Equal(-1, result.ExitCode);
        Assert.Single(artifactStore.Descriptors);
    }

    [Fact]
    public async Task ExecuteAsync_GivenAllowedCommand_CapturesStdoutStderrExitCodeAndWritesCommandLogArtifact()
    {
        var artifactStore = new InMemoryArtifactStore();
        var tool = new ShellCommandTool(new AllowAllPolicy(), artifactStore);
        var context = new CapabilityContext(new SessionId("session-allowed"), _worktreePath);

        var result = await tool.ExecuteAsync(
            new ShellCommandRequest(
                Command: SuccessWithStdErrCommand("hello-shell", "warn-shell"),
                WorkflowId: new WorkflowId("wf-shell"),
                Timeout: TimeSpan.FromSeconds(5)),
            context,
            CancellationToken.None);

        Assert.False(result.IsBlocked);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-shell", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warn-shell", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Single(artifactStore.Descriptors);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.Equal("hello-shell", payload.GetProperty("StandardOutput").GetString()?.Trim());
        Assert.Equal("warn-shell", payload.GetProperty("StandardError").GetString()?.Trim());
        Assert.False(payload.GetProperty("TimedOut").GetBoolean());
        Assert.Equal(0, payload.GetProperty("ExitCode").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_GivenCommandExceedsTimeout_ReturnsTimedOutResultAndPersistsEvidence()
    {
        var artifactStore = new InMemoryArtifactStore();
        var tool = new ShellCommandTool(new AllowAllPolicy(), artifactStore);
        var context = new CapabilityContext(new SessionId("session-timeout"), _worktreePath);

        var result = await tool.ExecuteAsync(
            new ShellCommandRequest(
                Command: LongRunningCommand(),
                WorkflowId: new WorkflowId("wf-shell"),
                Timeout: TimeSpan.FromMilliseconds(200)),
            context,
            CancellationToken.None);

        Assert.False(result.IsBlocked);
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.True(payload.GetProperty("TimedOut").GetBoolean());
        Assert.Equal(-1, payload.GetProperty("ExitCode").GetInt32());
    }

    private static string EchoCommand(string text)
        => OperatingSystem.IsWindows() ? $"echo {text}" : $"echo {EscapeSingleQuotes(text)}";

    private static string SuccessWithStdErrCommand(string stdOut, string stdErr)
        => OperatingSystem.IsWindows()
            ? $"echo {stdOut} && echo {stdErr} 1>&2"
            : $"echo '{EscapeSingleQuotes(stdOut)}'; echo '{EscapeSingleQuotes(stdErr)}' 1>&2";

    private static string LongRunningCommand()
        => OperatingSystem.IsWindows() ? "ping 127.0.0.1 -n 6 >nul" : "sleep 5";

    private static string EscapeSingleQuotes(string value)
        => value.Replace("'", "'\\''", StringComparison.Ordinal);

    private sealed class AllowAllPolicy : IPolicy<ShellCommandPolicyRequest>
    {
        public PolicyId PolicyId => new("local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellCommandPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed class DenyDeletePolicy : IPolicy<ShellCommandPolicyRequest>
    {
        public PolicyId PolicyId => new("local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellCommandPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                request.Command.Contains("rm -rf", StringComparison.OrdinalIgnoreCase)
                    ? PolicyDecision.Deny("Dangerous command denied by policy.")
                    : PolicyDecision.Allow());
    }

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        private readonly List<ArtifactDescriptor> _descriptors = [];
        private readonly List<string> _payloads = [];

        public IReadOnlyList<ArtifactDescriptor> Descriptors => _descriptors;

        public IReadOnlyList<string> Payloads => _payloads;

        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            _payloads.Add(artifact.Content);

            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                relativePath: $".wip/artifacts/{sessionId.Value}/{artifact.FileName}.json",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            _descriptors.Add(descriptor);
            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(
                _descriptors.Where(x => x.SessionId.Equals(sessionId)).ToArray());
    }
}
