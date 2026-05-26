using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Sessions;

public enum SessionState
{
    Created = 1,
    Editing = 2,
    Validating = 3,
    AwaitingApproval = 4,
    Approved = 5,
    Merged = 6
}

public sealed record SessionSnapshot(
    SessionId SessionId,
    WorkflowId WorkflowId,
    SessionState State,
    string RepositoryPath,
    string WorktreePath,
    DateTimeOffset UpdatedAtUtc);

public interface ISessionStore
{
    ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken);
}
