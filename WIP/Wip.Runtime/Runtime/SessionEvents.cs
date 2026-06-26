using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Artifacts;

namespace Wip.Runtime.Runtime;

public enum SessionEventKind
{
    SessionStarted = 1,
    SessionTransitioned = 2,
    SessionAttached = 3,
    SessionDetached = 4,
    PlanGenerated = 5,
    WorkflowExecuted = 6,
    DiffGenerated = 7,
    CheckpointGenerated = 8,
    ValidationCompleted = 9,
    ReviewGenerated = 10,
    ApprovalTokenCreated = 11,
    MergeCompleted = 12,
    SessionArchived = 13,
    SessionAborted = 14,
    MergeAttempted = 15,
    MergeFailed = 16,
    MergeSucceeded = 17
}

public sealed record SessionEvent(
    SessionEventKind Kind,
    SessionId SessionId,
    SessionState CurrentState,
    SessionState? PreviousState,
    DateTimeOffset OccurredAtUtc,
    string Message,
    string? CorrelationId = null,
    IReadOnlyList<SessionEventDiagnostic>? Diagnostics = null,
    IReadOnlyList<SessionEventArtifactReference>? ArtifactReferences = null);

public sealed record SessionEventDiagnostic(
    string Key,
    string Value);

public sealed record SessionEventArtifactReference(
    string Role,
    string ArtifactId,
    string ArtifactPath,
    ArtifactKind ArtifactKind);

public interface ISessionEventPublisher
{
    ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken);
}
