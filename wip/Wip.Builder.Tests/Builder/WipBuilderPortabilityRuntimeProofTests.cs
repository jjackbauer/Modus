using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Runtime;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Builder.Tests.Builder;

public sealed class WipBuilderPortabilityRuntimeProofTests
{
    private const string ChecklistItem = "Add builder portability proof that `AddWipRuntime` and `AddWipCapabilities` operate in non-shell .NET hosts with no shell-only transitive dependency or command-loop requirement [depends on BS-006 standalone builder usability]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task AddWipCapabilities_GivenNonShellHostComposition_ExecutesWorkflowWithoutShellCommandLoop()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddWorkflow<PortableWorkflow, PortableWorkflowRequest, PortableWorkflowResult>(
            workflowId: new WorkflowId("workflow.portable.capabilities"),
            displayName: "Portable capabilities workflow");

        var orchestrator = new WipRuntimeOrchestrator(new InMemorySessionStore(), new CollectingSessionEventPublisher());
        var root = Path.Combine(Path.GetTempPath(), "wip-builder-portable-capabilities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var started = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("workflow.portable.capabilities"),
                    TaskDescription: "prove builder portability",
                    RepositoryPath: root,
                    WorktreePath: Path.Combine(root, "worktree"),
                    ArtifactDirectory: Path.Combine(root, "artifacts")),
                CancellationToken.None);

            var result = await orchestrator.RunWorkflowAsync(
                started.SessionId,
                builder,
                selectedWorkflowId: new WorkflowId("workflow.portable.capabilities"),
                cancellationToken: CancellationToken.None);

            var runStage = Assert.Single(result.Stages, static stage => stage.Descriptor.Stage == WorkflowStageKind.Run);
            Assert.Equal("workflow.portable.capabilities", result.WorkflowId.Value);
            Assert.Equal(typeof(PortableWorkflowRequest), runStage.Descriptor.RequestType);
            Assert.Equal(typeof(PortableWorkflowResult), runStage.Descriptor.ResultType);
            Assert.Equal(typeof(PortableWorkflowRequest).FullName, runStage.MappedInputContractName);
            Assert.Null(Type.GetType("Wip.Shell.Interactive.WipShellCommandLoop, Wip.Shell", throwOnError: false));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task AddWipRuntime_GivenPlainDotNetHost_ResolvesRuntimeAndRunsWithoutShellOnlyDependencies()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipRuntime();
        builder.AddWorkflow<PortableWorkflow, PortableWorkflowRequest, PortableWorkflowResult>(
            workflowId: new WorkflowId("workflow.portable.runtime"),
            displayName: "Portable runtime workflow");

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<WipRuntimeOrchestrator>();
        var root = Path.Combine(Path.GetTempPath(), "wip-builder-portable-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var started = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("workflow.portable.runtime"),
                    TaskDescription: "prove runtime portability",
                    RepositoryPath: root,
                    WorktreePath: Path.Combine(root, "worktree"),
                    ArtifactDirectory: Path.Combine(root, "artifacts")),
                CancellationToken.None);

            var result = await orchestrator.RunWorkflowAsync(
                started.SessionId,
                builder,
                selectedWorkflowId: new WorkflowId("workflow.portable.runtime"),
                cancellationToken: CancellationToken.None);

            var runStage = Assert.Single(result.Stages, static stage => stage.Descriptor.Stage == WorkflowStageKind.Run);
            Assert.Equal("workflow.portable.runtime", result.WorkflowId.Value);
            Assert.Equal(typeof(PortableWorkflowRequest), runStage.Descriptor.RequestType);
            Assert.Equal(typeof(PortableWorkflowResult), runStage.Descriptor.ResultType);
            Assert.Equal(typeof(PortableWorkflowRequest).FullName, runStage.MappedInputContractName);

            var depsFilePath = Path.Combine(
                AppContext.BaseDirectory,
                $"{typeof(WipBuilderPortabilityRuntimeProofTests).Assembly.GetName().Name}.deps.json");
            var depsJson = File.ReadAllText(depsFilePath);

            Assert.DoesNotContain("\"Wip.Shell/", depsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Wip.ShellHost/", depsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Wip.Tools.Shell/", depsJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed record PortableWorkflowRequest(string Goal);

    private sealed record PortableWorkflowResult(string Summary);

    private sealed class PortableWorkflow : IWorkflow<PortableWorkflowRequest, PortableWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.portable");

        public ValueTask<PortableWorkflowResult> ExecuteAsync(
            PortableWorkflowRequest request,
            WorkflowContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new PortableWorkflowResult($"processed:{request.Goal}"));
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
            {
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);
            }

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}