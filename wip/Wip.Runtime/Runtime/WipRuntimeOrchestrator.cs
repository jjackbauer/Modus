using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Agent.Basic;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Workspaces.Git;
using DotNetValidationRequest = Wip.Validation.DotNet.DotNet.DotNetValidationRequest;
using DotNetValidationValidator = Wip.Validation.DotNet.DotNet.DotNetValidationValidator;

namespace Wip.Runtime.Runtime;

public sealed class WipRuntimeOrchestrator
{
    private const string SessionStateFileName = "session-state.json";
    private const string SessionEventJournalFileName = "event-journal.ndjson";
    private static readonly JsonSerializerOptions SessionStateJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly FrozenDictionary<SessionState, SessionState> NextStates =
        new Dictionary<SessionState, SessionState>
        {
            [SessionState.Created] = SessionState.Editing,
            [SessionState.Editing] = SessionState.Checkpointed,
            [SessionState.Checkpointed] = SessionState.Validating,
            [SessionState.Validating] = SessionState.AwaitingApproval,
            [SessionState.AwaitingApproval] = SessionState.Approved,
            [SessionState.Approved] = SessionState.Merged,
            [SessionState.Merged] = SessionState.Archived
        }.ToFrozenDictionary();

    private readonly ISessionStore _sessionStore;
    private readonly ISessionEventPublisher _eventPublisher;
    private readonly SemaphoreSlim _authorityGate = new(1, 1);
    private SessionId? _attachedSessionId;

    public WipRuntimeOrchestrator(ISessionStore sessionStore, ISessionEventPublisher eventPublisher)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public ValueTask<SessionSnapshot> StartSessionAsync(
        WorkflowId workflowId,
        string repositoryPath,
        string worktreePath,
        CancellationToken cancellationToken)
        => StartSessionAsync(
            new SessionStartRequest(
                WorkflowId: workflowId,
                TaskDescription: string.Empty,
                RepositoryPath: repositoryPath,
                WorktreePath: worktreePath),
            cancellationToken);

    public async ValueTask<SessionSnapshot> StartSessionAsync(
        SessionStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var repositoryPath = request.RepositoryPath;
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(repositoryPath));

        var now = DateTimeOffset.UtcNow;
        var sessionId = new SessionId(Guid.NewGuid().ToString("N"));
        var baseBranch = Coalesce(request.BaseBranch, TryResolveGitValue(repositoryPath, "branch", "--show-current"), "unknown");
        var baseCommit = Coalesce(request.BaseCommit, TryResolveGitValue(repositoryPath, "rev-parse", "HEAD"), "unknown");
        var targetBranch = Coalesce(request.TargetBranch, baseBranch);
        var targetCommit = Coalesce(request.TargetCommit, baseCommit);
        var worktreePath = ResolveWorktreePath(repositoryPath, sessionId, request.WorktreePath);
        var artifactDirectory = ResolveArtifactDirectory(repositoryPath, sessionId, request.ArtifactDirectory);

        var preparedWorkspace = string.IsNullOrWhiteSpace(request.WorktreePath)
            ? TryPrepareGitSessionWorkspace(repositoryPath, sessionId, targetBranch, worktreePath)
            : null;

        if (preparedWorkspace is not null)
        {
            worktreePath = preparedWorkspace.WorktreePath;
            baseBranch = preparedWorkspace.TargetBranch;
            baseCommit = preparedWorkspace.TargetCommit;
            targetBranch = preparedWorkspace.TargetBranch;
            targetCommit = preparedWorkspace.TargetCommit;
        }

        var snapshot = new SessionSnapshot(
            SessionId: sessionId,
            WorkflowId: request.WorkflowId,
            State: SessionState.Created,
            RepositoryPath: repositoryPath,
            WorktreePath: worktreePath,
            UpdatedAtUtc: now,
            TaskDescription: request.TaskDescription.Trim(),
            BaseBranch: baseBranch,
            BaseCommit: baseCommit,
            TargetBranch: targetBranch,
            TargetCommit: targetCommit,
            ArtifactDirectory: artifactDirectory);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(snapshot.WorktreePath);
            Directory.CreateDirectory(snapshot.ArtifactDirectory);
            await _sessionStore.SaveAsync(snapshot, cancellationToken);
            await PersistSessionStateAsync(snapshot, cancellationToken);
            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionStarted,
                    SessionId: snapshot.SessionId,
                    CurrentState: snapshot.State,
                    PreviousState: null,
                    OccurredAtUtc: now,
                    Message: "Session started."),
                snapshot.RepositoryPath,
                cancellationToken);
        }
        finally
        {
            _authorityGate.Release();
        }

        return snapshot;
    }

    public async ValueTask<SessionSnapshot> TransitionAsync(
        SessionId sessionId,
        SessionState targetState,
        CancellationToken cancellationToken)
    {
        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await _sessionStore.LoadAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
            return await ApplyTransitionUnderAuthorityAsync(current, targetState, cancellationToken);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionSnapshot> SelectWorkflowAsync(
        SessionId sessionId,
        WorkflowId workflowId,
        CancellationToken cancellationToken)
    {
        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "select a workflow", SessionState.Created, SessionState.Editing);

            if (current.WorkflowId == workflowId)
                return current;

            var updated = current with
            {
                WorkflowId = workflowId,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            return await PersistSessionSnapshotUpdateUnderAuthorityAsync(updated, cancellationToken);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionPlanResult> PlanAsync(
        SessionId sessionId,
        string task,
        WipBuilder builder,
        PolicyId policyId,
        PlanOnlyAgent agent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(task));

        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(agent);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "plan", SessionState.Created, SessionState.Editing);
            current = await EnsureStateUnderAuthorityAsync(current, SessionState.Editing, cancellationToken);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "plan");

            var context = CreateAgentExecutionContext(current, task, builder, policyId);
            var plan = await agent.ExecuteAsync(
                new PlanOnlyAgentRequest(context),
                new CapabilityContext(current.SessionId, current.WorktreePath),
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.PlanGenerated,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Message: $"Plan generated for session '{current.SessionId}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("plan", "succeeded", current),
                    ArtifactReferences: []),
                current.RepositoryPath,
                cancellationToken);

            return new SessionPlanResult(current, plan);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<WorkflowExecutionResult> RunWorkflowAsync(
        SessionId sessionId,
        WipBuilder builder,
        WorkflowId? selectedWorkflowId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await _sessionStore.LoadAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "run");

            var workflow = ResolveWorkflowSelection(current.WorkflowId, selectedWorkflowId, builder.WorkflowRegistrations);
            var compilation = WorkflowBuilderStageCompiler.CompileLinear(workflow);
            var stageExecutions = new List<WorkflowStageExecution>(compilation.StageDescriptors.Count);

            foreach (var stageDescriptor in compilation.StageDescriptors)
            {
                var targetState = WorkflowStageStateMapper.ToSessionState(stageDescriptor.Stage);
                var appliedTransition = false;

                if (current.State != targetState)
                {
                    current = await AdvanceToStateUnderAuthorityAsync(current, targetState, cancellationToken);
                    appliedTransition = true;
                }

                stageExecutions.Add(new WorkflowStageExecution(stageDescriptor, current.State, appliedTransition)
                {
                    MappedInputContractName = compilation.ResolveMappedInputContractName(stageDescriptor.Stage)
                });
            }

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.WorkflowExecuted,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Message: $"Workflow '{workflow.WorkflowId.Value}' executed for session '{current.SessionId}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("run", "succeeded", current),
                    ArtifactReferences: []),
                current.RepositoryPath,
                cancellationToken);

            return new WorkflowExecutionResult(workflow.WorkflowId, stageExecutions);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionDiffResult> GenerateDiffAsync(
        SessionId sessionId,
        SessionDiffRequest request,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ValidateRequired(request.DiffHash, nameof(request.DiffHash));
        ValidateRequired(request.Patch, nameof(request.Patch));
        ArgumentNullException.ThrowIfNull(request.ChangedFiles);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "generate a diff", SessionState.Editing, SessionState.Checkpointed, SessionState.Validating, SessionState.AwaitingApproval, SessionState.Approved);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "diff");

            var diffArtifact = await artifactStore.SaveAsync(
                current.SessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId($"workspace-diff-{Guid.NewGuid():N}"),
                    kind: ArtifactKind.Patch,
                    fileName: "workspace-diff",
                    content: BuildDiffArtifactContent(request),
                    producerType: "Wip.Runtime",
                    producerVersion: "1.0.0",
                    producedAtUtc: request.ProducedAtUtc ?? DateTimeOffset.UtcNow),
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.DiffGenerated,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: request.ProducedAtUtc ?? DateTimeOffset.UtcNow,
                    Message: $"Diff generated for session '{current.SessionId}' with hash '{request.DiffHash}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("diff", "succeeded", current),
                    ArtifactReferences: [CreateArtifactReference("diff", diffArtifact)]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionDiffResult(current, request.DiffHash, request.ChangedFiles.ToArray(), diffArtifact);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionCheckpointResult> CreateCheckpointAsync(
        SessionId sessionId,
        SessionCheckpointRequest request,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ValidateRequired(request.Name, nameof(request.Name));
        ValidateRequired(request.DiffHash, nameof(request.DiffHash));
        ValidateRequired(request.Patch, nameof(request.Patch));
        ArgumentNullException.ThrowIfNull(request.ChangedFiles);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "create a checkpoint", SessionState.Editing, SessionState.Checkpointed);
            current = await EnsureStateUnderAuthorityAsync(current, SessionState.Checkpointed, cancellationToken);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "checkpoint");

            var producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
            var artifact = await artifactStore.SaveAsync(
                current.SessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId($"checkpoint-{Guid.NewGuid():N}"),
                    kind: ArtifactKind.Json,
                    fileName: "checkpoint",
                    content: JsonSerializer.Serialize(new
                    {
                        request.Name,
                        request.DiffHash,
                        ChangedFiles = request.ChangedFiles,
                        request.Patch,
                        ProducedAtUtc = producedAtUtc
                    }),
                    producerType: "Wip.Runtime",
                    producerVersion: "1.0.0",
                    producedAtUtc: producedAtUtc),
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.CheckpointGenerated,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: producedAtUtc,
                    Message: $"Checkpoint '{request.Name}' generated for session '{current.SessionId}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("checkpoint", "succeeded", current),
                    ArtifactReferences: [CreateArtifactReference("checkpoint", artifact)]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionCheckpointResult(current, artifact);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionValidationResult> ValidateAsync(
        SessionId sessionId,
        SessionValidationRequest request,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ValidateRequired(request.Summary, nameof(request.Summary));

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "validate", SessionState.Editing, SessionState.Checkpointed, SessionState.Validating);
            current = await AdvanceToStateUnderAuthorityAsync(current, SessionState.Validating, cancellationToken);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "validate");

            ArtifactDescriptor artifact;
            bool succeeded;
            string? diffHash;
            DateTimeOffset producedAtUtc;

            if (request.RuntimeValidation is not null)
            {
                var validator = new DotNetValidationValidator(artifactStore, new WipWorkspaceProviderGit());
                var validation = await validator.ExecuteAsync(
                    new DotNetValidationRequest(
                        BuildProjectPath: request.RuntimeValidation.BuildProjectPath,
                        TestProjectPath: request.RuntimeValidation.TestProjectPath,
                        RepositoryPath: current.RepositoryPath,
                        DiffHashOverride: request.DiffHash,
                        CommandTimeout: request.RuntimeValidation.CommandTimeout,
                        DotNetExecutablePath: request.RuntimeValidation.DotNetExecutablePath),
                    new CapabilityContext(current.SessionId, current.WorktreePath),
                    cancellationToken);

                artifact = validation.ReportArtifact;
                succeeded = validation.Succeeded;
                diffHash = validation.Report.DiffHash;
                producedAtUtc = validation.Report.ProducedAtUtc;
            }
            else
            {
                producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
                artifact = await artifactStore.SaveAsync(
                    current.SessionId,
                    new ArtifactContent(
                        artifactId: new ArtifactId($"validation-report-{Guid.NewGuid():N}"),
                        kind: ArtifactKind.Json,
                        fileName: "validation-report",
                        content: BuildValidationReportContent(request, producedAtUtc),
                        producerType: "Wip.Runtime",
                        producerVersion: "1.0.0",
                        producedAtUtc: producedAtUtc),
                    cancellationToken);
                succeeded = request.Succeeded;
                diffHash = request.DiffHash;
            }

            current = await PersistSessionSnapshotUpdateUnderAuthorityAsync(
                current with
                {
                    UpdatedAtUtc = producedAtUtc,
                    ValidationStatus = succeeded ? SessionValidationStatus.Passed : SessionValidationStatus.Failed,
                    ValidationArtifactId = artifact.ArtifactId.Value,
                    ValidationArtifactPath = artifact.RelativePath,
                    ValidationCorrelationId = correlationId
                },
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.ValidationCompleted,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: producedAtUtc,
                    Message: $"Validation completed for session '{current.SessionId}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("validate", "succeeded", current),
                    ArtifactReferences: [CreateArtifactReference("validation-report", artifact)]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionValidationResult(current, artifact, succeeded, diffHash);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    private static string BuildValidationReportContent(SessionValidationRequest request, DateTimeOffset producedAtUtc)
    {
        var orderedResults = (request.CommandResults ?? Array.Empty<SessionValidationCommandResult>())
            .OrderBy(static result => result.StartedAtUtc)
            .ThenBy(static result => result.CompletedAtUtc)
            .ThenBy(static result => result.Command, StringComparer.Ordinal)
            .ToArray();

        var commandResults = orderedResults
            .Select((result, index) => new
            {
                Sequence = index + 1,
                result.Command,
                result.ExitCode,
                result.TimedOut,
                result.Succeeded,
                result.StartedAtUtc,
                result.CompletedAtUtc,
                StandardOutputSummary = SummarizeCommandEvidence(result.StandardOutput),
                StandardErrorSummary = SummarizeCommandEvidence(result.StandardError),
                result.StandardOutput,
                result.StandardError
            })
            .ToArray();

        var commandRollup = commandResults
            .Select(result => new
            {
                result.Sequence,
                result.Command,
                result.Succeeded,
                result.ExitCode,
                result.TimedOut,
                result.StandardOutputSummary,
                result.StandardErrorSummary
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            request.BuildSucceeded,
            request.TestSucceeded,
            request.DiffHash,
            request.Summary,
            CommandResults = commandResults,
            CommandRollup = commandRollup,
            ProducedAtUtc = producedAtUtc
        });
    }

    public async ValueTask<SessionReviewResult> ReviewAsync(
        SessionId sessionId,
        ReviewRequest request,
        WipRuntimeReviewGenerator reviewGenerator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reviewGenerator);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "review", SessionState.Validating, SessionState.AwaitingApproval);
            current = await EnsureStateUnderAuthorityAsync(current, SessionState.AwaitingApproval, cancellationToken);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "review");

            if (request.SessionId != current.SessionId)
            {
                throw new InvalidOperationException(
                    $"Review request session '{request.SessionId}' does not match orchestrator session '{current.SessionId}'.");
            }

            var review = await reviewGenerator.ReviewAsync(request, cancellationToken);
            current = await PersistSessionSnapshotUpdateUnderAuthorityAsync(
                current with
                {
                    UpdatedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow,
                    ApprovalStatus = SessionApprovalStatus.AwaitingApproval
                },
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.ReviewGenerated,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: request.ProducedAtUtc ?? DateTimeOffset.UtcNow,
                    Message: $"Review generated for session '{current.SessionId}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("review", "succeeded", current),
                    ArtifactReferences: [CreateArtifactReference("review-report", review.ReportArtifact)]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionReviewResult(current, review);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionApprovalTokenResult> CreateApprovalTokenAsync(
        SessionId sessionId,
        ApprovalTokenRequest request,
        WipRuntimeApprovalTokenFactory approvalTokenFactory,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(approvalTokenFactory);
        ArgumentNullException.ThrowIfNull(artifactStore);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(current, "create an approval token", SessionState.AwaitingApproval, SessionState.Approved);
            current = await EnsureStateUnderAuthorityAsync(current, SessionState.Approved, cancellationToken);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "approve");

            if (request.SessionId != current.SessionId)
            {
                throw new InvalidOperationException(
                    $"Approval token request session '{request.SessionId}' does not match orchestrator session '{current.SessionId}'.");
            }

            var approvalToken = approvalTokenFactory.Create(request);
            var artifact = await artifactStore.SaveAsync(
                current.SessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId($"approval-token-{Guid.NewGuid():N}"),
                    kind: ArtifactKind.Json,
                    fileName: "approval-token",
                    content: JsonSerializer.Serialize(approvalToken),
                    producerType: "Wip.Runtime",
                    producerVersion: "1.0.0",
                    producedAtUtc: approvalToken.ProducedAtUtc),
                cancellationToken);

            current = await PersistSessionSnapshotUpdateUnderAuthorityAsync(
                current with
                {
                    UpdatedAtUtc = approvalToken.ProducedAtUtc,
                    ApprovalStatus = SessionApprovalStatus.Approved
                },
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.ApprovalTokenCreated,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: approvalToken.ProducedAtUtc,
                    Message: $"Approval token created for session '{current.SessionId}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("approve", "succeeded", current),
                    ArtifactReferences:
                    [
                        CreateArtifactReference("approval-token", artifact),
                        CreateArtifactReference("validation-evidence", request.ValidationReport.ReportArtifactId, string.Empty, ArtifactKind.Json),
                        CreateArtifactReference("review-evidence", request.ReviewReport.ReportArtifactId, string.Empty, ArtifactKind.Markdown)
                    ]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionApprovalTokenResult(current, approvalToken, artifact);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionMergeResult> MergeAsync(
        SessionId sessionId,
        SessionMergeRequest request,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ValidateRequired(request.TargetBranch, nameof(request.TargetBranch));
        ValidateRequired(request.TargetCommit, nameof(request.TargetCommit));
        ValidateRequired(request.DiffHash, nameof(request.DiffHash));
        ValidateRequired(request.Summary, nameof(request.Summary));

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "merge");
            var mergeDecisionReferences = await CollectMergeDecisionArtifactReferencesAsync(current.SessionId, artifactStore, cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.MergeAttempted,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: producedAtUtc,
                    Message: $"Merge attempted for session '{current.SessionId}' into '{request.TargetBranch}'.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("merge", "attempted", current),
                    ArtifactReferences: mergeDecisionReferences),
                current.RepositoryPath,
                cancellationToken);

            try
            {
                EnsureCurrentState(current, "merge", SessionState.Approved);

                var artifact = await artifactStore.SaveAsync(
                    current.SessionId,
                    new ArtifactContent(
                        artifactId: new ArtifactId($"merge-result-{Guid.NewGuid():N}"),
                        kind: ArtifactKind.Json,
                        fileName: "merge-result",
                        content: JsonSerializer.Serialize(new
                        {
                            request.TargetBranch,
                            request.TargetCommit,
                            request.DiffHash,
                            request.Summary,
                            ProducedAtUtc = producedAtUtc
                        }),
                        producerType: "Wip.Runtime",
                        producerVersion: "1.0.0",
                        producedAtUtc: producedAtUtc),
                    cancellationToken);

                current = await ApplyTransitionUnderAuthorityAsync(current, SessionState.Merged, cancellationToken);
                await PublishAndJournalSessionEventAsync(
                    new SessionEvent(
                        Kind: SessionEventKind.MergeSucceeded,
                        SessionId: current.SessionId,
                        CurrentState: current.State,
                        PreviousState: null,
                        OccurredAtUtc: producedAtUtc,
                        Message: $"Merge succeeded for session '{current.SessionId}' into '{request.TargetBranch}'.",
                        CorrelationId: correlationId,
                        Diagnostics: BuildGovernanceDiagnostics("merge", "succeeded", current),
                        ArtifactReferences: [CreateArtifactReference("merge-result", artifact)]),
                    current.RepositoryPath,
                    cancellationToken);

                await PublishAndJournalSessionEventAsync(
                    new SessionEvent(
                        Kind: SessionEventKind.MergeCompleted,
                        SessionId: current.SessionId,
                        CurrentState: current.State,
                        PreviousState: null,
                        OccurredAtUtc: producedAtUtc,
                        Message: $"Merge completed for session '{current.SessionId}' into '{request.TargetBranch}'.",
                        CorrelationId: correlationId,
                        Diagnostics: BuildGovernanceDiagnostics("merge", "completed", current),
                        ArtifactReferences: [CreateArtifactReference("merge-result", artifact)]),
                    current.RepositoryPath,
                    cancellationToken);

                return new SessionMergeResult(current, artifact);
            }
            catch (Exception ex)
            {
                await PublishAndJournalSessionEventAsync(
                    new SessionEvent(
                        Kind: SessionEventKind.MergeFailed,
                        SessionId: current.SessionId,
                        CurrentState: current.State,
                        PreviousState: null,
                        OccurredAtUtc: DateTimeOffset.UtcNow,
                        Message: $"Merge failed for session '{current.SessionId}': {ex.Message}",
                        CorrelationId: correlationId,
                        Diagnostics:
                        [
                            ..BuildGovernanceDiagnostics("merge", "failed", current),
                            new SessionEventDiagnostic("failureReason", ex.Message)
                        ],
                        ArtifactReferences: mergeDecisionReferences),
                    current.RepositoryPath,
                    cancellationToken);

                throw;
            }
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionArtifactResult> ArchiveAsync(
        SessionId sessionId,
        SessionArchiveRequest request,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ValidateRequired(request.Reason, nameof(request.Reason));

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(
                current,
                "archive",
                SessionState.Created,
                SessionState.Editing,
                SessionState.Checkpointed,
                SessionState.Validating,
                SessionState.AwaitingApproval,
                SessionState.Approved,
                SessionState.Merged,
                SessionState.Archived);

            if (current.State != SessionState.Archived)
            {
                current = await ApplyTerminalTransitionUnderAuthorityAsync(
                    current,
                    SessionState.Archived,
                    cancellationToken);
            }
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "archive");

            var producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
            var cleanupOutcome = request.PolicyMode == SessionArchivePolicyMode.Cleanup
                ? CleanupSessionWorktree(current)
                : SessionWorktreeCleanupOutcome.MarkOnly(current.WorktreePath);
            var artifact = await artifactStore.SaveAsync(
                current.SessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId($"session-archive-{Guid.NewGuid():N}"),
                    kind: ArtifactKind.Json,
                    fileName: "session-archive",
                    content: JsonSerializer.Serialize(
                        new
                        {
                            request.Reason,
                            request.PolicyMode,
                            Cleanup = new
                            {
                                cleanupOutcome.Attempted,
                                cleanupOutcome.Succeeded,
                                cleanupOutcome.WorktreePath,
                                cleanupOutcome.Details
                            },
                            ProducedAtUtc = producedAtUtc
                        }),
                    producerType: "Wip.Runtime",
                    producerVersion: "1.0.0",
                    producedAtUtc: producedAtUtc),
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionArchived,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: producedAtUtc,
                    Message: $"Session '{current.SessionId}' archived.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("archive", "succeeded", current),
                    ArtifactReferences: [CreateArtifactReference("archive", artifact)]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionArtifactResult(current, artifact);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionArtifactResult> AbortAsync(
        SessionId sessionId,
        SessionAbortRequest request,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifactStore);
        ValidateRequired(request.Reason, nameof(request.Reason));

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadSessionUnderAuthorityAsync(sessionId, cancellationToken);
            EnsureCurrentState(
                current,
                "abort",
                SessionState.Created,
                SessionState.Editing,
                SessionState.Checkpointed,
                SessionState.Validating,
                SessionState.AwaitingApproval,
                SessionState.Approved,
                SessionState.Aborted);

            if (current.State != SessionState.Aborted)
            {
                current = await ApplyTerminalTransitionUnderAuthorityAsync(
                    current,
                    SessionState.Aborted,
                    cancellationToken);
            }
            var correlationId = CreateGovernanceCorrelationId(current.SessionId, "abort");

            var producedAtUtc = request.ProducedAtUtc ?? DateTimeOffset.UtcNow;
            var artifact = await artifactStore.SaveAsync(
                current.SessionId,
                new ArtifactContent(
                    artifactId: new ArtifactId($"session-abort-{Guid.NewGuid():N}"),
                    kind: ArtifactKind.Json,
                    fileName: "session-abort",
                    content: JsonSerializer.Serialize(new { request.Reason, ProducedAtUtc = producedAtUtc }),
                    producerType: "Wip.Runtime",
                    producerVersion: "1.0.0",
                    producedAtUtc: producedAtUtc),
                cancellationToken);

            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionAborted,
                    SessionId: current.SessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: producedAtUtc,
                    Message: $"Session '{current.SessionId}' aborted.",
                    CorrelationId: correlationId,
                    Diagnostics: BuildGovernanceDiagnostics("abort", "succeeded", current),
                    ArtifactReferences: [CreateArtifactReference("abort", artifact)]),
                current.RepositoryPath,
                cancellationToken);

            return new SessionArtifactResult(current, artifact);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<SessionSnapshot> AttachSessionAsync(
        string repositoryPath,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(repositoryPath));

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var restored = await _sessionStore.LoadAsync(sessionId, cancellationToken)
                ?? await LoadPersistedSessionStateAsync(repositoryPath, sessionId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Session '{sessionId}' was not found at '{BuildSessionStatePath(repositoryPath, sessionId)}'.");

            await _sessionStore.SaveAsync(restored, cancellationToken);

            _attachedSessionId = sessionId;
            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionAttached,
                    SessionId: sessionId,
                    CurrentState: restored.State,
                    PreviousState: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Message: "Session attached."),
                restored.RepositoryPath,
                cancellationToken);

            return restored;
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public async ValueTask<bool> DetachSessionAsync(CancellationToken cancellationToken)
    {
        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            if (_attachedSessionId is null)
                return false;

            var sessionId = _attachedSessionId.Value;
            var current = await _sessionStore.LoadAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Attached session '{sessionId}' was not found.");

            _attachedSessionId = null;
            await PublishAndJournalSessionEventAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionDetached,
                    SessionId: sessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Message: "Session detached."),
                current.RepositoryPath,
                cancellationToken);

            return true;
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    public ValueTask<SessionSnapshot?> GetSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
        => _sessionStore.LoadAsync(sessionId, cancellationToken);

    public async ValueTask<AgentExecutionContext> CreateAgentExecutionContextAsync(
        SessionId sessionId,
        string task,
        WipBuilder builder,
        PolicyId policyId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(task));

        ArgumentNullException.ThrowIfNull(builder);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _sessionStore.LoadAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

            return CreateAgentExecutionContext(snapshot, task, builder, policyId);
        }
        finally
        {
            _authorityGate.Release();
        }
    }

    private static async ValueTask PersistSessionStateAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var statePath = BuildSessionStatePath(snapshot.RepositoryPath, snapshot.SessionId);
        var stateDirectory = Path.GetDirectoryName(statePath)
            ?? throw new InvalidOperationException($"Failed to resolve session state directory for '{statePath}'.");

        Directory.CreateDirectory(stateDirectory);

        var payload = new PersistedSessionState(
            SessionId: snapshot.SessionId.Value,
            WorkflowId: snapshot.WorkflowId.Value,
            State: snapshot.State,
            RepositoryPath: snapshot.RepositoryPath,
            WorktreePath: snapshot.WorktreePath,
            UpdatedAtUtc: snapshot.UpdatedAtUtc,
            TaskDescription: snapshot.TaskDescription,
            BaseBranch: snapshot.BaseBranch,
            BaseCommit: snapshot.BaseCommit,
            TargetBranch: snapshot.TargetBranch,
            TargetCommit: snapshot.TargetCommit,
            ArtifactDirectory: snapshot.ArtifactDirectory,
            ValidationArtifactId: snapshot.ValidationArtifactId,
            ValidationArtifactPath: snapshot.ValidationArtifactPath,
            ValidationCorrelationId: snapshot.ValidationCorrelationId,
            ValidationStatus: snapshot.ValidationStatus,
            ApprovalStatus: snapshot.ApprovalStatus);

        await using var stream = File.Create(statePath);
        await JsonSerializer.SerializeAsync(stream, payload, SessionStateJsonOptions, cancellationToken);
    }

    private static async ValueTask<SessionSnapshot?> LoadPersistedSessionStateAsync(
        string repositoryPath,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        var statePath = BuildSessionStatePath(repositoryPath, sessionId);
        if (!File.Exists(statePath))
            return null;

        await using var stream = File.OpenRead(statePath);
        var restored = await JsonSerializer.DeserializeAsync<PersistedSessionState>(stream, SessionStateJsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Session state file '{statePath}' is empty or invalid.");

        if (!string.Equals(restored.SessionId, sessionId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Session state file '{statePath}' does not match requested session '{sessionId}'.");
        }

        return new SessionSnapshot(
            SessionId: new SessionId(restored.SessionId),
            WorkflowId: new WorkflowId(restored.WorkflowId),
            State: restored.State,
            RepositoryPath: restored.RepositoryPath,
            WorktreePath: restored.WorktreePath,
            UpdatedAtUtc: restored.UpdatedAtUtc,
            TaskDescription: restored.TaskDescription ?? string.Empty,
            BaseBranch: restored.BaseBranch ?? string.Empty,
            BaseCommit: restored.BaseCommit ?? string.Empty,
            TargetBranch: restored.TargetBranch ?? string.Empty,
            TargetCommit: restored.TargetCommit ?? string.Empty,
            ArtifactDirectory: restored.ArtifactDirectory ?? string.Empty,
            ValidationArtifactId: restored.ValidationArtifactId ?? string.Empty,
            ValidationArtifactPath: restored.ValidationArtifactPath ?? string.Empty,
            ValidationCorrelationId: restored.ValidationCorrelationId ?? string.Empty,
            ValidationStatus: restored.ValidationStatus,
            ApprovalStatus: restored.ApprovalStatus);
    }

    private static string BuildSessionStatePath(string repositoryPath, SessionId sessionId)
        => Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, SessionStateFileName);

    private static string BuildSessionEventJournalPath(string repositoryPath, SessionId sessionId)
        => Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, SessionEventJournalFileName);

    private static SessionState ResolveExpectedNext(SessionState state)
    {
        if (!NextStates.TryGetValue(state, out var expectedNext))
            throw new InvalidOperationException($"Session state '{state}' is terminal and cannot transition.");

        return expectedNext;
    }

    private static AgentExecutionContext CreateAgentExecutionContext(
        SessionSnapshot snapshot,
        string task,
        WipBuilder builder,
        PolicyId policyId)
    {
        if (!builder.PolicyRegistrations.Any(registration => registration.PolicyId == policyId))
        {
            throw new InvalidOperationException(
                $"Policy '{policyId.Value}' is not registered in the active builder.");
        }

        var tools = builder.CapabilityDescriptors
            .Where(descriptor => descriptor.Kind == CapabilityKind.Tool)
            .OrderBy(descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .ToArray();

        var validators = builder.CapabilityDescriptors
            .Where(descriptor => descriptor.Kind == CapabilityKind.Validator)
            .OrderBy(descriptor => descriptor.CapabilityId.Value, StringComparer.Ordinal)
            .ToArray();

        return new AgentExecutionContext(
            SessionId: snapshot.SessionId,
            WorkflowId: snapshot.WorkflowId,
            Task: task,
            RepositoryPath: snapshot.RepositoryPath,
            WorktreePath: snapshot.WorktreePath,
            Tools: tools,
            Validators: validators,
            PolicyId: policyId);
    }

    private async ValueTask<SessionSnapshot> LoadSessionUnderAuthorityAsync(SessionId sessionId, CancellationToken cancellationToken)
        => await _sessionStore.LoadAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

    private static void EnsureCurrentState(SessionSnapshot snapshot, string operationName, params SessionState[] allowedStates)
    {
        if (allowedStates.Contains(snapshot.State))
            return;

        var allowed = string.Join(", ", allowedStates.Select(static state => state.ToString()));
        throw new InvalidOperationException(
            $"Session '{snapshot.SessionId}' cannot {operationName} from state '{snapshot.State}'. Allowed states: {allowed}.");
    }

    private async ValueTask<SessionSnapshot> EnsureStateUnderAuthorityAsync(
        SessionSnapshot current,
        SessionState requiredState,
        CancellationToken cancellationToken)
    {
        if (current.State == requiredState)
            return current;

        return await ApplyTransitionUnderAuthorityAsync(current, requiredState, cancellationToken);
    }

    private async ValueTask<SessionSnapshot> AdvanceToStateUnderAuthorityAsync(
        SessionSnapshot current,
        SessionState targetState,
        CancellationToken cancellationToken)
    {
        while (current.State != targetState)
        {
            var expectedNext = ResolveExpectedNext(current.State);
            current = await ApplyTransitionUnderAuthorityAsync(current, expectedNext, cancellationToken);
        }

        return current;
    }

    private async ValueTask<SessionSnapshot> PersistSessionSnapshotUpdateUnderAuthorityAsync(
        SessionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await _sessionStore.SaveAsync(snapshot, cancellationToken);
        await PersistSessionStateAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static string BuildDiffArtifactContent(SessionDiffRequest request)
    {
        var lines = new List<string>
        {
            $"# Diff Hash: {request.DiffHash}",
            "# Changed Files:"
        };

        if (request.ChangedFiles.Count == 0)
        {
            lines.Add("# - (none)");
        }
        else
        {
            lines.AddRange(request.ChangedFiles.Select(static changedFile => $"# - {changedFile}"));
        }

        lines.Add(request.Patch);
        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateGovernanceCorrelationId(SessionId sessionId, string command)
        => $"{sessionId.Value}:{command}:{Guid.NewGuid():N}";

    private static IReadOnlyList<SessionEventDiagnostic> BuildGovernanceDiagnostics(
        string command,
        string outcome,
        SessionSnapshot snapshot)
        =>
        [
            new SessionEventDiagnostic("command", command),
            new SessionEventDiagnostic("outcome", outcome),
            new SessionEventDiagnostic("sessionState", snapshot.State.ToString()),
            new SessionEventDiagnostic("workflowId", snapshot.WorkflowId.Value)
        ];

    private static SessionEventArtifactReference CreateArtifactReference(string role, ArtifactDescriptor descriptor)
        => new(
            Role: role,
            ArtifactId: descriptor.ArtifactId.Value,
            ArtifactPath: descriptor.RelativePath,
            ArtifactKind: descriptor.Kind);

    private static SessionEventArtifactReference CreateArtifactReference(string role, ArtifactId artifactId, string artifactPath, ArtifactKind artifactKind)
        => new(
            Role: role,
            ArtifactId: artifactId.Value,
            ArtifactPath: artifactPath,
            ArtifactKind: artifactKind);

    private static async ValueTask<IReadOnlyList<SessionEventArtifactReference>> CollectMergeDecisionArtifactReferencesAsync(
        SessionId sessionId,
        IArtifactStore artifactStore,
        CancellationToken cancellationToken)
    {
        var descriptors = await artifactStore.ListAsync(sessionId, cancellationToken);
        var selected = descriptors
            .Where(descriptor =>
                descriptor.ArtifactId.Value.StartsWith("validation-report-", StringComparison.Ordinal)
                || descriptor.ArtifactId.Value.StartsWith("review-report-", StringComparison.Ordinal)
                || descriptor.ArtifactId.Value.StartsWith("approval-token-", StringComparison.Ordinal))
            .OrderByDescending(static descriptor => descriptor.ProducedAtUtc)
            .ThenBy(static descriptor => descriptor.ArtifactId.Value, StringComparer.Ordinal)
            .ToArray();

        var validation = selected.FirstOrDefault(descriptor => descriptor.ArtifactId.Value.StartsWith("validation-report-", StringComparison.Ordinal));
        var review = selected.FirstOrDefault(descriptor => descriptor.ArtifactId.Value.StartsWith("review-report-", StringComparison.Ordinal));
        var approval = selected.FirstOrDefault(descriptor => descriptor.ArtifactId.Value.StartsWith("approval-token-", StringComparison.Ordinal));

        var references = new List<SessionEventArtifactReference>(capacity: 3);
        if (validation is not null)
        {
            references.Add(CreateArtifactReference("validation-evidence", validation));
        }

        if (review is not null)
        {
            references.Add(CreateArtifactReference("review-evidence", review));
        }

        if (approval is not null)
        {
            references.Add(CreateArtifactReference("approval-evidence", approval));
        }

        return references;
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }

    private static string SummarizeCommandEvidence(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .ToArray();

        if (lines.Length == 0)
            return string.Empty;

        var summary = string.Join(" | ", lines);
        return summary.Length <= 240 ? summary : summary[..240];
    }

    private async ValueTask<SessionSnapshot> ApplyTransitionUnderAuthorityAsync(
        SessionSnapshot current,
        SessionState targetState,
        CancellationToken cancellationToken)
    {
        var expectedNext = ResolveExpectedNext(current.State);
        if (targetState != expectedNext)
        {
            throw new InvalidOperationException(
                $"Invalid transition for session '{current.SessionId}': {current.State} -> {targetState}. Expected next state is {expectedNext}.");
        }

        var updated = current with
        {
            State = targetState,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ValidationStatus = ResolveValidationStatusForTransition(current.ValidationStatus, targetState),
            ApprovalStatus = ResolveApprovalStatusForTransition(current.ApprovalStatus, targetState)
        };

        await PersistSessionSnapshotUpdateUnderAuthorityAsync(updated, cancellationToken);
        await PublishAndJournalSessionEventAsync(
            new SessionEvent(
                Kind: SessionEventKind.SessionTransitioned,
                SessionId: current.SessionId,
                CurrentState: updated.State,
                PreviousState: current.State,
                OccurredAtUtc: updated.UpdatedAtUtc,
                Message: $"Session transitioned from {current.State} to {updated.State}."),
            updated.RepositoryPath,
            cancellationToken);

        return updated;
    }

    private async ValueTask<SessionSnapshot> ApplyTerminalTransitionUnderAuthorityAsync(
        SessionSnapshot current,
        SessionState targetState,
        CancellationToken cancellationToken)
    {
        var updated = current with
        {
            State = targetState,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ApprovalStatus = targetState == SessionState.Aborted ? SessionApprovalStatus.NotRequested : current.ApprovalStatus
        };

        await PersistSessionSnapshotUpdateUnderAuthorityAsync(updated, cancellationToken);
        await PublishAndJournalSessionEventAsync(
            new SessionEvent(
                Kind: SessionEventKind.SessionTransitioned,
                SessionId: current.SessionId,
                CurrentState: updated.State,
                PreviousState: current.State,
                OccurredAtUtc: updated.UpdatedAtUtc,
                Message: $"Session transitioned from {current.State} to {updated.State}."),
            updated.RepositoryPath,
            cancellationToken);

        return updated;
    }

    private static string ResolveWorktreePath(string repositoryPath, SessionId sessionId, string? worktreePath)
        => string.IsNullOrWhiteSpace(worktreePath)
            ? Path.Combine(repositoryPath, ".wip", "worktrees", sessionId.Value)
            : worktreePath;

    private static SessionWorktreeCleanupOutcome CleanupSessionWorktree(SessionSnapshot snapshot)
    {
        var normalizedRepositoryPath = Path.GetFullPath(snapshot.RepositoryPath);
        var normalizedWorktreePath = Path.GetFullPath(snapshot.WorktreePath);

        if (string.Equals(normalizedRepositoryPath, normalizedWorktreePath, StringComparison.OrdinalIgnoreCase))
        {
            return new SessionWorktreeCleanupOutcome(
                Attempted: true,
                Succeeded: false,
                WorktreePath: normalizedWorktreePath,
                Details: "Cleanup refused because worktree path matches repository path.");
        }

        if (!Directory.Exists(normalizedWorktreePath))
        {
            return new SessionWorktreeCleanupOutcome(
                Attempted: true,
                Succeeded: true,
                WorktreePath: normalizedWorktreePath,
                Details: "Worktree path already absent.");
        }

        try
        {
            var gitCleanup = TryRunGitCommand(snapshot.RepositoryPath, "worktree", "remove", "--force", normalizedWorktreePath);
            if (gitCleanup.ExitCode != 0 && Directory.Exists(normalizedWorktreePath))
            {
                Directory.Delete(normalizedWorktreePath, recursive: true);
            }

            return new SessionWorktreeCleanupOutcome(
                Attempted: true,
                Succeeded: !Directory.Exists(normalizedWorktreePath),
                WorktreePath: normalizedWorktreePath,
                Details: gitCleanup.ExitCode == 0
                    ? "Worktree removed through git worktree remove."
                    : "Worktree removed through filesystem cleanup fallback.");
        }
        catch (Exception ex)
        {
            return new SessionWorktreeCleanupOutcome(
                Attempted: true,
                Succeeded: false,
                WorktreePath: normalizedWorktreePath,
                Details: $"Cleanup failed: {ex.Message}");
        }
    }

    private static string ResolveArtifactDirectory(string repositoryPath, SessionId sessionId, string? artifactDirectory)
        => string.IsNullOrWhiteSpace(artifactDirectory)
            ? Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "artifacts")
            : artifactDirectory;

    private static string Coalesce(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        if (!string.IsNullOrWhiteSpace(secondary))
            return secondary.Trim();

        return fallback;
    }

    private static string Coalesce(string? primary, string fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();

    private static PreparedSessionWorkspace? TryPrepareGitSessionWorkspace(
        string repositoryPath,
        SessionId sessionId,
        string requestedTargetBranch,
        string requestedWorktreePath)
    {
        var currentHeadBranch = TryResolveGitValue(repositoryPath, "branch", "--show-current");
        if (string.IsNullOrWhiteSpace(currentHeadBranch))
            return null;

        var targetBranch = Coalesce(requestedTargetBranch, currentHeadBranch);
        var targetCommit = TryResolveGitValue(repositoryPath, "rev-parse", "--verify", targetBranch);
        if (string.IsNullOrWhiteSpace(targetCommit))
            return null;

        var sessionBranch = BuildSessionBranchName(sessionId);
        var parentDirectory = Path.GetDirectoryName(requestedWorktreePath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        if (Directory.Exists(requestedWorktreePath))
        {
            try
            {
                Directory.Delete(requestedWorktreePath, recursive: true);
            }
            catch
            {
                return null;
            }
        }

        var addWorktreeResult = TryRunGitCommand(
            repositoryPath,
            "worktree",
            "add",
            "-b",
            sessionBranch,
            requestedWorktreePath,
            targetBranch);

        if (addWorktreeResult.ExitCode != 0)
            return null;

        return new PreparedSessionWorkspace(
            WorktreePath: requestedWorktreePath,
            TargetBranch: targetBranch,
            TargetCommit: targetCommit);
    }

    private static (int ExitCode, string StdOut, string StdErr) TryRunGitCommand(string repositoryPath, params string[] arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdOut, stdErr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static string BuildSessionBranchName(SessionId sessionId)
        => $"wip/session/{sessionId.Value}";

    private static string? TryResolveGitValue(string repositoryPath, params string[] arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return null;

            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private sealed record PreparedSessionWorkspace(
        string WorktreePath,
        string TargetBranch,
        string TargetCommit);

    private static SessionValidationStatus ResolveValidationStatusForTransition(
        SessionValidationStatus currentStatus,
        SessionState targetState)
        => targetState == SessionState.Validating ? SessionValidationStatus.InProgress : currentStatus;

    private static SessionApprovalStatus ResolveApprovalStatusForTransition(
        SessionApprovalStatus currentStatus,
        SessionState targetState)
        => targetState switch
        {
            SessionState.AwaitingApproval => SessionApprovalStatus.AwaitingApproval,
            SessionState.Approved or SessionState.Merged => SessionApprovalStatus.Approved,
            _ => currentStatus
        };

    private async ValueTask PublishAndJournalSessionEventAsync(
        SessionEvent sessionEvent,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        await PersistSessionEventAsync(sessionEvent, repositoryPath, cancellationToken);
        await _eventPublisher.PublishAsync(sessionEvent, cancellationToken);
    }

    private static async ValueTask PersistSessionEventAsync(
        SessionEvent sessionEvent,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var journalPath = BuildSessionEventJournalPath(repositoryPath, sessionEvent.SessionId);
        var journalDirectory = Path.GetDirectoryName(journalPath)
            ?? throw new InvalidOperationException($"Failed to resolve session event journal directory for '{journalPath}'.");

        Directory.CreateDirectory(journalDirectory);

        var payload = new PersistedSessionEvent(
            Kind: sessionEvent.Kind,
            SessionId: sessionEvent.SessionId.Value,
            CurrentState: sessionEvent.CurrentState,
            PreviousState: sessionEvent.PreviousState,
            OccurredAtUtc: sessionEvent.OccurredAtUtc,
            Message: sessionEvent.Message,
            CorrelationId: sessionEvent.CorrelationId,
            Diagnostics: sessionEvent.Diagnostics?.Select(static diagnostic =>
                    new PersistedSessionEventDiagnostic(diagnostic.Key, diagnostic.Value))
                .ToArray(),
            ArtifactReferences: sessionEvent.ArtifactReferences?.Select(static artifactReference =>
                    new PersistedSessionEventArtifactReference(
                        artifactReference.Role,
                        artifactReference.ArtifactId,
                        artifactReference.ArtifactPath,
                        artifactReference.ArtifactKind))
                .ToArray());

        var serialized = JsonSerializer.Serialize(payload, SessionStateJsonOptions);
        await File.AppendAllTextAsync(journalPath, serialized + Environment.NewLine, cancellationToken);
    }

    private static WorkflowRegistration ResolveWorkflowSelection(
        WorkflowId sessionWorkflowId,
        WorkflowId? selectedWorkflowId,
        IReadOnlyList<WorkflowRegistration> workflows)
    {
        if (workflows.Count == 0)
            throw new InvalidOperationException("No workflows are registered in the active builder.");

        if (selectedWorkflowId.HasValue)
        {
            var requested = selectedWorkflowId.Value;
            var selected = workflows.FirstOrDefault(registration => registration.WorkflowId.Equals(requested));
            if (selected is null)
            {
                throw new InvalidOperationException(
                    $"Workflow '{requested.Value}' is not registered in the active builder.");
            }

            return selected;
        }

        var sessionWorkflow = workflows.FirstOrDefault(registration => registration.WorkflowId.Equals(sessionWorkflowId));
        if (sessionWorkflow is not null)
            return sessionWorkflow;

        if (workflows.Count == 1)
            return workflows[0];

        throw new InvalidOperationException(
            "Multiple workflows are registered; explicit workflow selection is required.");
    }

    private sealed record PersistedSessionState(
        string SessionId,
        string WorkflowId,
        SessionState State,
        string RepositoryPath,
        string WorktreePath,
        DateTimeOffset UpdatedAtUtc,
        string? TaskDescription = null,
        string? BaseBranch = null,
        string? BaseCommit = null,
        string? TargetBranch = null,
        string? TargetCommit = null,
        string? ArtifactDirectory = null,
        string? ValidationArtifactId = null,
        string? ValidationArtifactPath = null,
        string? ValidationCorrelationId = null,
        SessionValidationStatus ValidationStatus = SessionValidationStatus.NotStarted,
        SessionApprovalStatus ApprovalStatus = SessionApprovalStatus.NotRequested);

    private sealed record SessionWorktreeCleanupOutcome(
        bool Attempted,
        bool Succeeded,
        string WorktreePath,
        string Details)
    {
        public static SessionWorktreeCleanupOutcome MarkOnly(string worktreePath)
            => new(
                Attempted: false,
                Succeeded: true,
                WorktreePath: Path.GetFullPath(worktreePath),
                Details: "Archive mode mark-only preserved the session worktree.");
    }

    private sealed record PersistedSessionEvent(
        SessionEventKind Kind,
        string SessionId,
        SessionState CurrentState,
        SessionState? PreviousState,
        DateTimeOffset OccurredAtUtc,
        string Message,
        string? CorrelationId,
        IReadOnlyList<PersistedSessionEventDiagnostic>? Diagnostics,
        IReadOnlyList<PersistedSessionEventArtifactReference>? ArtifactReferences);

    private sealed record PersistedSessionEventDiagnostic(
        string Key,
        string Value);

    private sealed record PersistedSessionEventArtifactReference(
        string Role,
        string ArtifactId,
        string ArtifactPath,
        ArtifactKind ArtifactKind);
}
