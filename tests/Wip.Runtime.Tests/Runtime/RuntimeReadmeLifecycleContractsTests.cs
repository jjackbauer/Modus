using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class RuntimeReadmeLifecycleContractsTests
{
    private const string ChecklistItem = "Document Runtime orchestrator lifecycle including state transitions, persisted session snapshots, attach/detach flows, and workflow-stage progression [depends on Builder registration documentation]";

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
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/runtime-readme",
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
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/pipeline",
            cancellationToken: CancellationToken.None);

        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

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
                Assert.Equal(SessionState.Validating, stage.StateAfterStage);
                Assert.True(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Review, stage.Descriptor.Stage);
                Assert.Equal(SessionState.AwaitingApproval, stage.StateAfterStage);
                Assert.True(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.RequireApproval, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Approved, stage.StateAfterStage);
                Assert.True(stage.AppliedTransition);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Merge, stage.Descriptor.Stage);
                Assert.Equal(SessionState.Merged, stage.StateAfterStage);
                Assert.True(stage.AppliedTransition);
            });

        var merged = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        Assert.Equal(SessionState.Merged, merged!.State);
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

    private sealed class LinearWorkflow : IWorkflow<WorkflowRequest, WorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Task));
    }
}