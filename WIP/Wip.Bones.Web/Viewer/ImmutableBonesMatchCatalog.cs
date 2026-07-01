using System.Collections.Concurrent;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

public class ImmutableBonesMatchCatalog : IBonesMatchCatalog
{
    private readonly ConcurrentDictionary<(string SessionId, string MatchId), BonesRegisteredMatch> _matches = new();
    private readonly ConcurrentDictionary<(string SessionId, string MatchId), long> _revisionCounters = new();

    public void Register(BonesRegisteredMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var key = (match.SessionId.Value, match.MatchId.Value);
        var nextRevision = _revisionCounters.AddOrUpdate(key, 1, static (_, previous) => previous + 1);
        var immutableMatch = match with
        {
            Revision = new MatchRevision(nextRevision),
            MatchResult = CloneMatchResult(match.MatchResult),
        };

        _matches[key] = immutableMatch;
    }

    public bool TryGet(SessionId sessionId, BonesGameId matchId, out BonesRegisteredMatch match)
    {
        return _matches.TryGetValue((sessionId.Value, matchId.Value), out match!);
    }

    internal static BonesMatchResult CloneMatchResult(BonesMatchResult matchResult)
    {
        ArgumentNullException.ThrowIfNull(matchResult);

        return new BonesMatchResult(
            matchResult.GameId,
            matchResult.Winner,
            matchResult.CumulativeScores,
            matchResult.Rounds,
            matchResult.Transcript);
    }
}