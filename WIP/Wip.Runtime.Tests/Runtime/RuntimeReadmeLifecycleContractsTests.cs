using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Wip.Shell.CommandSemantics;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class RuntimeReadmeLifecycleContractsTests
{
    private const string ChecklistItem = "Document Runtime orchestrator lifecycle including state transitions, persisted session snapshots, attach/detach flows, and workflow-stage progression [depends on Builder registration documentation]";
    private const string CommandSemanticsChecklistItem =
        "Publish authoritative shell command semantics contract documenting `plan`, `run`, `validate`, `review`, `approve`, `merge`, and `transition` boundaries so `run` is not described as completing governance lifecycle [foundation for execution and governance remediation]";

    [Fact]
    [Trait("ChecklistItem", CommandSemanticsChecklistItem)]
    public void RuntimeReadme_GivenWorkflowExecutionSection_ExpectedRunSemanticsMatchShellHelpAndOrchestratorBehavior()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var readmePath = Path.Combine(repositoryRoot, "WIP", "Wip.Runtime", "README.md");
        var readme = File.ReadAllText(readmePath);
        var help = WipShellCommandSemantics.RenderHelpText();

        var executionSectionStart = readme.IndexOf("## Workflow Execution Boundary", StringComparison.Ordinal);
        Assert.True(executionSectionStart >= 0, "Expected Workflow Execution Boundary section in runtime README.");
        var executionSection = readme[executionSectionStart..];

        Assert.Contains(WipShellCommandSemantics.RunExecutionBoundaryPhrase, help, StringComparison.Ordinal);
        Assert.Contains("execution-only", executionSection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WipShellCommandSemantics.RunDoesNotReplaceGovernancePhrase, executionSection, StringComparison.Ordinal);
        Assert.Contains("validate", executionSection, StringComparison.Ordinal);
        Assert.Contains("review", executionSection, StringComparison.Ordinal);
        Assert.Contains("approve", executionSection, StringComparison.Ordinal);
        Assert.Contains("merge", executionSection, StringComparison.Ordinal);
        Assert.Contains("RunWorkflowAsync", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("run completes governance", executionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("run completes merge", executionSection, StringComparison.OrdinalIgnoreCase);
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "WIP", "Wip.Runtime")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing WIP/Wip.Runtime.");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task RuntimeReadme_GivenStartSession_SnapshotPersistedAndSessionStartedEventPublished()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-readme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "runtime-readme"),
                cancellationToken: CancellationToken.None);

            var persisted = await store.LoadAsync(snapshot.SessionId, CancellationToken.None);
            var sessionEvent = Assert.Single(publisher.Events);
            var statePath = Path.Combine(repositoryPath, ".wip", "sessions", snapshot.SessionId.Value, "session-state.json");

            Assert.NotNull(persisted);
            Assert.Equal(SessionState.Created, persisted!.State);
            Assert.Equal(SessionEventKind.SessionStarted, sessionEvent.Kind);
            Assert.Equal(snapshot.SessionId, sessionEvent.SessionId);
            Assert.Equal(SessionState.Created, sessionEvent.CurrentState);
            Assert.True(File.Exists(statePath));

            var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
            var document = JsonDocument.Parse(payload);
            Assert.Equal(snapshot.WorkflowId.Value, document.RootElement.GetProperty("WorkflowId").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task RuntimeReadme_GivenInvalidTransition_TransitionRejectedWithExpectedNextStateMessage()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("runtime-readme"),
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Validating, CancellationToken.None));

        var unchanged = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);

        Assert.Equal(SessionState.Created, unchanged!.State);
        Assert.Single(publisher.Events);
        Assert.Contains("Expected next state is Editing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task RuntimeReadme_GivenRunWorkflowAcrossStages_StateProgressionMatchesStageDescriptors()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithLinearWorkflow();

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("pipeline"),
            cancellationToken: CancellationToken.None);

        var artifactStore = new ReadmeInMemoryArtifactStore();
        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None,
            artifactStore: artifactStore);

        Assert.Collection(
            result.Stages,
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Plan, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Editing, stage.StateAfterStage);
                Assert.True(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Run, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Editing, stage.StateAfterStage);
                Assert.False(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Validate, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Editing, stage.StateAfterStage);
                Assert.False(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Review, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Editing, stage.StateAfterStage);
                Assert.False(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.RequireApproval, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Editing, stage.StateAfterStage);
                Assert.False(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Merge, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Editing, stage.StateAfterStage);
                Assert.False(stage.AppliedTransition);
            });

        var afterRun = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        Assert.Equal(SessionState.Editing, afterRun!.State);
        Assert.Equal(SessionApprovalStatus.NotRequested, afterRun.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, afterRun.ValidationStatus);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task RuntimeReadme_GivenAttachWithoutInMemorySession_PersistedSessionStateRestoresSuccessfully()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-readme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var initialStore = new InMemorySessionStore();
            var initialPublisher = new CollectingSessionEventPublisher();
            var initialOrchestrator = new WipRuntimeOrchestrator(initialStore, initialPublisher);

            var created = await initialOrchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "restore"),
                cancellationToken: CancellationToken.None);

            await initialOrchestrator.TransitionAsync(created.SessionId, SessionState.Editing, CancellationToken.None);

            var restoredStore = new InMemorySessionStore();
            var restoredPublisher = new CollectingSessionEventPublisher();
            var restoredOrchestrator = new WipRuntimeOrchestrator(restoredStore, restoredPublisher);

            var attached = await restoredOrchestrator.AttachSessionAsync(
                repositoryPath,
                created.SessionId,
                CancellationToken.None);

            var detached = await restoredOrchestrator.DetachSessionAsync(CancellationToken.None);
            var detachedAgain = await restoredOrchestrator.DetachSessionAsync(CancellationToken.None);

            Assert.Equal(SessionState.Editing, attached.State);
            Assert.True(detached);
            Assert.False(detachedAgain);
            Assert.Contains(restoredPublisher.Events, e => e.Kind == SessionEventKind.SessionAttached);
            Assert.Contains(restoredPublisher.Events, e => e.Kind == SessionEventKind.SessionDetached);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    private static WipBuilder CreateBuilderWithLinearWorkflow()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddAgent<ReadmePlanStageAgent, PlanStageRequest, PlanStageResult>(
            new CapabilityId("agent.workflow.plan"),
            displayName: "Workflow plan stage agent");
        builder.AddTool<ReadmeRunTool, WorkflowRequest, WorkflowResult>(
            new CapabilityId("tool.workflow.run"),
            displayName: "Workflow run tool");

        return builder;
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

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public List<SessionEvent> Events { get; } = [];

        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            Events.Add(sessionEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record WorkflowRequest(string Task);

    private sealed record WorkflowResult(string Outcome);

    private sealed class ReadmeRunTool : ITool<WorkflowRequest, WorkflowResult>
    {
        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Task));
    }

    private sealed class ReadmePlanStageAgent : IAgent<PlanStageRequest, PlanStageResult>
    {
        public ValueTask<PlanStageResult> ExecuteAsync(PlanStageRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanStageResult(true));
    }

    private sealed class LinearWorkflow : IWorkflow<WorkflowRequest, WorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Task));
    }

    private sealed class ReadmeInMemoryArtifactStore : IArtifactStore
    {
        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                $"artifacts/{sessionId.Value}/{artifact.FileName}-{artifact.ArtifactId.Value}",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>([]);
    }
}