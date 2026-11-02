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
    private readonly StrategyPool? _strategyPool;
    private readonly PopulationMatchComposer? _matchComposer;
    private readonly SemaphoreSlim? _concurrencyThrottle;

    public BonesPlayMatchTool(
        IArtifactStore artifactStore,
        BonesGameEngine engine,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> modelProvider,
        BonesLearningViewerCoordinator? viewerCoordinator = null,
        BonesStrategyScriptHost? scriptHost = null,
        BonesGameSimulationBudget? gameBudget = null,
        BonesStrategyLibrary? strategyLibrary = null,
        StrategyPool? strategyPool = null,
        PopulationMatchComposer? matchComposer = null,
        SemaphoreSlim? concurrencyThrottle = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _viewerCoordinator = viewerCoordinator;
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
        _playerSlotFactory = new BonesStrategyPlayerSlotFactory(_scriptHost, _modelProvider);
        _gameBudget = gameBudget;
        _strategyLibrary = strategyLibrary;
        _strategyPool = strategyPool;
        _matchComposer = matchComposer;
        _concurrencyThrottle = concurrencyThrottle;
    }

    public async ValueTask<BonesPlayMatchResult> ExecuteAsync(
        BonesPlayMatchRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.RepositoryPath));

        var concurrentGameCount = Math.Max(1, request.ConcurrentGameCount);

        if (concurrentGameCount == 1)
        {
            return await PlaySingleGameAsync(request, request.GameId, context, cancellationToken);
        }

        // Parallel execution: play N matches concurrently, each with distinct artifacts.
        var tasks = new Task<BonesPlayMatchResult>[concurrentGameCount];
        for (var i = 0; i < concurrentGameCount; i++)
        {
            var gameIndex = i;
            tasks[i] = Task.Run(async () =>
            {
                if ((_concurrencyThrottle ?? _gameBudget?.GameConcurrencyThrottle) is not null)
                    await (_concurrencyThrottle ?? _gameBudget?.GameConcurrencyThrottle)!.WaitAsync(cancellationToken);
                try
                {
                    var perGameId = new BonesGameId(
                        $"play-{request.Seed}-g{gameIndex}-{Guid.NewGuid():N}");
                    return await PlaySingleGameAsync(request, perGameId, context, cancellationToken);
                }
                finally
                {
                    (_concurrencyThrottle ?? _gameBudget?.GameConcurrencyThrottle)?.Release();
                }
            }, cancellationToken);
        }

        var results = await Task.WhenAll(tasks);

        // Return the first result as the primary (API compatibility).
        // Each game independently recorded its outcomes and knowledge.
        return results[0];
    }

    private async ValueTask<BonesPlayMatchResult> PlaySingleGameAsync(
        BonesPlayMatchRequest request,
        BonesGameId? explicitGameId,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var reservationHeld = false;
        if (_gameBudget is not null)
        {
            if (!_gameBudget.TryReserve(1))
            {
                throw new BonesGameSimulationBudgetExhaustedException(
                    $"Game budget exhausted before play: {_gameBudget.GamesSimulated}/{_gameBudget.MaxGamesPerRun} games used, {_gameBudget.ReservedGames} reserved.");
            }

            reservationHeld = true;
        }

        try
        {
            var result = await PlaySingleGameCoreAsync(request, explicitGameId, context, cancellationToken);
            _gameBudget?.RecordGames(1);
            reservationHeld = false;
            return result;
        }
        finally
        {
            if (reservationHeld)
                _gameBudget?.ReleaseReservation(1);
        }
    }

    private async ValueTask<BonesPlayMatchResult> PlaySingleGameCoreAsync(
        BonesPlayMatchRequest request,
        BonesGameId? explicitGameId,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var targetScore = request.TargetScore > 0 ? request.TargetScore : DefaultTargetScore;
        var gameId = explicitGameId ?? request.GameId ?? new BonesGameId($"play-{request.Seed}-{Guid.NewGuid():N}");
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
                crossSeatConfig: null,
                cancellationToken);
        }
        else
        {
            // I7: Use pool-driven match composition when pool has enough entries.
            if (_matchComposer is not null && _strategyPool is not null && _strategyPool.Count >= 4)
            {
                strategies = ComposePoolDrivenStrategies(_strategyPool);
            }
            else
            {
                strategies = await LoadAllActiveStrategiesAsync(knowledgeStore, cancellationToken);
            }
        }

        // Pool composition and active-strategy loading can both miss a seat after
        // promotion/pruning. Never let a match start with an incomplete seat map.
        strategies = await EnsureCompleteSeatCoverageAsync(strategies, knowledgeStore, request.Seed, cancellationToken);

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

        // I5: Record outcome in the population pool for ELO updates.
        if (_strategyPool is not null)
        {
            var seatToStrategyId = new Dictionary<int, BonesStrategyId>();
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                if (strategies.TryGetValue(playerId, out var strategyArtifact))
                {
                    seatToStrategyId[seat] = strategyArtifact.StrategyId;
                }
            }

            _strategyPool.RecordOutcome(matchResult, seatToStrategyId);
        }

        if (_viewerCoordinator is not null)
        {
            _viewerCoordinator.PublishLiveMatchState(
                context.SessionId,
                request.Seed,
                matchResult,
                isComplete: true);
        }

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

    /// <summary>
    /// Composes match strategies from the population pool using
    /// <see cref="PopulationMatchComposer"/>. Converts pool entries
    /// to <see cref="BonesStrategyArtifact"/> for use by the match engine.
    /// </summary>
    private ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact> ComposePoolDrivenStrategies(
        StrategyPool pool)
    {
        var composition = _matchComposer!.ComposeMatch(pool);
        var strategies = ImmutableDictionary.CreateBuilder<BonesPlayerId, BonesStrategyArtifact>();

        foreach (var entry in composition.Entries)
        {
            var playerId = new BonesPlayerId(entry.Seat);
            var artifact = new BonesStrategyArtifact(
                entry.Id,
                playerId,
                entry.Kind,
                entry.Source,
                BonesPromotionStatus.Active);
            strategies[playerId] = artifact;
        }

        return strategies.ToImmutable();
    }

    private async ValueTask<ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact>> EnsureCompleteSeatCoverageAsync(
        ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact> strategies,
        BonesPlayerKnowledgeStore knowledgeStore,
        int seed,
        CancellationToken cancellationToken)
    {
        var builder = strategies.ToBuilder();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (builder.ContainsKey(playerId))
                continue;

            var activeStrategy = await knowledgeStore.TryLoadActiveStrategy(playerId, cancellationToken);
            builder[playerId] = activeStrategy
                ?? CreateDeterministicSeedFallbackStrategy(playerId, seed);
        }

        return builder.ToImmutable();
    }

    private async ValueTask<ImmutableDictionary<BonesPlayerId, BonesStrategyArtifact>> BuildCoLearningPlayerSlotsAsync(
        int seed,
        BonesPlayerKnowledgeStore knowledgeStore,
        BonesCrossSeatMatchConfig? crossSeatConfig,
        CancellationToken cancellationToken)
    {
        var strategies = ImmutableDictionary.CreateBuilder<BonesPlayerId, BonesStrategyArtifact>();
        var libraryBestBySeat = _strategyLibrary!.LoadBestStrategiesForAllSeats();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            var effectivePlayerId = crossSeatConfig is not null
                ? crossSeatConfig.GetDeployedSeat(playerId)
                : playerId;

            var activeStrategy = await knowledgeStore.TryLoadActiveStrategy(playerId, cancellationToken);

            var libraryHasEntry = libraryBestBySeat.TryGetValue(playerId, out var libraryStrategy);

            if (activeStrategy is not null && activeStrategy.Kind == BonesStrategyKind.Script)
            {
                // When cross-seat is active, remap: strategy authored for playerId plays at effectivePlayerId.
                // Prefer the freshly-Ponder-authored active Script over a stale library Markdown entry.
                // If the library entry is also Script, prefer the active one (it is fresher).
                strategies[effectivePlayerId] = activeStrategy;
            }
            else if (libraryHasEntry)
            {
                strategies[effectivePlayerId] = libraryStrategy!;
            }
            else
            {
                strategies[effectivePlayerId] = CreateDeterministicSeedFallbackStrategy(effectivePlayerId, seed);
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

        // Deterministic seed-based selection. Do NOT use System.HashCode here: it is
        // randomized per process, which would make the fallback strategy change between runs.
        var fingerprint = unchecked((Seed * 397) ^ (Seat * 31) ^ (state.CurrentPlayer.Seat * 17) ^ legalMoves.Count);
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
                strategy.Source,
                executionMode: BonesScriptExecutionMode.Direct);
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
            var roundSeed = BonesSeedDerivation.Derive(request.Seed, roundNumber);
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
