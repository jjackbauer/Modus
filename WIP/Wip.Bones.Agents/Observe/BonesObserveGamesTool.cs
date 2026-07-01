using System.Collections.Immutable;
using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Observe;

public sealed class BonesObserveGamesTool : ITool<BonesObserveGamesRequest, BonesObserveGamesResult>
{
    private const string ProducerType = "Wip.Bones.Agents.BonesObserveGamesTool";
    private const string ProducerVersion = "1.0.0";
    private const int DefaultTargetScore = 100;

    private readonly IArtifactStore _artifactStore;
    private readonly BonesMatchSimulator _simulator;
    private readonly BonesLearningViewerCoordinator? _viewerCoordinator;
    private readonly BonesGameSimulationBudget? _gameBudget;

    public BonesObserveGamesTool(
        IArtifactStore artifactStore,
        BonesMatchSimulator simulator,
        BonesLearningViewerCoordinator? viewerCoordinator = null,
        BonesGameSimulationBudget? gameBudget = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _viewerCoordinator = viewerCoordinator;
        _gameBudget = gameBudget;
    }

    public async ValueTask<BonesObserveGamesResult> ExecuteAsync(
        BonesObserveGamesRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.GameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.GameCount), "Game count must be positive.");

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.RepositoryPath));

        if (request.ObserverPlayerIds.Count == 0)
            throw new ArgumentException("At least one observer player id is required.", nameof(request.ObserverPlayerIds));

        var targetScore = request.TargetScore > 0 ? request.TargetScore : DefaultTargetScore;
        var knowledgeStore = new BonesPlayerKnowledgeStore(
            _artifactStore,
            request.RepositoryPath,
            context.SessionId);

        if (_viewerCoordinator is not null)
        {
            await _viewerCoordinator.EnsureViewerPublishedAsync(
                context.SessionId,
                request.Seed,
                cancellationToken);
        }

        var observationsPerPlayer = request.ObserverPlayerIds
            .Distinct()
            .ToDictionary(observerId => observerId, _ => 0);

        for (var gameIndex = 0; gameIndex < request.GameCount; gameIndex++)
        {
            var gameNumber = gameIndex + 1;
            var gameId = new BonesGameId($"observe-{request.Seed}-g{gameNumber}");
            var gameSeed = HashCode.Combine(request.Seed, gameNumber);
            var matchConfig = new BonesMatchConfig(
                gameId,
                gameSeed,
                targetScore,
                CreateStubPlayerSlots());

            var matchResult = _simulator.RunMatch(matchConfig);

            if (_viewerCoordinator is not null)
            {
                _viewerCoordinator.PublishLiveMatchState(
                    context.SessionId,
                    gameSeed,
                    matchResult,
                    isComplete: true);
            }

            foreach (var observerId in observationsPerPlayer.Keys)
            {
                var markdown = BonesObservationTranscriptBuilder.BuildRedactedTranscript(observerId, matchResult);
                await knowledgeStore.SaveObservation(
                    observerId,
                    new BonesObservationTranscript(gameId, observerId, markdown),
                    cancellationToken);

                observationsPerPlayer[observerId]++;
            }
        }

        var transcriptsPersisted = observationsPerPlayer.Values.Sum();
        var executionArtifact = await SaveExecutionArtifactAsync(
            context.SessionId,
            request,
            request.GameCount,
            transcriptsPersisted,
            observationsPerPlayer,
            cancellationToken);

        _gameBudget?.RecordGames(request.GameCount);

        return new BonesObserveGamesResult(
            request.GameCount,
            transcriptsPersisted,
            observationsPerPlayer.ToImmutableDictionary(),
            executionArtifact);
    }

    private static ImmutableDictionary<BonesPlayerId, IBonesPlayerSlot> CreateStubPlayerSlots()
    {
        var slots = ImmutableDictionary.CreateBuilder<BonesPlayerId, IBonesPlayerSlot>();
        var stubSlot = new BonesFirstLegalMovePlayerSlot();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            slots.Add(new BonesPlayerId(seat), stubSlot);

        return slots.ToImmutable();
    }

    private async ValueTask<ArtifactDescriptor> SaveExecutionArtifactAsync(
        SessionId sessionId,
        BonesObserveGamesRequest request,
        int gamesObserved,
        int transcriptsPersisted,
        IReadOnlyDictionary<BonesPlayerId, int> observationsPerPlayer,
        CancellationToken cancellationToken)
    {
        var payload = new BonesObserveGamesExecutionLog(
            SessionId: sessionId,
            GameCount: request.GameCount,
            Seed: request.Seed,
            TargetScore: request.TargetScore > 0 ? request.TargetScore : DefaultTargetScore,
            GamesObserved: gamesObserved,
            TranscriptsPersisted: transcriptsPersisted,
            ObservationsPerPlayer: observationsPerPlayer.ToDictionary(
                entry => entry.Key.Seat,
                entry => entry.Value));

        var producedAtUtc = DateTimeOffset.UtcNow;

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-observe-games-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-observe-games-execution",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: producedAtUtc),
            cancellationToken);
    }

    private sealed record BonesObserveGamesExecutionLog(
        SessionId SessionId,
        int GameCount,
        int Seed,
        int TargetScore,
        int GamesObserved,
        int TranscriptsPersisted,
        IReadOnlyDictionary<int, int> ObservationsPerPlayer);
}
