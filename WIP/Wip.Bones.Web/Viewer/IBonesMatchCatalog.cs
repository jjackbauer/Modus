using Wip.Abstractions.Identifiers;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

public sealed record BonesRegisteredMatch(
    SessionId SessionId,
    BonesGameId MatchId,
    int MatchSeed,
    BonesMatchResult MatchResult,
    bool IsComplete = true)
{
    public MatchRevision Revision { get; init; }
}

public interface IBonesMatchCatalog
{
    void Register(BonesRegisteredMatch match);

    bool TryGet(SessionId sessionId, BonesGameId matchId, out BonesRegisteredMatch match);
}

public sealed class InMemoryBonesMatchCatalog : ImmutableBonesMatchCatalog;
