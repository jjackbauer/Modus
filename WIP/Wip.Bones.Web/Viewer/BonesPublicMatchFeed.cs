using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

public sealed class BonesCatalogPublicMatchFeed : IBonesPublicMatchFeed
{
    private readonly IBonesMatchCatalog _catalog;

    public BonesCatalogPublicMatchFeed(IBonesMatchCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void PublishMatchState(
        SessionId sessionId,
        BonesGameId matchId,
        int matchSeed,
        BonesMatchResult matchResult,
        bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(matchId);
        ArgumentNullException.ThrowIfNull(matchResult);

        _catalog.Register(new BonesRegisteredMatch(
            sessionId,
            matchId,
            matchSeed,
            ImmutableBonesMatchCatalog.CloneMatchResult(matchResult),
            isComplete));
    }
}
