using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Ponder;

/// <summary>
/// Co-learning Ponder agent that runs Ponder for all 4 seats when AllSeatsLearning=true,
/// or delegates to single-seat Ponder for backward compatibility.
/// Seats with viable library entries are skipped.
/// Partial failures are recorded as artifacts without aborting the iteration.
/// </summary>
public sealed class BonesCoLearningPonderAgent : IAgent<BonesPonderRequest, BonesPonderResult>
{
    private const string ProducerType = "Wip.Bones.Agents.BonesCoLearningPonderAgent";
    private const string ProducerVersion = "1.0.0";

    private readonly BonesPonderAgent _singleSeatAgent;
    private readonly BonesStrategyLibrary? _strategyLibrary;
    private readonly IArtifactStore _artifactStore;

    public BonesCoLearningPonderAgent(
        BonesPonderAgent singleSeatAgent,
        BonesStrategyLibrary? strategyLibrary = null,
        IArtifactStore? artifactStore = null)
    {
        _singleSeatAgent = singleSeatAgent ?? throw new ArgumentNullException(nameof(singleSeatAgent));
        _strategyLibrary = strategyLibrary;
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    public async ValueTask<BonesPonderResult> ExecuteAsync(
        BonesPonderRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AllSeatsLearning)
            return await _singleSeatAgent.ExecuteAsync(request, context, cancellationToken);

        // Co-learning mode: run Ponder for all seats that need it.
        var completed = new List<BonesPonderResult>();
        var failed = new List<BonesCoLearningSeatFailureInfo>();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playerId = new BonesPlayerId(seat);

            // Skip seats that already have a viable library strategy.
            if (IsLibraryEntryViable(playerId))
                continue;

            try
            {
                var seatRequest = new BonesPonderRequest(
                    playerId,
                    request.RepositoryPath,
                    request.ModelId);

                var seatResult = await _singleSeatAgent.ExecuteAsync(
                    seatRequest,
                    context,
                    cancellationToken);
                completed.Add(seatResult);
            }
            catch (InvalidOperationException)
            {
                // Ponder exhausted retry budget for this seat — record failure and continue.
                var failureArtifact = await SaveCoLearningPonderFailureArtifactAsync(
                    context.SessionId,
                    playerId,
                    cancellationToken);

                failed.Add(new BonesCoLearningSeatFailureInfo(
                    seat,
                    "Ponder retry budget exhausted for seat " + seat,
                    failureArtifact));
            }
        }

        // Return first completed result as aggregate, or synthesize a placeholder.
        if (completed.Count > 0)
            return completed[0];

        // All seats were skipped (viable library) or all failed.
        // Return a placeholder result — the downstream map binding doesn't read PonderResult.
        return CreatePlaceholderResult(request.PlayerId);
    }

    /// <summary>
    /// A seat is viable if its library entry (Script or Markdown) has at least 1 match played,
    /// indicating it has already gone through a learning cycle and can skip Ponder.
    /// </summary>
    private bool IsLibraryEntryViable(BonesPlayerId playerId)
    {
        if (_strategyLibrary is null)
            return false;

        var ranking = _strategyLibrary.GetRanking(playerId);
        if (ranking.Count == 0)
            return false;

        var best = ranking[0];

        // Script and Markdown entries are both viable when they have match history.
        if (best.Kind is not (BonesStrategyKind.Script or BonesStrategyKind.Markdown))
            return false;

        return best.Effectiveness.MatchesPlayed >= 1;
    }

    private async ValueTask<ArtifactDescriptor> SaveCoLearningPonderFailureArtifactAsync(
        SessionId sessionId,
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        var payload = new BonesCoLearningPonderFailureLog(
            SessionId: sessionId.Value,
            PlayerSeat: playerId.Seat,
            Reason: "Ponder retry budget exhausted; co-learning ponder continues with remaining seats.");

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-ponder-failure-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-ponder-failure",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static BonesPonderResult CreatePlaceholderResult(BonesPlayerId playerId)
    {
        var placeholderId = new BonesStrategyId("co-learning-placeholder");
        var placeholderDocument = new BonesStrategyDocument(
            placeholderId,
            playerId,
            "// Co-learning placeholder — all seats skipped or failed.");
        var placeholderArtifact = new BonesStrategyArtifact(
            placeholderId,
            playerId,
            BonesStrategyKind.Script,
            "// Co-learning placeholder.",
            BonesPromotionStatus.Active);
        var now = DateTimeOffset.UtcNow;
        var sessionId = new SessionId("co-learning-placeholder");

        return new BonesPonderResult(
            placeholderId,
            placeholderDocument,
            placeholderArtifact,
            new ArtifactDescriptor(
                new ArtifactId("co-learning-placeholder"),
                sessionId,
                ArtifactKind.Json,
                "co-learning-placeholder",
                ProducerType,
                ProducerVersion,
                now),
            0,
            new ArtifactDescriptor(
                new ArtifactId("co-learning-placeholder-exec"),
                sessionId,
                ArtifactKind.Json,
                "co-learning-placeholder-exec",
                ProducerType,
                ProducerVersion,
                now));
    }

    private sealed record BonesCoLearningSeatFailureInfo(
        int Seat,
        string Reason,
        ArtifactDescriptor FailureArtifact);

    private sealed record BonesCoLearningPonderFailureLog(
        string SessionId,
        int PlayerSeat,
        string Reason);
}
