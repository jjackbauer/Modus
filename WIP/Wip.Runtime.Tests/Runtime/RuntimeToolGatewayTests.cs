using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Builder;
using Wip.Policy.LocalSafe;
using Wip.Runtime.Runtime;
using Wip.Tools.Shell.Shell;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class RuntimeToolGatewayTests : IAsyncLifetime
{
    private const string ChecklistItem = "Implement Wip.Tools.Shell controlled command tool constrained to active worktree, denied dangerous patterns, and command execution log artifact production [depends on local-safe policy and runtime tool gateway]";
    private const string MergeIsolationChecklistItem = "Preserve privileged merge isolation so generic shell tool paths cannot trigger merge semantics or bypass approval/validation gates [depends on policy/tool hardening]";
    private static readonly CapabilityId ShellToolId = new("tool.shell.command");

    private string _worktreePath = string.Empty;

    public Task InitializeAsync()
    {
        _worktreePath = Path.Combine(Path.GetTempPath(), $"modus-runtime-tool-gateway-{Guid.NewGuid():N}");
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
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task InvokeAsync_GivenDangerousCommandPattern_ExpectedPolicyDeniesBeforeExecutionAndLogsReason()
    {
        var artifactStore = new InMemoryArtifactStore();
        var gateway = BuildGateway(artifactStore);

        var result = await gateway.InvokeAsync<ShellCommandRequest, ShellCommandResult>(
            ShellToolId,
            new ShellCommandRequest(
                Command: "rm -rf .",
                WorkflowId: new WorkflowId("workflow.shell")),
            new CapabilityContext(new SessionId("session-dangerous"), _worktreePath),
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("dangerous command rule", result.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("denied before execution", result.BlockReason, StringComparison.OrdinalIgnoreCase);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.True(payload.GetProperty("IsBlocked").GetBoolean());
        Assert.Contains(
            "dangerous command rule",
            payload.GetProperty("BlockReason").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task InvokeAsync_GivenWorkingDirectoryOutsideSessionWorktree_ExpectedPolicyDeniesPathBoundaryViolation()
    {
        var artifactStore = new InMemoryArtifactStore();
        var gateway = BuildGateway(artifactStore);

        var result = await gateway.InvokeAsync<ShellCommandRequest, ShellCommandResult>(
            ShellToolId,
            new ShellCommandRequest(
                Command: EchoCommand("blocked"),
                WorkflowId: new WorkflowId("workflow.shell"),
                RelativeWorkingDirectory: ".."),
            new CapabilityContext(new SessionId("session-boundary"), _worktreePath),
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("outside the active worktree boundary", result.BlockReason, StringComparison.OrdinalIgnoreCase);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.True(payload.GetProperty("IsBlocked").GetBoolean());
        Assert.Contains(
            "outside the active worktree boundary",
            payload.GetProperty("BlockReason").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task InvokeAsync_GivenAllowedCommandInsideWorktree_ExpectedCommandExecutesAndProducesExecutionLogArtifact()
    {
        var artifactStore = new InMemoryArtifactStore();
        var gateway = BuildGateway(artifactStore);

        var result = await gateway.InvokeAsync<ShellCommandRequest, ShellCommandResult>(
            ShellToolId,
            new ShellCommandRequest(
                Command: EchoCommand("gateway-allowed"),
                WorkflowId: new WorkflowId("workflow.shell"),
                Timeout: TimeSpan.FromSeconds(5)),
            new CapabilityContext(new SessionId("session-allowed"), _worktreePath),
            CancellationToken.None);

        Assert.False(result.IsBlocked);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("gateway-allowed", result.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.False(payload.GetProperty("IsBlocked").GetBoolean());
        Assert.Equal(0, payload.GetProperty("ExitCode").GetInt32());
        Assert.Contains("gateway-allowed", payload.GetProperty("StandardOutput").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", MergeIsolationChecklistItem)]
    public async Task InvokeAsync_GivenGenericShellMergeCommand_ExpectedPolicyDeniesBeforeExecutionAndLogsPrivilegedReason()
    {
        var artifactStore = new InMemoryArtifactStore();
        var gateway = BuildGateway(artifactStore);

        var result = await gateway.InvokeAsync<ShellCommandRequest, ShellCommandResult>(
            ShellToolId,
            new ShellCommandRequest(
                Command: "git merge feature/session",
                WorkflowId: new WorkflowId("workflow.shell")),
            new CapabilityContext(new SessionId("session-merge-isolation"), _worktreePath),
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Equal(-1, result.ExitCode);
        Assert.NotNull(result.BlockReason);

        Assert.True(PolicyViolationPayload.TryParse(result.BlockReason!, out var reasonPayload));
        Assert.NotNull(reasonPayload);
        Assert.Equal("git merge feature/session", reasonPayload!.BlockedAction);
        Assert.Equal("local-safe", reasonPayload.BlockingPolicy);
        Assert.Contains("dedicated shell merge flow", reasonPayload.NextStepGuidance, StringComparison.Ordinal);
        Assert.Equal(
            "Blocked by local-safe policy: merge semantics are privileged and must use the explicit merge command path with review, validation, and approval evidence.",
            reasonPayload.Reason);

        var payload = JsonDocument.Parse(artifactStore.Payloads.Single()).RootElement;
        Assert.True(payload.GetProperty("IsBlocked").GetBoolean());
        Assert.Equal(result.BlockReason, payload.GetProperty("BlockReason").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("StandardOutput").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("StandardError").GetString());
    }

    private RuntimeToolGateway BuildGateway(InMemoryArtifactStore artifactStore)
    {
        var services = new ServiceCollection();
        var builder = services.AddWipBuilder();

        services.AddSingleton<IArtifactStore>(artifactStore);
        services.AddSingleton<LocalSafePolicy>();
        services.AddSingleton<IPolicy<ShellCommandPolicyRequest>, LocalSafeShellCommandPolicyAdapter>();

        builder.AddTool<ShellCommandTool, ShellCommandRequest, ShellCommandResult>(
            ShellToolId,
            displayName: "Controlled Shell Command");

        var provider = services.BuildServiceProvider(validateScopes: true);
        return new RuntimeToolGateway(provider, builder.CapabilityDescriptors);
    }

    private static string EchoCommand(string text)
        => OperatingSystem.IsWindows() ? $"echo {text}" : $"echo '{EscapeSingleQuotes(text)}'";

    private static string EscapeSingleQuotes(string value)
        => value.Replace("'", "'\\''", StringComparison.Ordinal);

    private sealed class LocalSafeShellCommandPolicyAdapter : IPolicy<ShellCommandPolicyRequest>
    {
        private readonly LocalSafePolicy _inner;

        public LocalSafeShellCommandPolicyAdapter(LocalSafePolicy inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public PolicyId PolicyId => _inner.PolicyId;

        public ValueTask<PolicyDecision> EvaluateAsync(
            ShellCommandPolicyRequest request,
            PolicyContext context,
            CancellationToken cancellationToken)
        {
            var mapped = new LocalSafePolicyRequest(
                Command: request.Command,
                WorkingDirectory: request.WorkingDirectory,
                ValidationSucceeded: true,
                ApprovalGranted: true,
                RequireValidation: false,
                RequireApproval: false);

            return _inner.EvaluateAsync(mapped, context, cancellationToken);
        }
    }

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        private readonly List<ArtifactDescriptor> _descriptors = [];
        private readonly List<string> _payloads = [];

        public IReadOnlyList<string> Payloads => _payloads;

        public ValueTask<ArtifactDescriptor> SaveAsync(
            SessionId sessionId,
            ArtifactContent artifact,
            CancellationToken cancellationToken)
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