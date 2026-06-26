using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Sessions;

public enum SessionState
{
    Created = 1,
    Editing = 2,
    Validating = 3,
    AwaitingApproval = 4,
    Approved = 5,
    Merged = 6,
    Checkpointed = 7,
    Archived = 8,
    Aborted = 9
}

public enum SessionValidationStatus
{
    NotStarted = 1,
    InProgress = 2,
    Passed = 3,
    Failed = 4
}

public enum SessionApprovalStatus
{
    NotRequested = 1,
    AwaitingApproval = 2,
    Approved = 3
}

public sealed record SessionStartRequest(
    WorkflowId WorkflowId,
    string TaskDescription,
    string RepositoryPath,
    string? BaseBranch = null,
    string? BaseCommit = null,
    string? TargetBranch = null,
    string? TargetCommit = null,
    string? WorktreePath = null,
    string? ArtifactDirectory = null);

public sealed record SessionSnapshot(
    SessionId SessionId,
    WorkflowId WorkflowId,
    SessionState State,
    string RepositoryPath,
    string WorktreePath,
    DateTimeOffset UpdatedAtUtc,
    string TaskDescription = "",
    string BaseBranch = "",
    string BaseCommit = "",
    string TargetBranch = "",
    string TargetCommit = "",
    string ArtifactDirectory = "",
    string ValidationArtifactId = "",
    string ValidationArtifactPath = "",
    string ValidationCorrelationId = "",
    SessionValidationStatus ValidationStatus = SessionValidationStatus.NotStarted,
    SessionApprovalStatus ApprovalStatus = SessionApprovalStatus.NotRequested);

public interface ISessionStore
{
    ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken);
}
