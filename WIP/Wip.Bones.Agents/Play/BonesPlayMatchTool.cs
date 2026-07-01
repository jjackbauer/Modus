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

namespace Wip.Bones.Agents.Play;

public sealed class BonesPlayMatchTool : ITool<BonesPlayMatchRequest, BonesPlayMatchResult>
{
    private const string ProducerType = "Wip.Bones.Agents.BonesPlayMatchTool";
    private const string ProducerVersion = "1.0.0";
    private const string DefaultModelId = "bones-play-turn";
    private const int DefaultTargetScore = 100;

    private readonly IArtifactStore _artifactStore;
    private readonly BonesGameEngine _engine;
    private readonly IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> _modelProvider;
    private readonly BonesStrategyScriptHost _scriptHost;
    private readonly BonesStrategyPlayerSlotFactory _playerSlotFactory;
    private readonly BonesLearningViewerCoordinator? _viewerCoordinator;
    private readonly BonesGameSimulationBudget? _gameBudget;
    private readonly BonesStrategyLibrary? _strategyLibrary;

    public BonesPlayMatchTool(
        IArtifactStore artifactStore,
        BonesGameEngine engine,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> modelProvider,
        BonesLearningViewerCoordinator? viewerCoordinator = null,
        BonesStrategyScriptHost? scriptHost = null,
        BonesGameSimulationBudget? gameBudget = null,
        BonesStrategyLibrary? strategyLibrary = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _viewerCoordinator = viewerCoordinator;
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
        _playerSlotFactory = new BonesStrategyPlayerSlotFactory(_scriptHost, _modelProvider);
        _gameBudget = gameBudget;
        _strategyLibrary = strategyLibrary;
    }

    public async ValueTask<BonesPlayMatchResult> ExecuteAsync(
        BonesPlayMatchRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.RepositoryPath));

        var targetScore = request.TargetScore > 0 ? request.TargetScore : DefaultTargetScore;
        var gameId = request.GameId ?? new BonesGameId($"play-{request.Seed}-{Guid.NewGuid():N}");
        var knowledgeStore = new BonesPlayerKnowledgeStore(
            _artifactStore,
            request.RepositoryPath,
            context.SessionId);

        ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact> strategies;
        if (request.AllSeatsLearning && _strategyLibrary is not null)
        {
            strategies = await BuildCoLearningPlayerSlotsAsync(
                request.Seed,
                knowledgeStore,
                cancellationToken);
        }
        else
        {
            strategies = await LoadAllActiveStrategiesAsync(knowledgeStore, cancellationToken);
        }

        if (_viewerCoordinator is not null)
        {
            await _viewerCoordinator.EnsureViewerPublishedAsync(
                context.SessionId,
                request.Seed,
                cancellationToken);
        }

        var scriptPlayerSlots = BuildScriptPlayerSlots(strategies);

        var matchResult = await RunAgentMatchAsync(
            request,
            context,
            gameId,
            targetScore,
            strategies,
            scriptPlayerSlots,
            cancellationToken);

        var transcriptMarkdown = BonesMatchTranscriptBuilder.BuildFullTranscript(matchResult);
        var matchTranscriptArtifact = await SaveMatchTranscriptArtifactAsync(
            context.SessionId,
            gameId,
            transcriptMarkdown,
            cancellationToken);

        var matchHistoryArtifacts = new Dictionary<BonesPlayerId, ArtifactDescriptor>();
        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            var playerMarkdown = BonesMatchTranscriptBuilder.BuildPlayerMatchHistory(playerId, matchResult);
            var historyArtifact = await knowledgeStore.SaveMatchHistory(
                playerId,
                new BonesMatchHistoryTranscript(gameId, playerId, playerMarkdown),
                cancellationToken);

            matchHistoryArtifacts[playerId] = historyArtifact;
        }

        var executionArtifact = await SaveExecutionArtifactAsync(
            context.SessionId,
            request,
            gameId,
            matchResult,
            cancellationToken);

        var matchOutcome = new BonesStrategyMatchOutcome(
            matchResult.Winner,
            matchResult.CumulativeScores);

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            await knowledgeStore.RecordMatchOutcome(
                playerId,
                strategies[playerId].StrategyId,
                matchOutcome,
                cancellationToken);

            await TryUpsertLibraryBestAsync(
                knowledgeStore,
                playerId,
                strategies[playerId],
                cancellationToken);
        }

        if (_viewerCoordinator is not null)
        {
            _viewerCoordinator.PublishLiveMatchState(
                context.SessionId,
                request.Seed,
                matchResult,
                isComplete: true);
        }

        _gameBudget?.RecordGames(1);

        return new BonesPlayMatchResult(
            gameId,
            matchResult.Winner,
            matchResult.CumulativeScores,
            matchResult.Transcript,
            matchResult.Transcript.Length,
            matchTranscriptArtifact,
            executionArtifact,
            matchHistoryArtifacts.ToImmutableDictionary());
    }

    private async ValueTask TryUpsertLibraryBestAsync(
        BonesPlayerKnowledgeStore knowledgeStore,
        BonesPlayerId playerId,
        BonesStrategyArtifact strategy,
        CancellationToken cancellationToken)
    {
        if (_strategyLibrary is null)
            return;

        if (strategy.Kind != BonesStrategyKind.Script)
            return;

        var effectiveness = await knowledgeStore.GetEffectiveness(
            playerId,
            strategy.StrategyId,
            cancellationToken);

        _strategyLibrary.UpsertBestStrategy(playerId, strategy, effectiveness);
    }

    private async ValueTask<ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact>> LoadAllActiveStrategiesAsync(
        BonesPlayerKnowledgeStore knowledgeStore,
        CancellationToken cancellationToken)
    {
        var strategies = ImmutableDictionary.CreateBuilder<BonesPlayerId, BonesStrategyArtifact>();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            strategies[playerId] = await knowledgeStore.LoadActiveStrategy(playerId, cancellationToken);
        }

        return strategies.ToImmutable();
    }

    private async ValueTask<ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact>> BuildCoLearningPlayerSlotsAsync(
        int seed,
        BonesPlayerKnowledgeStore knowledgeStore,
        CancellationToken cancellationToken)
    {
        var strategies = ImmutableDictionary.CreateBuilder<BonesPlayerId, BonesStrategyArtifact>();
        var libraryBestBySeat = _strategyLibrary!.LoadBestStrategiesForAllSeats();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            var activeStrategy = await knowledgeStore.TryLoadActiveStrategy(playerId, cancellationToken);

            var libraryHasEntry = libraryBestBySeat.TryGetValue(playerId, out var libraryStrategy);

            if (activeStrategy is not null && activeStrategy.Kind == BonesStrategyKind.Script)
            {
                // Prefer the freshly-Ponder-authored active Script over a stale library Markdown entry.
                // If the library entry is also Script, prefer the active one (it is fresher).
                strategies[playerId] = activeStrategy;
            }
            else if (libraryHasEntry)
            {
                strategies[playerId] = libraryStrategy!;
            }
            else
            {
                strategies[playerId] = CreateDeterministicSeedFallbackStrategy(playerId, seed);
            }
        }

        return strategies.ToImmutable();
    }

    private static BonesStrategyArtifact CreateDeterministicSeedFallbackStrategy(
        BonesPlayerId playerId,
        int seed)
    {
        var strategyId = new BonesStrategyId($"deterministic-seed-{playerId.Seat}-{seed}");

        var source = $@"
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{{
    private static readonly int Seed = {seed};
    private static readonly int Seat = {playerId.Seat};

    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {{
        if (legalMoves.Count == 0)
            throw new System.InvalidOperationException(""No legal moves."");

        // Deterministic seed-based selection: hash the seed, seat, first tile, and move count
        var fingerprint = System.HashCode.Combine(Seed, Seat, state.CurrentPlayer.Seat, legalMoves.Count);
        var index = (fingerprint & int.MaxValue) % legalMoves.Count;
        return legalMoves[index];
    }}
}}";

        return new BonesStrategyArtifact(
            strategyId,
            playerId,
            BonesStrategyKind.Script,
            source,
            BonesPromotionStatus.Active);
    }

    private ImmutableDictionary<BonesPlayerId, BonesScriptStrategyPlayerSlot> BuildScriptPlayerSlots(
        IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> strategies)
    {
        var scriptSlots = ImmutableDictionary.CreateBuilder<BonesPlayerId, BonesScriptStrategyPlayerSlot>();

        foreach (var (playerId, strategy) in strategies)
        {
            if (strategy.Kind != BonesStrategyKind.Script)
                continue;

            scriptSlots[playerId] = new BonesScriptStrategyPlayerSlot(
                _scriptHost,
                strategy.StrategyId,
                strategy.Source);
        }

        return scriptSlots.ToImmutable();
    }

    private async ValueTask<BonesMatchResult> RunAgentMatchAsync(
        BonesPlayMatchRequest request,
        CapabilityContext context,
        BonesGameId gameId,
        int targetScore,
        IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> strategies,
        IReadOnlyDictionary<BonesPlayerId, BonesScriptStrategyPlayerSlot> scriptPlayerSlots,
        CancellationToken cancellationToken)
    {
        var cumulativeScores = InitializeScores();
        var rounds = new List<BonesMatchRoundRecord>();
        var transcript = new List<BonesEvent>();
        var roundNumber = 0;
        BonesPlayerId? matchWinner = null;
        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? DefaultModelId : request.ModelId;
        var viewerMatchId = BonesViewerUrlBuilder.CreateLearningMatchId(context.SessionId);

        while (matchWinner is null)
        {
            roundNumber++;
            var roundSeed = HashCode.Combine(request.Seed, roundNumber);
            var roundGameId = new BonesGameId($"{gameId.Value}-r{roundNumber}");
            var roundConfig = new BonesRoundConfig(roundGameId, roundSeed);
            var state = _engine.StartRound(roundConfig);

            while (!_engine.IsRoundComplete(state))
            {
                var activePlayer = state.CurrentPlayer;
                var legalMoves = _engine.GetLegalMoves(state, activePlayer);
                var selectedMove = await ChooseMoveAsync(
                    request,
                    context,
                    activePlayer,
                    strategies[activePlayer],
                    scriptPlayerSlots,
                    state,
                    legalMoves,
                    modelId,
                    cancellationToken);

                state = _engine.ApplyMove(state, selectedMove);

                if (_viewerCoordinator is not null)
                {
                    var liveTranscript = BuildLiveTranscript(transcript, state.EventLog);
                    _viewerCoordinator.PublishLiveMatchState(
                        context.SessionId,
                        request.Seed,
                        BonesPublicMatchStateBuilder.CreateInProgress(
                            viewerMatchId,
                            liveTranscript,
                            rounds,
                            cumulativeScores),
                        isComplete: false);
                }
            }

            var roundScore = _engine.ScoreRound(state);
            rounds.Add(new BonesMatchRoundRecord(roundNumber, roundScore, state.EventLog));
            AppendRoundToTranscript(transcript, state.EventLog);

            cumulativeScores = cumulativeScores.SetItem(
                roundScore.Winner,
                cumulativeScores[roundScore.Winner] + roundScore.Points);

            if (cumulativeScores[roundScore.Winner] >= targetScore)
                matchWinner = roundScore.Winner;
        }

        return new BonesMatchResult(
            gameId,
            matchWinner.Value,
            cumulativeScores,
            rounds,
            transcript);
    }

    private async ValueTask<BonesMove> ChooseMoveAsync(
        BonesPlayMatchRequest request,
        CapabilityContext context,
        BonesPlayerId activePlayer,
        BonesStrategyArtifact strategy,
        IReadOnlyDictionary<BonesPlayerId, BonesScriptStrategyPlayerSlot> scriptPlayerSlots,
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves,
        string modelId,
        CancellationToken cancellationToken)
    {
        if (legalMoves.Count == 0)
            throw new InvalidOperationException($"No legal moves available for seat {activePlayer.Seat}.");

        if (strategy.Kind == BonesStrategyKind.Script)
        {
            if (!scriptPlayerSlots.TryGetValue(activePlayer, out var scriptPlayerSlot))
            {
                throw new InvalidOperationException(
                    $"Script player slot is not bound for seat {activePlayer.Seat}.");
            }

            return scriptPlayerSlot.ChooseMove(state, legalMoves);
        }

        var markdownSlot = _playerSlotFactory.CreatePlayerSlot(
            strategy,
            new BonesStrategyPlayerSlotFactoryContext(context, modelId));

        return markdownSlot.ChooseMove(state, legalMoves);
    }

    private static ImmutableDictionary<BonesPlayerId, int> InitializeScores()
    {
        var scores = ImmutableDictionary.CreateBuilder<BonesPlayerId, int>();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            scores.Add(new BonesPlayerId(seat), 0);

        return scores.ToImmutable();
    }

    private static List<BonesEvent> BuildLiveTranscript(
        IReadOnlyList<BonesEvent> completedTranscript,
        IReadOnlyList<BonesEvent> currentRoundEvents)
    {
        var liveTranscript = new List<BonesEvent>(completedTranscript.Count + currentRoundEvents.Count);
        liveTranscript.AddRange(completedTranscript);

        var turnOffset = completedTranscript.Count;
        foreach (var roundEvent in currentRoundEvents)
        {
            liveTranscript.Add(new BonesEvent(
                turnOffset + roundEvent.TurnIndex,
                roundEvent.PlayerId,
                roundEvent.Kind,
                roundEvent.Tile,
                roundEvent.Side));
        }

        return liveTranscript;
    }

    private static void AppendRoundToTranscript(List<BonesEvent> transcript, IReadOnlyList<BonesEvent> roundEvents)
    {
        var turnOffset = transcript.Count;
        foreach (var roundEvent in roundEvents)
        {
            transcript.Add(new BonesEvent(
                turnOffset + roundEvent.TurnIndex,
                roundEvent.PlayerId,
                roundEvent.Kind,
                roundEvent.Tile,
                roundEvent.Side));
        }
    }

    private async ValueTask<ArtifactDescriptor> SaveMatchTranscriptArtifactAsync(
        SessionId sessionId,
        BonesGameId gameId,
        string markdown,
        CancellationToken cancellationToken)
    {
        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-play-match-transcript-{gameId.Value}"),
                kind: ArtifactKind.Markdown,
                fileName: $"bones-play-match-transcript-{gameId.Value}",
                content: markdown,
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async ValueTask<ArtifactDescriptor> SaveExecutionArtifactAsync(
        SessionId sessionId,
        BonesPlayMatchRequest request,
        BonesGameId gameId,
        BonesMatchResult matchResult,
        CancellationToken cancellationToken)
    {
        var payload = new BonesPlayMatchExecutionLog(
            SessionId: sessionId,
            GameId: gameId.Value,
            Seed: request.Seed,
            TargetScore: request.TargetScore > 0 ? request.TargetScore : DefaultTargetScore,
            WinnerSeat: matchResult.Winner.Seat,
            TurnsPlayed: matchResult.Transcript.Length,
            FinalScores: matchResult.CumulativeScores.ToDictionary(
                entry => entry.Key.Seat,
                entry => entry.Value));

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-play-match-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-play-match-execution",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private sealed record BonesPlayMatchExecutionLog(
        SessionId SessionId,
        string GameId,
        int Seed,
        int TargetScore,
        int WinnerSeat,
        int TurnsPlayed,
        IReadOnlyDictionary<int, int> FinalScores);
}
