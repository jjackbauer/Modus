using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Builder;

namespace Wip.Runtime.Runtime;

/// <summary>Resolves the stage input for one stage; plan-based or legacy-registry based.</summary>
public delegate WorkflowStageMappedInputEvidence WorkflowStageInputMapper(
    WorkflowStageDescriptor stageDescriptor,
    SessionSnapshot session,
    object? previousStageResult);

/// <summary>
/// Executes a compiled workflow. <see cref="LinearWorkflowExecutor"/> is the default; a host or a
/// workflow can register a custom executor when it needs branching, loops or its own evidence.
/// </summary>
public interface IWorkflowExecutor
{
    ValueTask<WorkflowExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// The governance operations the executor needs from the orchestrator: session-state advancement
/// under the authority gate, the artifact reference shape, and the terminal run event.
/// </summary>
public interface IWorkflowRunAuthority
{
    bool IsRunBackwardAdvancement(SessionState currentState, SessionState targetState);

    ValueTask<SessionSnapshot> AdvanceAsync(
        SessionSnapshot snapshot,
        SessionState targetState,
        CancellationToken cancellationToken);

    SessionEventArtifactReference CreateStageExecutionReference(ArtifactDescriptor artifact);

    ValueTask PublishWorkflowExecutedAsync(
        SessionSnapshot snapshot,
        WorkflowId workflowId,
        string correlationId,
        IReadOnlyList<SessionEventArtifactReference> artifactReferences,
        CancellationToken cancellationToken);
}

public sealed class WorkflowExecutionContext
{
    public WorkflowExecutionContext(
        WorkflowId workflowId,
        SessionSnapshot session,
        WipBuilder builder,
        IServiceProvider services,
        IArtifactStore? artifactStore,
        string correlationId,
        LinearWorkflowStageCompilation compilation,
        WorkflowStageInputMapper mapStageInput,
        IWorkflowRunAuthority authority)
    {
        WorkflowId = workflowId;
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Builder = builder ?? throw new ArgumentNullException(nameof(builder));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        ArtifactStore = artifactStore;
        CorrelationId = correlationId;
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        MapStageInput = mapStageInput ?? throw new ArgumentNullException(nameof(mapStageInput));
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    public WorkflowId WorkflowId { get; }

    public SessionSnapshot Session { get; }

    public WipBuilder Builder { get; }

    public IServiceProvider Services { get; }

    public IArtifactStore? ArtifactStore { get; }

    public string CorrelationId { get; }

    public LinearWorkflowStageCompilation Compilation { get; }

    public WorkflowStageInputMapper MapStageInput { get; }

    public IWorkflowRunAuthority Authority { get; }
}