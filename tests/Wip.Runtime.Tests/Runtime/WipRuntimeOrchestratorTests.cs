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

public sealed class WipRuntimeOrchestratorTests
{
    [Fact]
    public async Task StartSessionAsync_GivenValidRepository_CreatesCreatedStateAndEmitsStartEvent()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/abc123",
            cancellationToken: CancellationToken.None);

        var persisted = await store.LoadAsync(snapshot.SessionId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(SessionState.Created, persisted!.State);
        Assert.Equal("C:/repo", persisted.RepositoryPath);
        Assert.Equal("C:/repo/.wip/worktrees/abc123", persisted.WorktreePath);

        var sessionEvent = Assert.Single(publisher.Events);
        Assert.Equal(SessionEventKind.SessionStarted, sessionEvent.Kind);
        Assert.Equal(snapshot.SessionId, sessionEvent.SessionId);
        Assert.Equal(SessionState.Created, sessionEvent.CurrentState);
        Assert.Null(sessionEvent.PreviousState);
    }

    [Fact]
    public async Task TransitionAsync_GivenCommandSequence_RecordsExplicitStateChangesAndSessionEvents()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/def456",
            cancellationToken: CancellationToken.None);

        await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Editing, CancellationToken.None);
        await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Validating, CancellationToken.None);
        await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.AwaitingApproval, CancellationToken.None);
        await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Approved, CancellationToken.None);
        var merged = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Merged, CancellationToken.None);

        Assert.Equal(SessionState.Merged, merged.State);

        Assert.Collection(
            publisher.Events,
            e =>
            {
                Assert.Equal(SessionEventKind.SessionStarted, e.Kind);
                Assert.Null(e.PreviousState);
                Assert.Equal(SessionState.Created, e.CurrentState);
            },
            e => AssertTransition(e, SessionState.Created, SessionState.Editing),
            e => AssertTransition(e, SessionState.Editing, SessionState.Validating),
            e => AssertTransition(e, SessionState.Validating, SessionState.AwaitingApproval),
            e => AssertTransition(e, SessionState.AwaitingApproval, SessionState.Approved),
            e => AssertTransition(e, SessionState.Approved, SessionState.Merged));
    }

    [Fact]
    public async Task TransitionAsync_GivenInvalidTransition_ThrowsAndDoesNotMutateStateOrEmitTransitionEvent()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/ghi789",
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Validating, CancellationToken.None));

        var unchanged = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);

        Assert.Equal(SessionState.Created, unchanged!.State);
        Assert.Single(publisher.Events);
        Assert.Contains("Expected next state is Editing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartSessionAsync_GivenConcurrentCalls_AppliesSingleProcessAuthorityWithoutOverlappingSaves()
    {
        var store = new ConcurrencyTrackingSessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var first = orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/jkl111",
            cancellationToken: CancellationToken.None);

        var second = orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/jkl222",
            cancellationToken: CancellationToken.None);

        await Task.WhenAll(first.AsTask(), second.AsTask());

        Assert.Equal(1, store.MaxConcurrentSaves);
        Assert.Equal(2, publisher.Events.Count(e => e.Kind == SessionEventKind.SessionStarted));
    }

    [Fact]
    public async Task StartSessionAsync_GivenValidRepository_PersistsSessionStateJsonAtDeterministicPath()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "persisted"),
                cancellationToken: CancellationToken.None);

            var statePath = Path.Combine(
                repositoryPath,
                ".wip",
                "sessions",
                snapshot.SessionId.Value,
                "session-state.json");

            Assert.True(File.Exists(statePath));

            var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
            var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            Assert.Equal(snapshot.SessionId.Value, root.GetProperty("SessionId").GetString());
            Assert.Equal("Created", root.GetProperty("State").GetString());
            Assert.Equal(repositoryPath, root.GetProperty("RepositoryPath").GetString());
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task AttachSessionAsync_GivenPersistedSession_RestoresSnapshotAndDetachClearsAttachedContext()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
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

            Assert.Equal(SessionState.Editing, attached.State);

            var detached = await restoredOrchestrator.DetachSessionAsync(CancellationToken.None);
            var detachedAgain = await restoredOrchestrator.DetachSessionAsync(CancellationToken.None);

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

    [Fact]
    public async Task RunWorkflowAsync_GivenSelectedWorkflow_ExecutesLinearStagesAndProducesStageDescriptorsInOrder()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows();

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

        Assert.Equal("workflow.linear", result.WorkflowId.Value);
        Assert.Collection(
            result.Stages,
            stage => Assert.Equal(WorkflowStageKind.Plan, stage.Descriptor.Stage),
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Run, stage.Descriptor.Stage);
                Assert.Equal(typeof(WorkflowRequest), stage.Descriptor.RequestType);
                Assert.Equal(typeof(WorkflowResult), stage.Descriptor.ResultType);
            },
            stage => Assert.Equal(WorkflowStageKind.Validate, stage.Descriptor.Stage),
            stage => Assert.Equal(WorkflowStageKind.Review, stage.Descriptor.Stage),
            stage => Assert.Equal(WorkflowStageKind.RequireApproval, stage.Descriptor.Stage),
            stage => Assert.Equal(WorkflowStageKind.Merge, stage.Descriptor.Stage));

        var merged = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        Assert.Equal(SessionState.Merged, merged!.State);
    }

    [Fact]
    public async Task RunWorkflowAsync_GivenNoSelectedWorkflowAndMultipleCandidates_RequestsExplicitSelection()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows();

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.unknown"),
            repositoryPath: "C:/repo",
            worktreePath: "C:/repo/.wip/worktrees/select",
            cancellationToken: CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await orchestrator.RunWorkflowAsync(
                sessionId: snapshot.SessionId,
                builder: builder,
                selectedWorkflowId: null,
                cancellationToken: CancellationToken.None));

        Assert.Contains("explicit workflow selection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertTransition(SessionEvent sessionEvent, SessionState previous, SessionState current)
    {
        Assert.Equal(SessionEventKind.SessionTransitioned, sessionEvent.Kind);
        Assert.Equal(previous, sessionEvent.PreviousState);
        Assert.Equal(current, sessionEvent.CurrentState);
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

    private sealed class ConcurrencyTrackingSessionStore : ISessionStore
    {
        private int _inFlightSaves;

        public int MaxConcurrentSaves { get; private set; }

        public async ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            var inFlight = Interlocked.Increment(ref _inFlightSaves);
            MaxConcurrentSaves = Math.Max(MaxConcurrentSaves, inFlight);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlightSaves);
            }
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<SessionSnapshot?>(null);
    }

    private static WipBuilder CreateBuilderWithWorkflows()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");

        builder.AddWorkflow<SecondaryWorkflow, SecondaryWorkflowRequest, SecondaryWorkflowResult>(
            workflowId: new WorkflowId("workflow.secondary"),
            displayName: "Secondary workflow");

        return builder;
    }

    private sealed record WorkflowRequest(string Task);

    private sealed record WorkflowResult(string Outcome);

    private sealed class LinearWorkflow : IWorkflow<WorkflowRequest, WorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.linear");

        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new WorkflowResult(request.Task));
    }

    private sealed record SecondaryWorkflowRequest(string Task);

    private sealed record SecondaryWorkflowResult(string Outcome);

    private sealed class SecondaryWorkflow : IWorkflow<SecondaryWorkflowRequest, SecondaryWorkflowResult>
    {
        public WorkflowId WorkflowId => new("workflow.secondary");

        public ValueTask<SecondaryWorkflowResult> ExecuteAsync(SecondaryWorkflowRequest request, WorkflowContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new SecondaryWorkflowResult(request.Task));
    }
}
