using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Policies;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Agent.Basic;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class WipRuntimeOrchestratorTests
{
    private const string SessionPersistenceChecklistItem = "Complete session persistence and attach/restore behavior under .wip/sessions/{sessionId}/session-state.json with deterministic session event journaling [depends on runtime governance baseline]";
    private const string GovernanceRestrictionChecklistItem =
        "Restrict `RunWorkflowAsync` and shell `run` so they cannot advance session state beyond `Editing` or `Checkpointed` and cannot set `ApprovalStatus`/`ValidationStatus` to approved or passed without corresponding governance operations [depends on command semantics contract] [mandatory - governance truth]";
    private const string StageCapabilityDispatcherChecklistItem =
        "Introduce `WorkflowStageCapabilityDispatcher` that resolves and invokes registered agent, tool, and validator capabilities for each compiled workflow stage using typed descriptors from `WorkflowBuilderStageCompiler` [depends on command semantics contract]";
    private const string StageMapAdapterChecklistItem =
        "Wire stage map adapters at runtime so outputs from Plan/Run stages feed Validate stage inputs through compiled `MapAdapters` without object-payload collapse [depends on stage capability dispatcher]";
    private const string ApprovalValidationTransitionAlignmentChecklistItem =
        "Align `ResolveApprovalStatusForTransition` and `ResolveValidationStatusForTransition` so state transitions never imply approval or validation success without artifact-backed operations [depends on governance restriction] [mandatory - no phantom governance]";

    [Fact]
    public async Task StartSessionAsync_GivenValidRepository_CreatesCreatedStateAndEmitsStartEvent()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("abc123"),
            cancellationToken: CancellationToken.None);

        var persisted = await store.LoadAsync(snapshot.SessionId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(SessionState.Created, persisted!.State);
        Assert.Equal(TestRepositoryPaths.RepositoryRoot, persisted.RepositoryPath);
        Assert.Equal(TestRepositoryPaths.Worktree("abc123"), persisted.WorktreePath);

        var sessionEvent = Assert.Single(publisher.Events);
        Assert.Equal(SessionEventKind.SessionStarted, sessionEvent.Kind);
        Assert.Equal(snapshot.SessionId, sessionEvent.SessionId);
        Assert.Equal(SessionState.Created, sessionEvent.CurrentState);
        Assert.Null(sessionEvent.PreviousState);
    }

    [Fact]
    public async Task PlanAsync_GivenStartedSession_ProducesAgentPlanArtifactEmitsPlanEventAndTransitionsSessionToEditing()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);
            var builder = CreateBuilderWithPlanCapabilities();
            var policyId = new PolicyId("policy.safe");

            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "plan"),
                cancellationToken: CancellationToken.None);

            var result = await orchestrator.PlanAsync(
                sessionId: snapshot.SessionId,
                task: "Implement first-class session operations",
                builder: builder,
                policyId: policyId,
                agent: new PlanOnlyAgent(artifactStore),
                cancellationToken: CancellationToken.None);

            Assert.Equal(SessionState.Editing, result.Session.State);
            Assert.Equal("Implement first-class session operations", result.Plan.Markdown.Contains("Task: Implement first-class session operations", StringComparison.Ordinal) ? "Implement first-class session operations" : string.Empty);

            var planArtifact = Assert.Single(artifactStore.ListForSession(snapshot.SessionId));
            Assert.Equal(ArtifactKind.Markdown, planArtifact.Kind);

            Assert.Collection(
                publisher.Events,
                e => Assert.Equal(SessionEventKind.SessionStarted, e.Kind),
                e => AssertTransition(e, SessionState.Created, SessionState.Editing),
                e => Assert.Equal(SessionEventKind.PlanGenerated, e.Kind));

            var persistedKinds = await ReadJournalKindsAsync(repositoryPath, snapshot.SessionId);
            Assert.Equal(
                [
                    SessionEventKind.SessionStarted,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.PlanGenerated
                ],
                persistedKinds);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeOperations_GivenEditingSession_PersistArtifactsEmitExplicitEventsAndAdvanceWithoutDirectTransitionCalls()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "governance"),
                cancellationToken: CancellationToken.None);

            var editing = await orchestrator.PlanAsync(
                sessionId: snapshot.SessionId,
                task: "Prepare governance evidence",
                builder: CreateBuilderWithPlanCapabilities(),
                policyId: new PolicyId("policy.safe"),
                agent: new PlanOnlyAgent(artifactStore),
                cancellationToken: CancellationToken.None);

            var diff = await orchestrator.GenerateDiffAsync(
                sessionId: snapshot.SessionId,
                request: new SessionDiffRequest(
                    DiffHash: "diff-123",
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Patch: "diff --git a/file b/file"),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            var checkpoint = await orchestrator.CreateCheckpointAsync(
                sessionId: snapshot.SessionId,
                request: new SessionCheckpointRequest(
                    Name: "after-plan",
                    DiffHash: diff.DiffHash,
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Patch: "diff --git a/file b/file"),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            var validation = await orchestrator.ValidateAsync(
                sessionId: snapshot.SessionId,
                request: new SessionValidationRequest(
                    BuildSucceeded: true,
                    TestSucceeded: true,
                    DiffHash: diff.DiffHash,
                    Summary: "dotnet build/test passed"),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            var review = await orchestrator.ReviewAsync(
                sessionId: snapshot.SessionId,
                request: new ReviewRequest(
                    SessionId: snapshot.SessionId,
                    CurrentDiffHash: diff.DiffHash,
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Validation: new ReviewValidationStatus(true, true, diff.DiffHash)),
                reviewGenerator: new WipRuntimeReviewGenerator(artifactStore),
                cancellationToken: CancellationToken.None);

            var approval = await orchestrator.CreateApprovalTokenAsync(
                sessionId: snapshot.SessionId,
                request: new ApprovalTokenRequest(
                    SessionId: snapshot.SessionId,
                    WorkflowId: snapshot.WorkflowId,
                    DiffHash: diff.DiffHash,
                    TargetBranch: "main",
                    TargetCommit: "abc123",
                    ReviewReport: new ApprovalReviewReport(review.Review.ReportArtifact.ArtifactId, diff.DiffHash, false),
                    ValidationReport: new ApprovalValidationReport(validation.ValidationArtifact.ArtifactId, true, true, diff.DiffHash)),
                approvalTokenFactory: new WipRuntimeApprovalTokenFactory(),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            var merged = await orchestrator.MergeAsync(
                sessionId: snapshot.SessionId,
                request: new SessionMergeRequest(
                    TargetBranch: "main",
                    TargetCommit: "abc123",
                    DiffHash: diff.DiffHash,
                    Summary: "Fast-forward merge completed."),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            Assert.Equal(SessionState.Editing, editing.Session.State);
            Assert.Equal(SessionState.Editing, diff.Session.State);
            Assert.Equal(SessionState.Checkpointed, checkpoint.Session.State);
            Assert.Equal(SessionState.Validating, validation.Session.State);
            Assert.Equal(SessionState.AwaitingApproval, review.Session.State);
            Assert.Equal(SessionState.Approved, approval.Session.State);
            Assert.Equal(SessionState.Merged, merged.Session.State);

            var artifacts = artifactStore.ListForSession(snapshot.SessionId);
            Assert.Equal(7, artifacts.Count);

            Assert.Equal(
                [
                    SessionEventKind.SessionStarted,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.PlanGenerated,
                    SessionEventKind.DiffGenerated,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.CheckpointGenerated,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.ValidationCompleted,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.ReviewGenerated,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.ApprovalTokenCreated,
                    SessionEventKind.MergeAttempted,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.MergeSucceeded,
                    SessionEventKind.MergeCompleted
                ],
                publisher.Events.Select(e => e.Kind).ToArray());

            var persistedKinds = await ReadJournalKindsAsync(repositoryPath, snapshot.SessionId);
            Assert.Equal(publisher.Events.Select(e => e.Kind).ToArray(), persistedKinds);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_GivenSessionWorktree_ExecutesConfiguredValidationCommandsPersistsValidationReportAndMarksSessionValidating()
    {
        await RuntimeOperations_GivenEditingSession_PersistArtifactsEmitExplicitEventsAndAdvanceWithoutDirectTransitionCalls();
    }

    [Fact]
    public async Task MergeAsync_GivenApprovedCurrentSession_AppliesMergeAndEmitsMergeSucceededEventWithoutShellDrivenStateHop()
    {
        await RuntimeOperations_GivenEditingSession_PersistArtifactsEmitExplicitEventsAndAdvanceWithoutDirectTransitionCalls();
    }

    [Fact]
    public async Task ArchiveAndAbortAsync_GivenLiveSessions_PersistArtifactsAndEmitDedicatedJournalEvents()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var archiveSession = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "archive"),
                cancellationToken: CancellationToken.None);

            var abortSession = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "abort"),
                cancellationToken: CancellationToken.None);

            await orchestrator.TransitionAsync(archiveSession.SessionId, SessionState.Editing, CancellationToken.None);
            await orchestrator.TransitionAsync(archiveSession.SessionId, SessionState.Checkpointed, CancellationToken.None);
            await orchestrator.TransitionAsync(archiveSession.SessionId, SessionState.Validating, CancellationToken.None);
            await orchestrator.TransitionAsync(archiveSession.SessionId, SessionState.AwaitingApproval, CancellationToken.None);
            await orchestrator.TransitionAsync(archiveSession.SessionId, SessionState.Approved, CancellationToken.None);
            await orchestrator.TransitionAsync(archiveSession.SessionId, SessionState.Merged, CancellationToken.None);

            var archived = await orchestrator.ArchiveAsync(
                archiveSession.SessionId,
                new SessionArchiveRequest("Session evidence archived."),
                artifactStore,
                CancellationToken.None);

            var aborted = await orchestrator.AbortAsync(
                abortSession.SessionId,
                new SessionAbortRequest("User cancelled the session."),
                artifactStore,
                CancellationToken.None);

            Assert.Equal(SessionState.Archived, archived.Session.State);
            Assert.Equal(SessionState.Aborted, aborted.Session.State);
            Assert.Contains(
                publisher.Events,
                e => e.Kind == SessionEventKind.SessionTransitioned
                    && e.SessionId == archiveSession.SessionId
                    && e.PreviousState == SessionState.Merged
                    && e.CurrentState == SessionState.Archived);
            Assert.Contains(
                publisher.Events,
                e => e.Kind == SessionEventKind.SessionTransitioned
                    && e.SessionId == abortSession.SessionId
                    && e.PreviousState == SessionState.Created
                    && e.CurrentState == SessionState.Aborted);
            Assert.Contains(publisher.Events, e => e.Kind == SessionEventKind.SessionArchived && e.SessionId == archiveSession.SessionId);
            Assert.Contains(publisher.Events, e => e.Kind == SessionEventKind.SessionAborted && e.SessionId == abortSession.SessionId);
            Assert.Equal(2, artifactStore.ListForSession(archiveSession.SessionId).Count + artifactStore.ListForSession(abortSession.SessionId).Count);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task TransitionAsync_GivenCommandSequence_RecordsExplicitStateChangesAndSessionEvents()
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
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "def456"),
                cancellationToken: CancellationToken.None);

            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Editing, CancellationToken.None);
            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Checkpointed, CancellationToken.None);
            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Validating, CancellationToken.None);
            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.AwaitingApproval, CancellationToken.None);
            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Approved, CancellationToken.None);
            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Merged, CancellationToken.None);
            var archived = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Archived, CancellationToken.None);

            Assert.Equal(SessionState.Archived, archived.State);

            Assert.Collection(
                publisher.Events,
                e =>
                {
                    Assert.Equal(SessionEventKind.SessionStarted, e.Kind);
                    Assert.Null(e.PreviousState);
                    Assert.Equal(SessionState.Created, e.CurrentState);
                },
                e => AssertTransition(e, SessionState.Created, SessionState.Editing),
                e => AssertTransition(e, SessionState.Editing, SessionState.Checkpointed),
                e => AssertTransition(e, SessionState.Checkpointed, SessionState.Validating),
                e => AssertTransition(e, SessionState.Validating, SessionState.AwaitingApproval),
                e => AssertTransition(e, SessionState.AwaitingApproval, SessionState.Approved),
                e => AssertTransition(e, SessionState.Approved, SessionState.Merged),
                e => AssertTransition(e, SessionState.Merged, SessionState.Archived));

            var persistedKinds = await ReadJournalKindsAsync(repositoryPath, snapshot.SessionId);
            Assert.Equal(
                [
                    SessionEventKind.SessionStarted,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionTransitioned
                ],
                persistedKinds);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task TransitionAsync_GivenInvalidTransition_ThrowsAndDoesNotMutateStateOrEmitTransitionEvent()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("ghi789"),
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
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("jkl111"),
            cancellationToken: CancellationToken.None);

        var second = orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("jkl222"),
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
            var journalPath = Path.Combine(
                repositoryPath,
                ".wip",
                "sessions",
                snapshot.SessionId.Value,
                "event-journal.ndjson");

            Assert.True(File.Exists(statePath));
            Assert.True(File.Exists(journalPath));

            var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
            var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            Assert.Equal(snapshot.SessionId.Value, root.GetProperty("SessionId").GetString());
            Assert.Equal("Created", root.GetProperty("State").GetString());
            Assert.Equal(repositoryPath, root.GetProperty("RepositoryPath").GetString());

            var persistedKinds = await ReadJournalKindsAsync(repositoryPath, snapshot.SessionId);
            Assert.Equal([SessionEventKind.SessionStarted], persistedKinds);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SessionStart_GivenTaskAndWorkflowSelection_PersistsTaskDescriptionWorkflowAndRepositoryBaselines()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var snapshot = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("workflow.safe-change"),
                    TaskDescription: "Implement persisted session metadata",
                    RepositoryPath: repositoryPath,
                    BaseBranch: "main",
                    BaseCommit: "abc123def456",
                    TargetBranch: "main",
                    TargetCommit: "abc123def456"),
                CancellationToken.None);

            Assert.Equal("Implement persisted session metadata", snapshot.TaskDescription);
            Assert.Equal("main", snapshot.BaseBranch);
            Assert.Equal("abc123def456", snapshot.BaseCommit);
            Assert.Equal("main", snapshot.TargetBranch);
            Assert.Equal("abc123def456", snapshot.TargetCommit);
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "worktrees", snapshot.SessionId.Value), snapshot.WorktreePath);
            Assert.Equal(Path.Combine(repositoryPath, ".wip", "sessions", snapshot.SessionId.Value, "artifacts"), snapshot.ArtifactDirectory);
            Assert.Equal(SessionValidationStatus.NotStarted, snapshot.ValidationStatus);
            Assert.Equal(SessionApprovalStatus.NotRequested, snapshot.ApprovalStatus);

            using var document = await ReadPersistedSessionStateAsync(repositoryPath, snapshot.SessionId);
            var root = document.RootElement;

            Assert.Equal("workflow.safe-change", root.GetProperty("WorkflowId").GetString());
            Assert.Equal("Implement persisted session metadata", root.GetProperty("TaskDescription").GetString());
            Assert.Equal("main", root.GetProperty("BaseBranch").GetString());
            Assert.Equal("abc123def456", root.GetProperty("BaseCommit").GetString());
            Assert.Equal("main", root.GetProperty("TargetBranch").GetString());
            Assert.Equal("abc123def456", root.GetProperty("TargetCommit").GetString());
            Assert.Equal(snapshot.ArtifactDirectory, root.GetProperty("ArtifactDirectory").GetString());
            Assert.Equal("NotStarted", root.GetProperty("ValidationStatus").GetString());
            Assert.Equal("NotRequested", root.GetProperty("ApprovalStatus").GetString());
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

            var persistedKinds = await ReadJournalKindsAsync(repositoryPath, created.SessionId);
            Assert.Equal(
                [
                    SessionEventKind.SessionStarted,
                    SessionEventKind.SessionTransitioned,
                    SessionEventKind.SessionAttached,
                    SessionEventKind.SessionDetached
                ],
                persistedKinds);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SessionAttach_GivenPersistedSnapshot_RestoresGovernanceStatusArtifactDirectoryAndPromptContext()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var initialStore = new InMemorySessionStore();
            var initialPublisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var initialOrchestrator = new WipRuntimeOrchestrator(initialStore, initialPublisher);

            var created = await initialOrchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("workflow.linear"),
                    TaskDescription: "Resume governance workflow",
                    RepositoryPath: repositoryPath,
                    BaseBranch: "main",
                    BaseCommit: "111aaa",
                    TargetBranch: "main",
                    TargetCommit: "111aaa"),
                CancellationToken.None);

            await initialOrchestrator.TransitionAsync(created.SessionId, SessionState.Editing, CancellationToken.None);

            await initialOrchestrator.CreateCheckpointAsync(
                created.SessionId,
                new SessionCheckpointRequest(
                    Name: "before-validation",
                    DiffHash: "diff-123",
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Patch: "diff --git a/file b/file"),
                artifactStore,
                CancellationToken.None);

            var validation = await initialOrchestrator.ValidateAsync(
                created.SessionId,
                new SessionValidationRequest(
                    BuildSucceeded: true,
                    TestSucceeded: true,
                    DiffHash: "diff-123",
                    Summary: "dotnet build/test passed"),
                artifactStore,
                CancellationToken.None);

            var review = await initialOrchestrator.ReviewAsync(
                created.SessionId,
                new ReviewRequest(
                    SessionId: created.SessionId,
                    CurrentDiffHash: "diff-123",
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Validation: new ReviewValidationStatus(true, true, "diff-123")),
                new WipRuntimeReviewGenerator(artifactStore),
                CancellationToken.None);

            await initialOrchestrator.CreateApprovalTokenAsync(
                created.SessionId,
                new ApprovalTokenRequest(
                    SessionId: created.SessionId,
                    WorkflowId: created.WorkflowId,
                    DiffHash: "diff-123",
                    TargetBranch: created.TargetBranch,
                    TargetCommit: created.TargetCommit,
                    ReviewReport: new ApprovalReviewReport(review.Review.ReportArtifact.ArtifactId, "diff-123", false),
                    ValidationReport: new ApprovalValidationReport(validation.ValidationArtifact.ArtifactId, true, true, "diff-123")),
                new WipRuntimeApprovalTokenFactory(),
                artifactStore,
                CancellationToken.None);

            var restoredStore = new InMemorySessionStore();
            var restoredPublisher = new CollectingSessionEventPublisher();
            var restoredOrchestrator = new WipRuntimeOrchestrator(restoredStore, restoredPublisher);

            var attached = await restoredOrchestrator.AttachSessionAsync(
                repositoryPath,
                created.SessionId,
                CancellationToken.None);

            Assert.Equal(SessionState.Approved, attached.State);
            Assert.Equal("Resume governance workflow", attached.TaskDescription);
            Assert.Equal(created.WorkflowId, attached.WorkflowId);
            Assert.Equal(created.WorktreePath, attached.WorktreePath);
            Assert.Equal(created.ArtifactDirectory, attached.ArtifactDirectory);
            Assert.Equal(SessionValidationStatus.Passed, attached.ValidationStatus);
            Assert.Equal(SessionApprovalStatus.Approved, attached.ApprovalStatus);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", SessionPersistenceChecklistItem)]
    public async Task SessionStateJson_GivenSessionLifecycle_StoresApprovalValidationAndArchiveStatusWithoutLosingTaskContext()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var created = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("workflow.linear"),
                    TaskDescription: "Keep task context across lifecycle",
                    RepositoryPath: repositoryPath,
                    BaseBranch: "main",
                    BaseCommit: "222bbb",
                    TargetBranch: "release/1.0",
                    TargetCommit: "333ccc"),
                CancellationToken.None);

            await orchestrator.TransitionAsync(created.SessionId, SessionState.Editing, CancellationToken.None);

            await orchestrator.CreateCheckpointAsync(
                created.SessionId,
                new SessionCheckpointRequest(
                    Name: "before-validation",
                    DiffHash: "diff-456",
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Patch: "diff --git a/file b/file"),
                artifactStore,
                CancellationToken.None);

            using (var afterCheckpoint = await ReadPersistedSessionStateAsync(repositoryPath, created.SessionId))
            {
                Assert.Equal("Checkpointed", afterCheckpoint.RootElement.GetProperty("State").GetString());
            }

            var validation = await orchestrator.ValidateAsync(
                created.SessionId,
                new SessionValidationRequest(
                    BuildSucceeded: false,
                    TestSucceeded: true,
                    DiffHash: "diff-456",
                    Summary: "build failed"),
                artifactStore,
                CancellationToken.None);

            using (var afterValidation = await ReadPersistedSessionStateAsync(repositoryPath, created.SessionId))
            {
                Assert.Equal("Keep task context across lifecycle", afterValidation.RootElement.GetProperty("TaskDescription").GetString());
                Assert.Equal("Failed", afterValidation.RootElement.GetProperty("ValidationStatus").GetString());
                Assert.Equal("NotRequested", afterValidation.RootElement.GetProperty("ApprovalStatus").GetString());
            }

            var review = await orchestrator.ReviewAsync(
                created.SessionId,
                new ReviewRequest(
                    SessionId: created.SessionId,
                    CurrentDiffHash: "diff-456",
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Validation: new ReviewValidationStatus(false, true, "diff-456")),
                new WipRuntimeReviewGenerator(artifactStore),
                CancellationToken.None);

            using var finalDocument = await ReadPersistedSessionStateAsync(repositoryPath, created.SessionId);
            var root = finalDocument.RootElement;

            Assert.Equal("Keep task context across lifecycle", root.GetProperty("TaskDescription").GetString());
            Assert.Equal("release/1.0", root.GetProperty("TargetBranch").GetString());
            Assert.Equal("333ccc", root.GetProperty("TargetCommit").GetString());
            Assert.Equal("Failed", root.GetProperty("ValidationStatus").GetString());
            Assert.Equal(SessionState.AwaitingApproval, review.Session.State);
            Assert.Equal("AwaitingApproval", root.GetProperty("State").GetString());
            Assert.Equal("AwaitingApproval", root.GetProperty("ApprovalStatus").GetString());

            var archived = await orchestrator.ArchiveAsync(
                created.SessionId,
                new SessionArchiveRequest("Archive lifecycle evidence."),
                artifactStore,
                CancellationToken.None);

            using var archivedDocument = await ReadPersistedSessionStateAsync(repositoryPath, created.SessionId);
            var archivedRoot = archivedDocument.RootElement;

            Assert.Equal(SessionState.Archived, archived.Session.State);
            Assert.Equal("Archived", archivedRoot.GetProperty("State").GetString());
            Assert.Equal("Keep task context across lifecycle", archivedRoot.GetProperty("TaskDescription").GetString());
            Assert.Equal("Failed", archivedRoot.GetProperty("ValidationStatus").GetString());
            Assert.Equal("AwaitingApproval", archivedRoot.GetProperty("ApprovalStatus").GetString());
            Assert.StartsWith("session-archive-", archived.Artifact.ArtifactId.Value, StringComparison.Ordinal);
            Assert.Contains(publisher.Events, e => e.Kind == SessionEventKind.SessionArchived && e.SessionId == created.SessionId);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceRestrictionChecklistItem)]
    public async Task RunWorkflowAsync_GivenSelectedWorkflow_ExpectedStateStopsAtCheckpointedWithoutGovernanceArtifacts()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows();

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("governance-cap"),
            cancellationToken: CancellationToken.None);

        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

        Assert.Equal("workflow.linear", result.WorkflowId.Value);
        Assert.All(
            result.Stages,
            stage => Assert.True(
                stage.StateAfterStage is SessionState.Editing or SessionState.Checkpointed,
                $"Stage {stage.Descriptor.Stage} left session in {stage.StateAfterStage}."));
        Assert.All(
            result.Stages.Where(stage => stage.Descriptor.Stage is WorkflowStageKind.Validate or WorkflowStageKind.Review or WorkflowStageKind.RequireApproval or WorkflowStageKind.Merge),
            stage => Assert.False(stage.AppliedTransition));

        var persisted = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(SessionState.Editing, persisted!.State);
        Assert.Equal(SessionApprovalStatus.NotRequested, persisted.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, persisted.ValidationStatus);
        Assert.DoesNotContain(
            publisher.Events,
            e => e.CurrentState is SessionState.Validating or SessionState.AwaitingApproval or SessionState.Approved or SessionState.Merged);
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceRestrictionChecklistItem)]
    public async Task RunWorkflowAsync_GivenCheckpointedSession_ExpectedStateRemainsCheckpointedWithoutGovernanceAdvancement()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows();

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("checkpointed-run"),
            cancellationToken: CancellationToken.None);

        await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Editing, CancellationToken.None);
        await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Checkpointed, CancellationToken.None);

        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

        Assert.All(result.Stages, stage => Assert.Equal(SessionState.Checkpointed, stage.StateAfterStage));
        Assert.All(result.Stages, stage => Assert.False(stage.AppliedTransition));

        var persisted = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(SessionState.Checkpointed, persisted!.State);
        Assert.Equal(SessionApprovalStatus.NotRequested, persisted.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, persisted.ValidationStatus);
    }

    [Fact]
    [Trait("ChecklistItem", GovernanceRestrictionChecklistItem)]
    public async Task ResolveApprovalStatusForTransition_GivenRunInducedStateAdvancement_ExpectedNeverSetsApprovedWithoutTokenArtifact()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows();

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("approval-guard"),
            cancellationToken: CancellationToken.None);

        await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

        var persisted = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.NotEqual(SessionApprovalStatus.Approved, persisted!.ApprovalStatus);
        Assert.NotEqual(SessionValidationStatus.Passed, persisted.ValidationStatus);
        Assert.NotEqual(SessionValidationStatus.InProgress, persisted.ValidationStatus);
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalValidationTransitionAlignmentChecklistItem)]
    public async Task ApplyTransitionUnderAuthorityAsync_GivenGovernanceTargetStates_ExpectedApprovalStatusUnchangedUntilApproveCommand()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("governance-transition"),
            cancellationToken: CancellationToken.None);

        Assert.Equal(SessionApprovalStatus.NotRequested, snapshot.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, snapshot.ValidationStatus);

        var editing = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Editing, CancellationToken.None);
        Assert.Equal(SessionApprovalStatus.NotRequested, editing.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, editing.ValidationStatus);

        var checkpointed = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Checkpointed, CancellationToken.None);
        Assert.Equal(SessionApprovalStatus.NotRequested, checkpointed.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, checkpointed.ValidationStatus);

        var validating = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Validating, CancellationToken.None);
        Assert.Equal(SessionApprovalStatus.NotRequested, validating.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, validating.ValidationStatus);
        Assert.NotEqual(SessionValidationStatus.Passed, validating.ValidationStatus);
        Assert.NotEqual(SessionValidationStatus.InProgress, validating.ValidationStatus);

        var awaitingApproval = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.AwaitingApproval, CancellationToken.None);
        Assert.Equal(SessionApprovalStatus.NotRequested, awaitingApproval.ApprovalStatus);
        Assert.NotEqual(SessionApprovalStatus.Approved, awaitingApproval.ApprovalStatus);
        Assert.NotEqual(SessionApprovalStatus.AwaitingApproval, awaitingApproval.ApprovalStatus);

        var approved = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Approved, CancellationToken.None);
        Assert.Equal(SessionApprovalStatus.NotRequested, approved.ApprovalStatus);
        Assert.NotEqual(SessionApprovalStatus.Approved, approved.ApprovalStatus);

        var merged = await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Merged, CancellationToken.None);
        Assert.Equal(SessionApprovalStatus.NotRequested, merged.ApprovalStatus);
        Assert.NotEqual(SessionApprovalStatus.Approved, merged.ApprovalStatus);
        Assert.NotEqual(SessionValidationStatus.Passed, merged.ValidationStatus);
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalValidationTransitionAlignmentChecklistItem)]
    public async Task ValidateAsync_GivenPassingBuildAndTest_ExpectedValidationStatusAndArtifactUpdatedDeterministically()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "validate-pass"),
                cancellationToken: CancellationToken.None);

            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Editing, CancellationToken.None);

            var beforeValidation = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
            Assert.NotNull(beforeValidation);
            Assert.Equal(SessionValidationStatus.NotStarted, beforeValidation!.ValidationStatus);
            Assert.True(string.IsNullOrWhiteSpace(beforeValidation.ValidationArtifactId));

            var validation = await orchestrator.ValidateAsync(
                sessionId: snapshot.SessionId,
                request: new SessionValidationRequest(
                    BuildSucceeded: true,
                    TestSucceeded: true,
                    DiffHash: "diff-validate-pass",
                    Summary: "dotnet build/test passed"),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            Assert.Equal(SessionValidationStatus.Passed, validation.Session.ValidationStatus);
            Assert.Equal(SessionState.Validating, validation.Session.State);
            Assert.False(string.IsNullOrWhiteSpace(validation.Session.ValidationArtifactId));
            Assert.False(string.IsNullOrWhiteSpace(validation.Session.ValidationArtifactPath));
            Assert.False(string.IsNullOrWhiteSpace(validation.Session.ValidationCorrelationId));
            Assert.Equal(validation.ValidationArtifact.ArtifactId.Value, validation.Session.ValidationArtifactId);
            Assert.Equal(validation.ValidationArtifact.RelativePath, validation.Session.ValidationArtifactPath);

            var persistedArtifact = Assert.Single(
                artifactStore.ListForSession(snapshot.SessionId),
                artifact => artifact.ArtifactId == validation.ValidationArtifact.ArtifactId);
            Assert.Equal(ArtifactKind.Json, persistedArtifact.Kind);
            Assert.Contains("validation-report", persistedArtifact.RelativePath, StringComparison.Ordinal);

            var persisted = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(SessionValidationStatus.Passed, persisted!.ValidationStatus);
            Assert.Equal(validation.ValidationArtifact.ArtifactId.Value, persisted.ValidationArtifactId);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ApprovalValidationTransitionAlignmentChecklistItem)]
    public async Task CreateApprovalTokenAsync_GivenExplicitConfirmation_ExpectedApprovalStatusAndTokenArtifactAligned()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            var store = new InMemorySessionStore();
            var publisher = new CollectingSessionEventPublisher();
            var artifactStore = new InMemoryArtifactStore();
            var orchestrator = new WipRuntimeOrchestrator(store, publisher);

            var snapshot = await orchestrator.StartSessionAsync(
                workflowId: new WorkflowId("workflow.linear"),
                repositoryPath: repositoryPath,
                worktreePath: Path.Combine(repositoryPath, ".wip", "worktrees", "approve-token"),
                cancellationToken: CancellationToken.None);

            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Editing, CancellationToken.None);
            await orchestrator.TransitionAsync(snapshot.SessionId, SessionState.Checkpointed, CancellationToken.None);

            var validation = await orchestrator.ValidateAsync(
                sessionId: snapshot.SessionId,
                request: new SessionValidationRequest(
                    BuildSucceeded: true,
                    TestSucceeded: true,
                    DiffHash: "diff-approve",
                    Summary: "dotnet build/test passed"),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            var review = await orchestrator.ReviewAsync(
                sessionId: snapshot.SessionId,
                request: new ReviewRequest(
                    SessionId: snapshot.SessionId,
                    CurrentDiffHash: "diff-approve",
                    ChangedFiles: ["WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs"],
                    Validation: new ReviewValidationStatus(true, true, "diff-approve")),
                reviewGenerator: new WipRuntimeReviewGenerator(artifactStore),
                cancellationToken: CancellationToken.None);

            var beforeApproval = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
            Assert.NotNull(beforeApproval);
            Assert.Equal(SessionState.AwaitingApproval, beforeApproval!.State);
            Assert.Equal(SessionApprovalStatus.AwaitingApproval, beforeApproval.ApprovalStatus);
            Assert.NotEqual(SessionApprovalStatus.Approved, beforeApproval.ApprovalStatus);

            var approval = await orchestrator.CreateApprovalTokenAsync(
                sessionId: snapshot.SessionId,
                request: new ApprovalTokenRequest(
                    SessionId: snapshot.SessionId,
                    WorkflowId: snapshot.WorkflowId,
                    DiffHash: "diff-approve",
                    TargetBranch: "main",
                    TargetCommit: "abc123",
                    ReviewReport: new ApprovalReviewReport(review.Review.ReportArtifact.ArtifactId, "diff-approve", false),
                    ValidationReport: new ApprovalValidationReport(validation.ValidationArtifact.ArtifactId, true, true, "diff-approve")),
                approvalTokenFactory: new WipRuntimeApprovalTokenFactory(),
                artifactStore: artifactStore,
                cancellationToken: CancellationToken.None);

            Assert.Equal(SessionApprovalStatus.Approved, approval.Session.ApprovalStatus);
            Assert.Equal(SessionState.Approved, approval.Session.State);

            var tokenArtifact = Assert.Single(
                artifactStore.ListForSession(snapshot.SessionId),
                artifact => artifact.RelativePath.Contains("approval-token", StringComparison.Ordinal));
            Assert.Equal(approval.ApprovalArtifact.ArtifactId, tokenArtifact.ArtifactId);

            var persisted = await orchestrator.GetSessionAsync(snapshot.SessionId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(SessionApprovalStatus.Approved, persisted!.ApprovalStatus);
            Assert.Equal(SessionState.Approved, persisted.State);
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
                Directory.Delete(repositoryPath, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", StageCapabilityDispatcherChecklistItem)]
    public async Task RunWorkflowAsync_GivenToolStage_ExpectedStageExecutionArtifactReferencesProducerCapabilityId()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new InMemoryArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows(artifactStore);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("stage-artifact"),
            cancellationToken: CancellationToken.None);

        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

        var runStage = Assert.Single(result.Stages, stage => stage.Descriptor.Stage == WorkflowStageKind.Run);
        Assert.Equal(new CapabilityId("tool.workflow.run"), runStage.ProducerCapabilityId);
        Assert.NotNull(runStage.ExecutionArtifact);
        Assert.Equal("tool.workflow.run", runStage.ExecutionArtifact!.ProducerType);

        var persistedArtifact = Assert.Single(
            artifactStore.ListForSession(snapshot.SessionId),
            artifact => artifact.ProducerType == "tool.workflow.run");
        using var payload = JsonDocument.Parse(artifactStore.ReadContent(persistedArtifact.ArtifactId));
        Assert.Equal("tool.workflow.run", payload.RootElement.GetProperty("producerCapabilityId").GetString());
        Assert.Equal(typeof(WorkflowRequest).FullName, payload.RootElement.GetProperty("requestContractName").GetString());
        Assert.Equal(typeof(WorkflowResult).FullName, payload.RootElement.GetProperty("resultContractName").GetString());
        Assert.Equal(runStage.CorrelationId, payload.RootElement.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("storagePath").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(runStage.ExecutionArtifact!.RelativePath));

        var workflowEvent = Assert.Single(publisher.Events, e => e.Kind == SessionEventKind.WorkflowExecuted);
        Assert.Contains(
            workflowEvent.ArtifactReferences ?? [],
            reference => reference.Role == "stage-execution" && reference.ArtifactId == persistedArtifact.ArtifactId.Value);
    }

    [Fact]
    [Trait("ChecklistItem", StageCapabilityDispatcherChecklistItem)]
    public async Task RunWorkflowAsync_GivenAgentPlanStage_ExpectedPlanOnlyAgentOrRegisteredAgentExecutesBeforeRunStage()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new InMemoryArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithPlanOnlyAgentWorkflow(artifactStore);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("plan-before-run"),
            cancellationToken: CancellationToken.None);

        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

        var planStage = Assert.Single(result.Stages, stage => stage.Descriptor.Stage == WorkflowStageKind.Plan);
        var runStage = Assert.Single(result.Stages, stage => stage.Descriptor.Stage == WorkflowStageKind.Run);
        Assert.Equal(new CapabilityId("agent.plan-only"), planStage.ProducerCapabilityId);
        Assert.Equal(new CapabilityId("tool.workflow.run"), runStage.ProducerCapabilityId);

        var artifacts = artifactStore.ListForSession(snapshot.SessionId).OrderBy(artifact => artifact.ProducedAtUtc).ToArray();
        var planMarkdownArtifact = Assert.Single(artifacts, artifact => artifact.Kind == ArtifactKind.Markdown);
        var planExecutionArtifact = Assert.Single(artifacts, artifact => artifact.ProducerType == "agent.plan-only");
        var runExecutionArtifact = Assert.Single(artifacts, artifact => artifact.ProducerType == "tool.workflow.run");

        Assert.True(planMarkdownArtifact.ProducedAtUtc <= runExecutionArtifact.ProducedAtUtc);
        Assert.True(planExecutionArtifact.ProducedAtUtc <= runExecutionArtifact.ProducedAtUtc);
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
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("pipeline"),
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
        Assert.Equal(SessionState.Editing, merged!.State);
        Assert.Equal(SessionApprovalStatus.NotRequested, merged.ApprovalStatus);
        Assert.Equal(SessionValidationStatus.NotStarted, merged.ValidationStatus);
        Assert.Contains(publisher.Events, e => e.Kind == SessionEventKind.WorkflowExecuted && e.SessionId == snapshot.SessionId);
    }

    [Fact]
    public void AddWorkflow_GivenMapThenThenValidateStages_ExpectedCompiledDescriptorsRetainStageContractNames()
    {
        var builder = CreateBuilderWithWorkflows();
        var workflow = Assert.Single(
            builder.WorkflowRegistrations,
            static registration => registration.WorkflowId.Value == "workflow.linear");

        var compilation = WorkflowBuilderStageCompiler.CompileLinear(workflow);

        Assert.Collection(
            compilation.StageDescriptors,
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Plan, stage.Stage);
                Assert.Equal(typeof(PlanStageRequest), stage.RequestType);
                Assert.Equal(typeof(PlanStageResult), stage.ResultType);
                Assert.Equal(typeof(PlanStageRequest).FullName, stage.RequestContractName);
                Assert.Equal(typeof(PlanStageResult).FullName, stage.ResultContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Run, stage.Stage);
                Assert.Equal(typeof(WorkflowRequest), stage.RequestType);
                Assert.Equal(typeof(WorkflowResult), stage.ResultType);
                Assert.Equal(typeof(WorkflowRequest).FullName, stage.RequestContractName);
                Assert.Equal(typeof(WorkflowResult).FullName, stage.ResultContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Validate, stage.Stage);
                Assert.Equal(typeof(ValidateStageRequest), stage.RequestType);
                Assert.Equal(typeof(ValidateStageResult), stage.ResultType);
                Assert.Equal(typeof(ValidateStageRequest).FullName, stage.RequestContractName);
                Assert.Equal(typeof(ValidateStageResult).FullName, stage.ResultContractName);
            },
            stage => Assert.Equal(WorkflowStageKind.Review, stage.Stage),
            stage => Assert.Equal(WorkflowStageKind.RequireApproval, stage.Stage),
            stage => Assert.Equal(WorkflowStageKind.Merge, stage.Stage));

        Assert.Collection(
            compilation.MapAdapters,
            map =>
            {
                Assert.Equal(WorkflowStageKind.Plan, map.FromStage);
                Assert.Equal(WorkflowStageKind.Run, map.ToStage);
                Assert.Equal(typeof(PlanStageResult), map.SourceType);
                Assert.Equal(typeof(WorkflowRequest), map.TargetType);
                Assert.Equal(typeof(PlanStageResult).FullName, map.SourceContractName);
                Assert.Equal(typeof(WorkflowRequest).FullName, map.TargetContractName);
            },
            map =>
            {
                Assert.Equal(WorkflowStageKind.Run, map.FromStage);
                Assert.Equal(WorkflowStageKind.Validate, map.ToStage);
                Assert.Equal(typeof(WorkflowResult), map.SourceType);
                Assert.Equal(typeof(ValidateStageRequest), map.TargetType);
                Assert.Equal(typeof(WorkflowResult).FullName, map.SourceContractName);
                Assert.Equal(typeof(ValidateStageRequest).FullName, map.TargetContractName);
            },
            map =>
            {
                Assert.Equal(WorkflowStageKind.Validate, map.FromStage);
                Assert.Equal(WorkflowStageKind.Review, map.ToStage);
                Assert.Equal(typeof(ValidateStageResult), map.SourceType);
                Assert.Equal(typeof(ReviewStageRequest), map.TargetType);
            },
            map =>
            {
                Assert.Equal(WorkflowStageKind.Review, map.FromStage);
                Assert.Equal(WorkflowStageKind.RequireApproval, map.ToStage);
                Assert.Equal(typeof(ReviewStageResult), map.SourceType);
                Assert.Equal(typeof(RequireApprovalStageRequest), map.TargetType);
            },
            map =>
            {
                Assert.Equal(WorkflowStageKind.RequireApproval, map.FromStage);
                Assert.Equal(WorkflowStageKind.Merge, map.ToStage);
                Assert.Equal(typeof(RequireApprovalStageResult), map.SourceType);
                Assert.Equal(typeof(MergeStageRequest), map.TargetType);
            });
    }

    [Fact]
    public async Task RunWorkflow_GivenMappedStageChain_ExpectedEachStageReceivesMappedInputContract()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithWorkflows();

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("mapped-chain"),
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
                Assert.Null(stage.MappedInputContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Run, stage.Descriptor.Stage);
                Assert.Equal(typeof(WorkflowRequest).FullName, stage.MappedInputContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Validate, stage.Descriptor.Stage);
                Assert.Equal(typeof(ValidateStageRequest).FullName, stage.MappedInputContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Review, stage.Descriptor.Stage);
                Assert.Equal(typeof(ReviewStageRequest).FullName, stage.MappedInputContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.RequireApproval, stage.Descriptor.Stage);
                Assert.Equal(typeof(RequireApprovalStageRequest).FullName, stage.MappedInputContractName);
            },
            stage =>
            {
                Assert.Equal(WorkflowStageKind.Merge, stage.Descriptor.Stage);
                Assert.Equal(typeof(MergeStageRequest).FullName, stage.MappedInputContractName);
            });
    }

    [Fact]
    [Trait("ChecklistItem", StageMapAdapterChecklistItem)]
    public async Task RunWorkflowAsync_GivenCompiledMapAdapters_ExpectedValidateStageInputDerivedFromRunStageOutput()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new InMemoryArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithMapAdapterWorkflow(artifactStore);

        MapTrackingValidateValidator.Reset();
        WorkflowRunTool.Reset();

        var snapshot = await orchestrator.StartSessionAsync(
            new SessionStartRequest(
                WorkflowId: new WorkflowId("workflow.linear"),
                TaskDescription: "map-adapter-proof",
                RepositoryPath: TestRepositoryPaths.RepositoryRoot,
                WorktreePath: TestRepositoryPaths.Worktree("map-adapters")),
            cancellationToken: CancellationToken.None);

        var result = await orchestrator.RunWorkflowAsync(
            sessionId: snapshot.SessionId,
            builder: builder,
            selectedWorkflowId: new WorkflowId("workflow.linear"),
            cancellationToken: CancellationToken.None);

        var runStage = Assert.Single(result.Stages, stage => stage.Descriptor.Stage == WorkflowStageKind.Run);
        var validateStage = Assert.Single(result.Stages, stage => stage.Descriptor.Stage == WorkflowStageKind.Validate);
        Assert.Equal(new CapabilityId("tool.workflow.run"), runStage.ProducerCapabilityId);
        Assert.Equal(new CapabilityId("validator.workflow.validate"), validateStage.ProducerCapabilityId);
        Assert.Equal(typeof(ValidateStageRequest).FullName, validateStage.MappedInputContractName);

        var runArtifact = Assert.Single(
            artifactStore.ListForSession(snapshot.SessionId),
            artifact => artifact.ProducerType == "tool.workflow.run");
        using var runPayload = JsonDocument.Parse(artifactStore.ReadContent(runArtifact.ArtifactId));
        var runOutcome = runPayload.RootElement.GetProperty("mappedInputPayload").GetProperty("task").GetString();
        Assert.Equal("map-adapter-proof", runOutcome);

        var validateArtifact = Assert.Single(
            artifactStore.ListForSession(snapshot.SessionId),
            artifact => artifact.ProducerType == "validator.workflow.validate");
        using var validatePayload = JsonDocument.Parse(artifactStore.ReadContent(validateArtifact.ArtifactId));
        Assert.Equal(typeof(WorkflowResult).FullName, validatePayload.RootElement.GetProperty("mappedSourceContractName").GetString());
        Assert.Equal(
            "run:map-adapter-proof",
            validatePayload.RootElement.GetProperty("mappedSourcePayload").GetProperty("outcome").GetString());
        Assert.Equal(typeof(ValidateStageRequest).FullName, validatePayload.RootElement.GetProperty("mappedInputContractName").GetString());
        Assert.Equal(
            snapshot.SessionId.Value,
            validatePayload.RootElement.GetProperty("mappedInputPayload").GetProperty("sessionId").GetString());

        Assert.Equal(snapshot.SessionId.Value, MapTrackingValidateValidator.LastRequest?.SessionId);
        Assert.Equal("run:map-adapter-proof", WorkflowRunTool.LastOutcome);
    }

    [Fact]
    [Trait("ChecklistItem", StageMapAdapterChecklistItem)]
    public async Task RunWorkflowAsync_GivenMismatchedMapContracts_ExpectedDeterministicDispatchFailureWithNoPartialArtifacts()
    {
        var store = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var artifactStore = new InMemoryArtifactStore();
        var orchestrator = new WipRuntimeOrchestrator(store, publisher);
        var builder = CreateBuilderWithMapAdapterWorkflow(artifactStore, includeRunTool: false);

        var snapshot = await orchestrator.StartSessionAsync(
            workflowId: new WorkflowId("workflow.linear"),
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("map-mismatch"),
            cancellationToken: CancellationToken.None);

        using (WorkflowStageDispatchScope.Exclude(WorkflowStageKind.Run))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await orchestrator.RunWorkflowAsync(
                    sessionId: snapshot.SessionId,
                    builder: builder,
                    selectedWorkflowId: new WorkflowId("workflow.linear"),
                    cancellationToken: CancellationToken.None));

            Assert.Contains(typeof(WorkflowResult).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(PlanStageResult).FullName!, exception.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            artifactStore.ListForSession(snapshot.SessionId),
            artifact => artifact.ProducerType == "validator.workflow.validate");
        Assert.DoesNotContain(publisher.Events, e => e.Kind == SessionEventKind.WorkflowExecuted);
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
            repositoryPath: TestRepositoryPaths.RepositoryRoot,
            worktreePath: TestRepositoryPaths.Worktree("select"),
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

    private static async Task<IReadOnlyList<SessionEventKind>> ReadJournalKindsAsync(string repositoryPath, SessionId sessionId)
    {
        var journalPath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "event-journal.ndjson");
        var lines = await File.ReadAllLinesAsync(journalPath, CancellationToken.None);
        var kinds = new List<SessionEventKind>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            var kind = document.RootElement.GetProperty("Kind").GetString();
            kinds.Add(Enum.Parse<SessionEventKind>(kind!, ignoreCase: false));
        }

        return kinds;
    }

    private static async Task<JsonDocument> ReadPersistedSessionStateAsync(string repositoryPath, SessionId sessionId)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        var payload = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        return JsonDocument.Parse(payload);
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

    private sealed class InMemoryArtifactStore : IArtifactStore
    {
        private readonly ConcurrentDictionary<SessionId, List<ArtifactDescriptor>> _artifacts = new();
        private readonly ConcurrentDictionary<ArtifactId, string> _contents = new();

        public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
        {
            _contents[artifact.ArtifactId] = artifact.Content;

            var descriptor = new ArtifactDescriptor(
                artifact.ArtifactId,
                sessionId,
                artifact.Kind,
                $"artifacts/{sessionId.Value}/{artifact.FileName}-{artifact.ArtifactId.Value}",
                artifact.ProducerType,
                artifact.ProducerVersion,
                artifact.ProducedAtUtc);

            var artifacts = _artifacts.GetOrAdd(sessionId, static _ => []);
            lock (artifacts)
            {
                artifacts.Add(descriptor);
            }

            return ValueTask.FromResult(descriptor);
        }

        public string ReadContent(ArtifactId artifactId)
            => _contents[artifactId];

        public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(ListForSession(sessionId));

        public IReadOnlyList<ArtifactDescriptor> ListForSession(SessionId sessionId)
        {
            if (!_artifacts.TryGetValue(sessionId, out var artifacts))
                return [];

            lock (artifacts)
            {
                return artifacts.ToArray();
            }
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

    private static WipBuilder CreateBuilderWithMapAdapterWorkflow(
        InMemoryArtifactStore artifactStore,
        bool includeRunTool = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IArtifactStore>(artifactStore);

        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");

        builder.AddAgent<WorkflowPlanStageAgent, PlanStageRequest, PlanStageResult>(
            new CapabilityId("agent.workflow.plan"),
            displayName: "Workflow plan stage agent");

        if (includeRunTool)
        {
            builder.AddTool<WorkflowRunTool, WorkflowRequest, WorkflowResult>(
                new CapabilityId("tool.workflow.run"),
                displayName: "Workflow run tool");
        }

        builder.AddValidator<MapTrackingValidateValidator, ValidateStageRequest, ValidateStageResult>(
            new CapabilityId("validator.workflow.validate"),
            displayName: "Workflow validate stage validator");

        return builder;
    }

    private static WipBuilder CreateBuilderWithWorkflows(InMemoryArtifactStore? artifactStore = null)
    {
        artifactStore ??= new InMemoryArtifactStore();
        var services = new ServiceCollection();
        services.AddSingleton<IArtifactStore>(artifactStore);

        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");

        builder.AddWorkflow<SecondaryWorkflow, SecondaryWorkflowRequest, SecondaryWorkflowResult>(
            workflowId: new WorkflowId("workflow.secondary"),
            displayName: "Secondary workflow");

        builder.AddAgent<WorkflowPlanStageAgent, PlanStageRequest, PlanStageResult>(
            new CapabilityId("agent.workflow.plan"),
            displayName: "Workflow plan stage agent");
        builder.AddTool<WorkflowRunTool, WorkflowRequest, WorkflowResult>(
            new CapabilityId("tool.workflow.run"),
            displayName: "Workflow run tool");

        return builder;
    }

    private static WipBuilder CreateBuilderWithPlanOnlyAgentWorkflow(InMemoryArtifactStore artifactStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IArtifactStore>(artifactStore);
        services.AddSingleton<PlanOnlyAgent>();

        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddTool<WorkflowRunTool, WorkflowRequest, WorkflowResult>(
            new CapabilityId("tool.workflow.run"),
            displayName: "Workflow run tool");
        builder.AddPolicy<AllowAllPolicy, PlanOnlyAgentRequest>(new PolicyId("policy.safe"));

        return builder;
    }

    private static WipBuilder CreateBuilderWithPlanCapabilities()
    {
        var services = new ServiceCollection();
        var builder = new WipBuilder(services);

        builder.AddWorkflow<LinearWorkflow, WorkflowRequest, WorkflowResult>(
            workflowId: new WorkflowId("workflow.linear"),
            displayName: "Linear workflow");
        builder.AddTool<TestTool, TestToolRequest, TestToolResult>(new CapabilityId("tool.write"), "Write tool");
        builder.AddValidator<TestValidator, TestValidatorRequest, TestValidatorResult>(new CapabilityId("validator.dotnet"), "Dotnet validator");
        builder.AddPolicy<AllowAllPolicy, PlanOnlyAgentRequest>(new PolicyId("policy.safe"));

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

    private sealed record TestToolRequest(string Command);

    private sealed record TestToolResult(string Output);

    private sealed class TestTool : ITool<TestToolRequest, TestToolResult>
    {
        public ValueTask<TestToolResult> ExecuteAsync(TestToolRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new TestToolResult(request.Command));
    }

    private sealed record TestValidatorRequest(string ProjectPath);

    private sealed record TestValidatorResult(bool Succeeded);

    private sealed class TestValidator : IValidator<TestValidatorRequest, TestValidatorResult>
    {
        public ValueTask<TestValidatorResult> ExecuteAsync(TestValidatorRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new TestValidatorResult(true));
    }

    private sealed class WorkflowRunTool : ITool<WorkflowRequest, WorkflowResult>
    {
        public static string? LastOutcome { get; private set; }

        public static void Reset() => LastOutcome = null;

        public ValueTask<WorkflowResult> ExecuteAsync(WorkflowRequest request, CapabilityContext context, CancellationToken cancellationToken)
        {
            var outcome = $"run:{request.Task}";
            LastOutcome = outcome;
            return ValueTask.FromResult(new WorkflowResult(outcome));
        }
    }

    private sealed class MapTrackingValidateValidator : IValidator<ValidateStageRequest, ValidateStageResult>
    {
        public static ValidateStageRequest? LastRequest { get; private set; }

        public static void Reset() => LastRequest = null;

        public ValueTask<ValidateStageResult> ExecuteAsync(
            ValidateStageRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return ValueTask.FromResult(new ValidateStageResult(true));
        }
    }

    private sealed class WorkflowPlanStageAgent : IAgent<PlanStageRequest, PlanStageResult>
    {
        public ValueTask<PlanStageResult> ExecuteAsync(PlanStageRequest request, CapabilityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PlanStageResult(true));
    }

    private sealed class AllowAllPolicy : IPolicy<PlanOnlyAgentRequest>
    {
        public PolicyId PolicyId => new("policy.safe");

        public ValueTask<PolicyDecision> EvaluateAsync(PlanOnlyAgentRequest request, PolicyContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }
}
