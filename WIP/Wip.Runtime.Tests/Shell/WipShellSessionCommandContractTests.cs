using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Wip.Shell.Interactive;
using Xunit;

namespace Wip.Runtime.Tests.Shell;

public sealed class WipShellSessionCommandContractTests
{
    private const string ChecklistItem = "Align shell command contract to full MVP global/session command surface, including context-sensitive errors and command guidance when no session is active [depends on shell baseline]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task SessionStart_GivenQuotedTask_StartsWithoutLegacyRepositoryArgumentsAndPersistsRuntimeMetadata()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new CollectingSessionEventPublisher());
            var builder = CreateBuilderWithShellWorkflow();
            using var reader = new StringReader("init\nuse workflow workflow.shell\nsession start \"Capture runtime metadata\"\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, builder, repositoryRoot: repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            var sessionId = ExtractSessionId(output, "Session started:");
            using var persistedState = await ReadPersistedSessionStateAsync(repositoryPath, sessionId);
            var root = persistedState.RootElement;

            Assert.Equal(0, exitCode);
            Assert.Contains("Repository initialized:", output, StringComparison.Ordinal);
            Assert.Contains($"Session started: {sessionId}", output, StringComparison.Ordinal);
            Assert.Contains("workflow=workflow.shell", output, StringComparison.Ordinal);
            Assert.Equal("Capture runtime metadata", root.GetProperty("TaskDescription").GetString());
            Assert.Equal("workflow.shell", root.GetProperty("WorkflowId").GetString());
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "worktrees", sessionId.Value), root.GetProperty("WorktreePath").GetString());
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "artifacts"), root.GetProperty("ArtifactDirectory").GetString());
            Assert.DoesNotContain("Usage: session start \"<task>\" [repository-path] [worktree-path]", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task SessionAttach_GivenPersistedSessionId_AttachesByIdWithoutLegacyRepositoryArgument()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);
            var started = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("workflow.shell"),
                    TaskDescription: "Resume shell attach",
                    RepositoryPath: repositoryPath,
                    BaseBranch: "main",
                    BaseCommit: "abc123",
                    TargetBranch: "main",
                    TargetCommit: "abc123"),
                CancellationToken.None);

            await orchestrator.DetachSessionAsync(CancellationToken.None);

            using var reader = new StringReader($"session attach {started.SessionId}\nrepo\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, repositoryRoot: repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains($"Session attached: {started.SessionId}", output, StringComparison.Ordinal);
            Assert.Contains($"wip[{started.SessionId}]> ", output, StringComparison.Ordinal);
            Assert.Contains($"activeSession: {started.SessionId} state=Created workflow=workflow.shell", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Usage: session attach <session-id> [repository-path]", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task SessionCommands_GivenLegacyRepositoryAndWorktreeArguments_RejectOldShellContractWithoutPersistingSessions()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var worktreePath = Path.Combine(repositoryPath, "manual-worktree");
            var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new CollectingSessionEventPublisher());
            using var reader = new StringReader($"session start \"Legacy args\" {repositoryPath} {worktreePath}\nsession attach session-123 {repositoryPath}\nsessions\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, repositoryRoot: repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();
            var sessionsDirectory = Path.Combine(repositoryPath, ".wip", "sessions");

            Assert.Equal(0, exitCode);
            Assert.Contains("Usage: session start \"<task>\"", output, StringComparison.Ordinal);
            Assert.Contains("Usage: session attach <session-id>", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Session started:", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Session attached:", output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(sessionsDirectory));
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task SessionCommands_GivenNoActiveSession_RenderGuidanceForEntireSessionCommandSurface()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new CollectingSessionEventPublisher());
            using var reader = new StringReader("plan\nrun\ndiff\ncheckpoint\nvalidate\nreview\napprove\nmerge\nartifacts\nstatus\ndetach\narchive\nabort\nexit\n");
            using var writer = new StringWriter();
            var loop = new WipShellCommandLoop(orchestrator, reader, writer, repositoryRoot: repositoryPath);

            var exitCode = await loop.RunAsync(CancellationToken.None);

            var output = writer.ToString();

            Assert.Equal(0, exitCode);

            foreach (var command in new[] { "plan", "run", "diff", "checkpoint", "validate", "review", "approve", "merge", "artifacts", "status", "detach", "archive", "abort" })
            {
                Assert.Contains($"Command '{command}' requires an active session.", output, StringComparison.Ordinal);
            }

            Assert.Contains("Start a session with: session start \"<task>\"", output, StringComparison.Ordinal);
            Assert.Contains("Attach a session with: session attach <session-id>", output, StringComparison.Ordinal);
            Assert.Contains("Session commands: plan, run, diff, checkpoint, validate, review, approve, merge, artifacts, status, detach, archive, abort", output, StringComparison.Ordinal);
            Assert.Contains("Global commands available without a session: help, init, repo, config, sessions, session start \"<task>\", session attach <session-id>, use workflow <id>, workflows, plugins [load|unload], debug-logs, exit", output, StringComparison.Ordinal);
            Assert.DoesNotContain("wip[", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static SessionId ExtractSessionId(string output, string prefix)
    {
        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var prefixIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (prefixIndex < 0)
                continue;

            var remainder = line[(prefixIndex + prefix.Length)..].TrimStart();
            var separatorIndex = remainder.IndexOf(' ');
            var value = separatorIndex >= 0 ? remainder[..separatorIndex] : remainder;
            return new SessionId(value);
        }

        throw new InvalidOperationException($"Could not find '{prefix}' in shell output.{Environment.NewLine}{output}");
    }

    private static async Task<JsonDocument> ReadPersistedSessionStateAsync(string repositoryPath, SessionId sessionId)
    {
        var path = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(path, CancellationToken.None);
        return JsonDocument.Parse(payload);
    }

    private static WipBuilder CreateBuilderWithShellWorkflow()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);
        builder.AddWorkflow<ShellWorkflow, ShellWorkflowRequest, ShellWorkflowResult>(
            new WorkflowId("workflow.shell"),
            "Shell workflow");
        return builder;
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

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed record ShellWorkflowRequest(string TaskDescription);

    private sealed record ShellWorkflowResult(string Summary);

    private sealed class ShellWorkflow : IWorkflow<ShellWorkflowRequest, ShellWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.shell");

        public ValueTask<ShellWorkflowResult> ExecuteAsync(
            ShellWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ShellWorkflowResult(request.TaskDescription));
    }
}