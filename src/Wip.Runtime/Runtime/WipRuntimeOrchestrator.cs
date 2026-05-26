using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;

namespace Wip.Runtime.Runtime;

public sealed class WipRuntimeOrchestrator
{
    private const string SessionStateFileName = "session-state.json";
    private static readonly JsonSerializerOptions SessionStateJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly FrozenDictionary<SessionState, SessionState> NextStates =
        new Dictionary<SessionState, SessionState>
        {
            [SessionState.Created] = SessionState.Editing,
            [SessionState.Editing] = SessionState.Validating,
            [SessionState.Validating] = SessionState.AwaitingApproval,
            [SessionState.AwaitingApproval] = SessionState.Approved,
            [SessionState.Approved] = SessionState.Merged
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

    public async ValueTask<SessionSnapshot> StartSessionAsync(
        WorkflowId workflowId,
        string repositoryPath,
        string worktreePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(repositoryPath));

        if (string.IsNullOrWhiteSpace(worktreePath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(worktreePath));

        var now = DateTimeOffset.UtcNow;
        var snapshot = new SessionSnapshot(
            SessionId: new SessionId(Guid.NewGuid().ToString("N")),
            WorkflowId: workflowId,
            State: SessionState.Created,
            RepositoryPath: repositoryPath,
            WorktreePath: worktreePath,
            UpdatedAtUtc: now);

        await _authorityGate.WaitAsync(cancellationToken);
        try
        {
            await _sessionStore.SaveAsync(snapshot, cancellationToken);
            await PersistSessionStateAsync(snapshot, cancellationToken);
            await _eventPublisher.PublishAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionStarted,
                    SessionId: snapshot.SessionId,
                    CurrentState: snapshot.State,
                    PreviousState: null,
                    OccurredAtUtc: now,
                    Message: "Session started."),
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

            var workflow = ResolveWorkflowSelection(current.WorkflowId, selectedWorkflowId, builder.WorkflowRegistrations);
            var stageDescriptors = WorkflowStageDescriptorMapper.CreateLinear(workflow.RequestType, workflow.ResultType);
            var stageExecutions = new List<WorkflowStageExecution>(stageDescriptors.Count);

            foreach (var stageDescriptor in stageDescriptors)
            {
                var targetState = WorkflowStageStateMapper.ToSessionState(stageDescriptor.Stage);
                var appliedTransition = false;

                if (current.State != targetState)
                {
                    current = await ApplyTransitionUnderAuthorityAsync(current, targetState, cancellationToken);
                    appliedTransition = true;
                }

                stageExecutions.Add(new WorkflowStageExecution(stageDescriptor, current.State, appliedTransition));
            }

            return new WorkflowExecutionResult(workflow.WorkflowId, stageExecutions);
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
            await _eventPublisher.PublishAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionAttached,
                    SessionId: sessionId,
                    CurrentState: restored.State,
                    PreviousState: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Message: "Session attached."),
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
            await _eventPublisher.PublishAsync(
                new SessionEvent(
                    Kind: SessionEventKind.SessionDetached,
                    SessionId: sessionId,
                    CurrentState: current.State,
                    PreviousState: null,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Message: "Session detached."),
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
            UpdatedAtUtc: snapshot.UpdatedAtUtc);

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
            UpdatedAtUtc: restored.UpdatedAtUtc);
    }

    private static string BuildSessionStatePath(string repositoryPath, SessionId sessionId)
        => Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, SessionStateFileName);

    private static SessionState ResolveExpectedNext(SessionState state)
    {
        if (!NextStates.TryGetValue(state, out var expectedNext))
            throw new InvalidOperationException($"Session state '{state}' is terminal and cannot transition.");

        return expectedNext;
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
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _sessionStore.SaveAsync(updated, cancellationToken);
        await PersistSessionStateAsync(updated, cancellationToken);
        await _eventPublisher.PublishAsync(
            new SessionEvent(
                Kind: SessionEventKind.SessionTransitioned,
                SessionId: current.SessionId,
                CurrentState: updated.State,
                PreviousState: current.State,
                OccurredAtUtc: updated.UpdatedAtUtc,
                Message: $"Session transitioned from {current.State} to {updated.State}."),
            cancellationToken);

        return updated;
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
        DateTimeOffset UpdatedAtUtc);
}
