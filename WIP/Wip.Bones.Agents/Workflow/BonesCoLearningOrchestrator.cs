using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Workflow;

public sealed class BonesCoLearningOrchestrator
{
    private readonly IArtifactStore _artifactStore;
    private readonly BonesStrategyLibrary _strategyLibrary;
    private readonly BonesPonderAgent? _ponderAgent;
    private readonly BonesEnhanceStrategyAgent? _enhanceAgent;

    public BonesCoLearningOrchestrator(
        IArtifactStore artifactStore,
        BonesStrategyLibrary strategyLibrary,
        BonesPonderAgent? ponderAgent = null,
        BonesEnhanceStrategyAgent? enhanceAgent = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _strategyLibrary = strategyLibrary ?? throw new ArgumentNullException(nameof(strategyLibrary));
        _ponderAgent = ponderAgent;
        _enhanceAgent = enhanceAgent;
    }

    /// <summary>
    /// Runs Ponder for every seat (1-4) whose library entry is missing or has win rate below the viability threshold.
    /// When AllSeatsLearning is false, only the LearningPlayerId seat is processed (backward compatible).
    /// Seats with viable library strategies are skipped.
    /// On partial failure, records failure artifacts and continues with remaining seats.
    /// </summary>
    public async ValueTask<BonesCoLearningPonderResult> RunPonderStageAsync(
        BonesCoLearningPonderRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (_ponderAgent is null)
            throw new InvalidOperationException("BonesCoLearningOrchestrator.RunPonderStageAsync requires a BonesPonderAgent.");

        var completed = new List<BonesPonderResult>();
        var failed = new List<BonesCoLearningSeatFailure>();

        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? null : request.ModelId.Trim();

        var seatsToProcess = request.AllSeatsLearning
            ? Enumerable.Range(BonesPlayerId.MinSeat, BonesPlayerId.MaxSeat - BonesPlayerId.MinSeat + 1)
            : request.LearningPlayerId is { } learningPlayerId
                ? new[] { learningPlayerId.Seat }
                : new[] { BonesPlayerId.MinSeat };

        foreach (var seat in seatsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playerId = new BonesPlayerId(seat);

            if (request.AllSeatsLearning && IsLibraryEntryViable(playerId))
                continue;

            try
            {
                var ponderRequest = modelId is null
                    ? new BonesPonderRequest(playerId, request.RepositoryPath)
                    : new BonesPonderRequest(playerId, request.RepositoryPath, modelId);

                var ponderResult = await _ponderAgent.ExecuteAsync(ponderRequest, context, cancellationToken);
                completed.Add(ponderResult);
            }
            catch (InvalidOperationException)
            {
                // Ponder exhausted retry budget — record failure and continue
                var failureArtifact = await SavePonderFailureArtifactAsync(
                    context.SessionId,
                    playerId,
                    cancellationToken);

                failed.Add(new BonesCoLearningSeatFailure(
                    seat,
                    "Ponder retry budget exhausted for seat " + seat,
                    failureArtifact));
            }
        }

        return new BonesCoLearningPonderResult(completed, failed);
    }

    /// <summary>
    /// Runs Enhance for every seat (1-4) independently after a match completes.
    /// When AllSeatsLearning is false, only the LearningPlayerId seat is enhanced (backward compatible).
    /// Each seat evaluates from its own perspective (match outcome, transcript, matchGameId).
    /// If enhance fails for one seat, other seats are unaffected.
    /// </summary>
    public async ValueTask<BonesCoLearningEnhanceResult> RunEnhanceStageAsync(
        BonesCoLearningEnhanceRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (_enhanceAgent is null)
            throw new InvalidOperationException("BonesCoLearningOrchestrator.RunEnhanceStageAsync requires a BonesEnhanceStrategyAgent.");

        var completed = new List<BonesEnhanceStrategyResult>();
        var failed = new List<BonesCoLearningSeatFailure>();

        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? null : request.ModelId.Trim();

        var seatsToProcess = request.AllSeatsLearning
            ? Enumerable.Range(BonesPlayerId.MinSeat, BonesPlayerId.MaxSeat - BonesPlayerId.MinSeat + 1)
            : request.LearningPlayerId is { } learningPlayerId
                ? new[] { learningPlayerId.Seat }
                : new[] { BonesPlayerId.MinSeat };

        foreach (var seat in seatsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playerId = new BonesPlayerId(seat);

            try
            {
                var enhanceRequest = modelId is null
                    ? new BonesEnhanceStrategyRequest(
                        playerId,
                        request.RepositoryPath,
                        request.MatchGameId,
                        EvaluationTargetScore: request.EvaluationTargetScore)
                    : new BonesEnhanceStrategyRequest(
                        playerId,
                        request.RepositoryPath,
                        request.MatchGameId,
                        ModelId: modelId,
                        EvaluationTargetScore: request.EvaluationTargetScore);

                var enhanceResult = await _enhanceAgent.ExecuteAsync(enhanceRequest, context, cancellationToken);
                completed.Add(enhanceResult);
            }
            catch (Exception ex)
            {
                // Enhance failed for this seat — other seats unaffected
                var failureArtifact = await SaveEnhanceFailureArtifactAsync(
                    context.SessionId,
                    playerId,
                    ex,
                    cancellationToken);

                failed.Add(new BonesCoLearningSeatFailure(
                    seat,
                    $"Enhance failed for seat {seat}: {ex.Message}",
                    failureArtifact));
            }
        }

        return new BonesCoLearningEnhanceResult(completed, failed);
    }

    /// <summary>
    /// Determines whether a seat already has a viable strategy in the library.
    /// A seat is viable if its library entry is a Script with at least 1 match played.
    /// Markdown entries are never viable — the seat must go through Ponder to author a Script.
    /// </summary>
    private bool IsLibraryEntryViable(BonesPlayerId playerId)
    {
        var ranking = _strategyLibrary.GetRanking(playerId);
        if (ranking.Count == 0)
            return false;

        var best = ranking[0];

        // Only Script entries are viable; Markdown entries must go through Ponder.
        if (best.Kind != BonesStrategyKind.Script)
            return false;

        var effectiveness = best.Effectiveness;

        // Viable if at least 1 match played
        if (effectiveness.MatchesPlayed >= 1)
            return true;

        return false;
    }

    private async ValueTask<ArtifactDescriptor> SavePonderFailureArtifactAsync(
        SessionId sessionId,
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        var payload = new BonesCoLearningPonderFailureLog(
            SessionId: sessionId.Value,
            PlayerSeat: playerId.Seat,
            Reason: "Ponder retry budget exhausted; co-learning orchestrator continues with remaining seats.");

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-ponder-failure-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-ponder-failure",
                content: System.Text.Json.JsonSerializer.Serialize(payload),
                producerType: "Wip.Bones.Agents.BonesCoLearningOrchestrator",
                producerVersion: "1.0.0",
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async ValueTask<ArtifactDescriptor> SaveEnhanceFailureArtifactAsync(
        SessionId sessionId,
        BonesPlayerId playerId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var payload = new BonesCoLearningEnhanceFailureLog(
            SessionId: sessionId.Value,
            PlayerSeat: playerId.Seat,
            Reason: exception.Message);

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-enhance-failure-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-enhance-failure",
                content: System.Text.Json.JsonSerializer.Serialize(payload),
                producerType: "Wip.Bones.Agents.BonesCoLearningOrchestrator",
                producerVersion: "1.0.0",
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private sealed record BonesCoLearningPonderFailureLog(
        string SessionId,
        int PlayerSeat,
        string Reason);

    private sealed record BonesCoLearningEnhanceFailureLog(
        string SessionId,
        int PlayerSeat,
        string Reason);
}
