using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;

namespace Wip.Runtime.Runtime;

public enum SessionEventKind
{
    SessionStarted = 1,
    SessionTransitioned = 2,
    SessionAttached = 3,
    SessionDetached = 4
}

public sealed record SessionEvent(
    SessionEventKind Kind,
    SessionId SessionId,
    SessionState CurrentState,
    SessionState? PreviousState,
    DateTimeOffset OccurredAtUtc,
    string Message);

public interface ISessionEventPublisher
{
    ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken);
}
