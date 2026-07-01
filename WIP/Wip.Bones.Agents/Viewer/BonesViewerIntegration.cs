using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Viewer;

public static class BonesViewerMarkers
{
    public const string ViewerUrlStdoutPrefix = "Bones viewer:";

    public const string ViewerUrlArtifactFileName = "bones-viewer-url";
}

public interface IBonesPublicMatchFeed
{
    void PublishMatchState(
        SessionId sessionId,
        BonesGameId matchId,
        int matchSeed,
        BonesMatchResult matchResult,
        bool isComplete);
}

public interface IBonesViewerStdout
{
    void WriteLine(string line);
}

public interface IBonesViewerRunPublisher
{
    ValueTask PublishViewerUrlOnceAsync(
        SessionId sessionId,
        BonesGameId matchId,
        Uri viewerUrl,
        CancellationToken cancellationToken);
}

public static class BonesViewerUrlBuilder
{
    public static Uri BuildMatchViewUrl(Uri viewerBaseUrl, SessionId sessionId, BonesGameId matchId)
    {
        ArgumentNullException.ThrowIfNull(viewerBaseUrl);

        var baseUri = viewerBaseUrl.ToString().TrimEnd('/');
        return new Uri($"{baseUri}/bones/sessions/{sessionId.Value}/matches/{matchId.Value}/view");
    }

    public static BonesGameId CreateLearningMatchId(SessionId sessionId)
        => new($"learning-{sessionId.Value}");
}

public sealed class BonesLearningViewerCoordinator
{
    private readonly IBonesPublicMatchFeed _matchFeed;
    private readonly IBonesViewerRunPublisher _runPublisher;
    private readonly Uri _viewerBaseUrl;

    public BonesLearningViewerCoordinator(
        IBonesPublicMatchFeed matchFeed,
        IBonesViewerRunPublisher runPublisher,
        Uri viewerBaseUrl)
    {
        _matchFeed = matchFeed ?? throw new ArgumentNullException(nameof(matchFeed));
        _runPublisher = runPublisher ?? throw new ArgumentNullException(nameof(runPublisher));
        _viewerBaseUrl = viewerBaseUrl ?? throw new ArgumentNullException(nameof(viewerBaseUrl));
    }

    public async ValueTask EnsureViewerPublishedAsync(
        SessionId sessionId,
        int matchSeed,
        CancellationToken cancellationToken)
    {
        var matchId = BonesViewerUrlBuilder.CreateLearningMatchId(sessionId);
        var viewerUrl = BonesViewerUrlBuilder.BuildMatchViewUrl(_viewerBaseUrl, sessionId, matchId);
        await _runPublisher.PublishViewerUrlOnceAsync(sessionId, matchId, viewerUrl, cancellationToken);

        _matchFeed.PublishMatchState(
            sessionId,
            matchId,
            matchSeed,
            CreateEmptyMatch(matchId),
            isComplete: false);
    }

    public void PublishLiveMatchState(
        SessionId sessionId,
        int matchSeed,
        BonesMatchResult matchResult,
        bool isComplete)
    {
        var matchId = BonesViewerUrlBuilder.CreateLearningMatchId(sessionId);
        var publishedResult = new BonesMatchResult(
            matchId,
            matchResult.Winner,
            matchResult.CumulativeScores,
            matchResult.Rounds,
            matchResult.Transcript);

        _matchFeed.PublishMatchState(sessionId, matchId, matchSeed, publishedResult, isComplete);
    }

    private static BonesMatchResult CreateEmptyMatch(BonesGameId matchId)
    {
        var scores = new Dictionary<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            scores[new BonesPlayerId(seat)] = 0;

        return new BonesMatchResult(
            matchId,
            new BonesPlayerId(BonesPlayerId.MinSeat),
            scores,
            [],
            []);
    }
}

public sealed class BonesViewerRunPublisher : IBonesViewerRunPublisher
{
    private const string ProducerType = "Wip.Bones.Agents.BonesViewerRunPublisher";
    private const string ProducerVersion = "1.0.0";

    private readonly IArtifactStore _artifactStore;
    private readonly IBonesViewerStdout _stdout;
    private readonly HashSet<string> _publishedSessions = new(StringComparer.Ordinal);

    public BonesViewerRunPublisher(IArtifactStore artifactStore, IBonesViewerStdout stdout)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
    }

    public async ValueTask PublishViewerUrlOnceAsync(
        SessionId sessionId,
        BonesGameId matchId,
        Uri viewerUrl,
        CancellationToken cancellationToken)
    {
        if (!_publishedSessions.Add(sessionId.Value))
            return;

        _stdout.WriteLine($"{BonesViewerMarkers.ViewerUrlStdoutPrefix} {viewerUrl}");

        var payload = new BonesViewerUrlArtifact(
            SessionId: sessionId.Value,
            MatchId: matchId.Value,
            ViewerUrl: viewerUrl.ToString());

        await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-viewer-url-{sessionId.Value}"),
                kind: ArtifactKind.Json,
                fileName: BonesViewerMarkers.ViewerUrlArtifactFileName,
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private sealed record BonesViewerUrlArtifact(string SessionId, string MatchId, string ViewerUrl);
}

public sealed class ConsoleBonesViewerStdout : IBonesViewerStdout
{
    public void WriteLine(string line) => Console.WriteLine(line);
}

public sealed class CollectingBonesViewerStdout : IBonesViewerStdout
{
    public List<string> Lines { get; } = [];

    public void WriteLine(string line) => Lines.Add(line);
}

public static class BonesPublicMatchStateBuilder
{
    public static BonesMatchResult CreateInProgress(
        BonesGameId gameId,
        IReadOnlyList<BonesEvent> transcript,
        IReadOnlyList<BonesMatchRoundRecord> rounds,
        IReadOnlyDictionary<BonesPlayerId, int> cumulativeScores,
        BonesPlayerId? provisionalWinner = null)
    {
        var winner = provisionalWinner ?? new BonesPlayerId(BonesPlayerId.MinSeat);
        return new BonesMatchResult(gameId, winner, cumulativeScores, rounds, transcript);
    }
}
