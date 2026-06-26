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
        var tool = new ShellCommandTool(new DenyOutsideWorktreePolicy(), artifactStore);
        var context = new CapabilityContext(new SessionId("session-outside"), _worktreePath);

        var result = await tool.ExecuteAsync(
            new ShellCommandRequest(
                Command: EchoCommand("never-runs"),
                WorkflowId: new WorkflowId("wf-shell"),
                RelativeWorkingDirectory: ".."),
            context,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains("outside the active worktree boundary", result.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1, result.ExitCode);
        Assert.Single(artifactStore.Descriptors);
        Assert.Contains("\"IsBlocked\":true", artifactStore.Payloads.Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rm -rf .")]
    [InlineData("del /f /q *.dll")]
    [InlineData("erase /f /q *.log")]
    [InlineData("rmdir /s /q temp")]
    [InlineData("rd /s /q cache")]
    [InlineData("Remove-Item -Recurse -Force .\\bin")]
    [InlineData("git push origin main")]
    [InlineData("git clean -fdx")]
    [InlineData("echo hacked > .git/config")]
    [InlineData("git reset --hard HEAD~1")]
    [InlineData("mkfs.ext4 /dev/sda1")]
    [InlineData("format c:")]
    [InlineData("dd if=/dev/zero of=/dev/sda")]
    [InlineData("wipefs -a /dev/sda")]
    [InlineData("diskpart /s wipe.txt")]
    [InlineData(":(){:|:&};:")]
    [InlineData("shutdown /s /t 0")]
    [InlineData("reboot")]
    [InlineData("halt")]
    [InlineData("poweroff")]
    [InlineData("init 0")]
    [InlineData("init 6")]
    public async Task ExecuteAsync_GivenMvpDangerousCommand_BlocksPreExecutionWithDeterministicReason(string command)
    {
        var artifactStore = new InMemoryArtifactStore();
        var policy = new CountingAllowAllPolicy();
        var tool = new ShellCommandTool(policy, artifactStore);
        var context = new CapabilityContext(new SessionId("session-policy"), _worktreePath);

        var result = await tool.ExecuteAsync(
            new ShellCommandRequest(
                Command: command,
                WorkflowId: new WorkflowId("wf-shell")),
            context,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.True(DangerousCommandDenylist.TryMatch(command, out var expectedMatch));
        Assert.NotNull(result.BlockReason);

        var reasonPayload = AssertPolicyViolationPayload(result.BlockReason!);
        Assert.Equal(command, reasonPayload.BlockedAction);
        Assert.Equal("local-safe", reasonPayload.BlockingPolicy);
        Assert.Contains("Remove dangerous command tokens", reasonPayload.NextStepGuidance, StringComparison.Ordinal);
        Assert.Equal(DangerousCommandDenylist.BuildToolBlockedReason(expectedMatch), reasonPayload.Reason);

        Assert.Equal(-1, result.ExitCode);
        Assert.Equal(0, policy.EvaluationCount);
        Assert.Single(artifactStore.Descriptors);
        Assert.Single(artifactStore.Payloads);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.True(payload.GetProperty("IsBlocked").GetBoolean());
        Assert.Equal(result.BlockReason, payload.GetProperty("BlockReason").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("StandardOutput").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("StandardError").GetString());
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

    private static PolicyViolationPayload AssertPolicyViolationPayload(string payloadText)
    {
        Assert.True(PolicyViolationPayload.TryParse(payloadText, out var payload));
        Assert.NotNull(payload);
        return payload!;
    }

    private sealed class AllowAllPolicy : IPolicy<ShellCommandPolicyRequest>
    {
        public PolicyId PolicyId => new("local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellCommandPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed class CountingAllowAllPolicy : IPolicy<ShellCommandPolicyRequest>
    {
        public int EvaluationCount { get; private set; }

        public PolicyId PolicyId => new("local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellCommandPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
        {
            EvaluationCount++;
            return ValueTask.FromResult(PolicyDecision.Allow());
        }
    }

    private sealed class DenyOutsideWorktreePolicy : IPolicy<ShellCommandPolicyRequest>
    {
        public PolicyId PolicyId => new("local-safe");

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellCommandPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
        {
            var normalizedWorktree = EnsureTrailingSeparator(Path.GetFullPath(context.WorktreePath));
            var normalizedCandidate = Path.GetFullPath(request.WorkingDirectory);

            var isInside = string.Equals(
                               normalizedWorktree.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                               normalizedCandidate,
                               StringComparison.OrdinalIgnoreCase)
                           || normalizedCandidate.StartsWith(normalizedWorktree, StringComparison.OrdinalIgnoreCase);

            return ValueTask.FromResult(
                isInside
                    ? PolicyDecision.Allow()
                    : PolicyDecision.Deny("Blocked by local-safe policy: command working directory resolves outside the active worktree boundary."));
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
                return path;

            return path + Path.DirectorySeparatorChar;
        }
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
