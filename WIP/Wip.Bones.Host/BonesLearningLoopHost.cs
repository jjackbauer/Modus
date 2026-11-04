using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Microsoft.Extensions.Logging;

namespace Wip.Bones.Host;

public sealed class BonesLearningLoopHost : BackgroundService
{
    /// <summary>
    /// Number of completed iterations between periodic pool snapshot saves.
    /// Default: every 10 iterations.
    /// </summary>
    internal const int SnapshotIntervalIterations = 10;

    private readonly BonesHostOptions _options;
    private readonly BonesHostSessionStore _sessionStore;
    private readonly BonesLoopState _state;
    private readonly BonesGameSimulationBudget _gameBudget;
    private readonly BonesStrategyPromotionOptions _promotionOptions;
    private readonly BonesStrategyBootstrapper _strategyBootstrapper;
    private readonly BonesStrategyLibrary _strategyLibrary;
    private readonly WipRuntimeOrchestrator _orchestrator;
    private readonly WipBuilder _workflowBuilder;
    private readonly IServiceProvider _services;
    private readonly IArtifactStore _artifactStore;
    private readonly StrategyPool _strategyPool;
    private readonly ILogger<BonesLearningLoopHost> _logger;
    private readonly SemaphoreSlim _resumeGate = new(0, 1);
    private long _nextIterationNumber;
    private int _iterationsSinceLastSnapshot;
    private int _gamesSinceLastEval;
    private int _lastGamesSimulatedSnapshot;
    private int _consecutiveFailures;

    public BonesLearningLoopHost(
        BonesHostOptions options,
        BonesHostSessionStore sessionStore,
        BonesLoopState state,
        BonesGameSimulationBudget gameBudget,
        BonesStrategyPromotionOptions promotionOptions,
        BonesStrategyBootstrapper strategyBootstrapper,
        BonesStrategyLibrary strategyLibrary,
        WipRuntimeOrchestrator orchestrator,
        WipBuilder workflowBuilder,
        IServiceProvider services,
        IArtifactStore artifactStore,
        StrategyPool strategyPool,
        ILogger<BonesLearningLoopHost> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _gameBudget = gameBudget ?? throw new ArgumentNullException(nameof(gameBudget));
        _promotionOptions = promotionOptions ?? throw new ArgumentNullException(nameof(promotionOptions));
        _strategyBootstrapper = strategyBootstrapper ?? throw new ArgumentNullException(nameof(strategyBootstrapper));
        _strategyLibrary = strategyLibrary ?? throw new ArgumentNullException(nameof(strategyLibrary));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _workflowBuilder = workflowBuilder ?? throw new ArgumentNullException(nameof(workflowBuilder));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _strategyPool = strategyPool ?? throw new ArgumentNullException(nameof(strategyPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public BonesLoopState CurrentState => _state;

    public void RequestStop()
    {
        _state.StopRequested = true;
    }

    public void RequestStart()
    {
        _state.StopRequested = false;
        if (!_state.IsRunning)
        {
            try
            {
                _resumeGate.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.HostSessionId = _sessionStore.HostSessionId;
        _state.IsRunning = true;

        if (_options.RequireLibraryBootstrap)
        {
            var learningPlayerId = _options.DefaultParameters.LearningPlayerId;
            var ranking = _strategyLibrary.GetRanking(learningPlayerId);
            if (ranking.Count == 0)
            {
                _state.IsRunning = false;
                throw new InvalidOperationException(
                    $"RequireLibraryBootstrap is enabled but no library entries exist for player seat {learningPlayerId.Seat}.");
            }
        }

        var maxConcurrency = Math.Min(_options.ParallelSessionCount, Environment.ProcessorCount);
        using var concurrencyThrottle = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        // Total game concurrency throttle: sessions * concurrent games per session.
        var gameConcurrencyLimit = _options.ParallelSessionCount
            * _options.AggressiveIteration.ConcurrentGamesPerSession;
        using var gameConcurrencyThrottle = new SemaphoreSlim(gameConcurrencyLimit, gameConcurrencyLimit);
        _gameBudget.GameConcurrencyThrottle = gameConcurrencyThrottle;
        _gameBudget.IsPerSessionBudget = _options.AggressiveIteration.PerSessionBudget;
        _gameBudget.SessionCount = _options.ParallelSessionCount;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_state.CapReached || _gameBudget.IsExhausted)
                {
                    _state.CapReached = true;
                    _state.StopReason ??= "GameBudgetExhausted";
                    break;
                }

                if (_state.StopRequested)
                {
                    _state.IsRunning = false;
                    await WaitForResumeOrStopAsync(stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    _state.IsRunning = true;
                    continue;
                }

                var iterationNumber = Interlocked.Increment(ref _nextIterationNumber);
                // Reproducibility: derive from the configured seed only. HostSessionId is a
                // per-process GUID; including it made every run use a different seed.
                var baseSeed = BonesSeedDerivation.Derive(_options.DefaultParameters.Seed);

                var sessionTasks = new Task<bool>[_options.ParallelSessionCount];
                for (var sessionIndex = 0; sessionIndex < _options.ParallelSessionCount; sessionIndex++)
                {
                    var capturedSessionIndex = sessionIndex;
                    sessionTasks[sessionIndex] = Task.Run(async () =>
                    {
                        await concurrencyThrottle.WaitAsync(stoppingToken);
                        try
                        {
                            return await RunSingleSessionAsync(
                                iterationNumber,
                                capturedSessionIndex,
                                baseSeed,
                                stoppingToken);
                        }
                        finally
                        {
                            concurrencyThrottle.Release();
                        }
                    }, stoppingToken);
                }

                try
                {
                    var sessionResults = await Task.WhenAll(sessionTasks);
                    _state.IterationCount = iterationNumber;

                    var anyFailure = sessionResults.Any(static succeeded => !succeeded);
                    if (_state.CapReached)
                        break;

                    if (anyFailure)
                    {
                        _consecutiveFailures++;
                        if (_consecutiveFailures >= _options.MaxConsecutiveIterationFailures)
                        {
                            _state.StopReason = $"ConsecutiveIterationFailures({_consecutiveFailures})";
                            _state.IsRunning = false;
                            _logger.LogError(
                                "Stopping Bones learning loop after {FailureCount} consecutive iteration failures. Last error: {LastError}",
                                _consecutiveFailures,
                                _state.LastIterationError?.Message ?? "(none)");
                            break;
                        }

                        var backoffMs = Math.Min(
                            _options.FailureBackoffBaseMs * Math.Pow(2, _consecutiveFailures - 1),
                            _options.FailureBackoffMaxMs);
                        _logger.LogWarning(
                            "Bones iteration {IterationNumber} failed; backing off {BackoffMs:F0} ms before retry (consecutive failures: {FailureCount}).",
                            iterationNumber,
                            backoffMs,
                            _consecutiveFailures);
                        await Task.Delay(TimeSpan.FromMilliseconds(backoffMs), stoppingToken);
                    }
                    else
                    {
                        _consecutiveFailures = 0;
                    }

                    // I1: Periodic snapshot persistence — decoupled from EvaluateAndPromote.
                    if (Interlocked.Increment(ref _iterationsSinceLastSnapshot) >= SnapshotIntervalIterations)
                    {
                        Interlocked.Exchange(ref _iterationsSinceLastSnapshot, 0);
                        TrySavePoolSnapshot();
                    }

                    // I9: Trigger EvaluateAndPromote periodically based on games played.
                    TryEvaluateAndPromote();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(
                        exception,
                        "Unexpected error outside iteration scope for iteration {IterationNumber}.",
                        iterationNumber);
                }

                if (_state.StopRequested || stoppingToken.IsCancellationRequested)
                    continue;

                if (_options.IterationDelayMs > 0)
                    await Task.Delay(_options.IterationDelayMs, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _state.IsRunning = false;
            _state.StopReason ??= _state.CapReached ? "GameBudgetExhausted" : "Stopped";
            var stageName = _state.CapReached ? "CapReached" : "Stopped";
            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                _state.IterationCount,
                stageName,
                _state.CurrentIteration.SessionId,
                _state.CurrentIteration.MatchId,
                true));
        }
    }

    internal int ComputePlannedIterationGameBudget()
        => _options.DefaultParameters.GameCount
            + 1
            + _promotionOptions.EvaluationMatchCount * 2;

    /// <summary>
    /// Persists the pool snapshot to disk if the pool is non-empty and a snapshot
    /// path is configured. Logs success or warning. Does not throw on I/O errors.
    /// </summary>
    internal void TrySavePoolSnapshot()
    {
        try
        {
            if (_strategyPool.Count == 0)
                return;

            var snapshotPath = _strategyPool.SnapshotPath;
            if (snapshotPath is null)
                return;

            _strategyPool.PersistToDisk(snapshotPath);
            _logger.LogInformation(
                "Periodic pool snapshot saved: {EntryCount} entries to {SnapshotPath}",
                _strategyPool.Count,
                snapshotPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save periodic pool snapshot.");
        }
    }

    /// <summary>
    /// Checks whether enough games have been played since the last evaluation
    /// cycle and triggers <see cref="StrategyPool.EvaluateAndPromote"/> if so.
    /// Resets the counter and logs the outcome.
    /// </summary>
    internal void TryEvaluateAndPromote()
    {
        try
        {
            // Track games played since last evaluation using the game budget.
            var currentGames = _gameBudget.GamesSimulated;
            var delta = currentGames - _lastGamesSimulatedSnapshot;
            _lastGamesSimulatedSnapshot = currentGames;
            _gamesSinceLastEval += delta;

            var threshold = _options.Population.EvaluationCycleGameCount;
            if (_gamesSinceLastEval < threshold)
                return;

            _gamesSinceLastEval = 0;

            if (_strategyPool.Count == 0)
                return;

            var results = _strategyPool.EvaluateAndPromote();

            _logger.LogInformation(
                "Evaluation cycle complete: {Promoted} survivors, {Rejected} rejected, " +
                "new elite: {EliteId}",
                results.Promoted,
                results.Rejected,
                _strategyPool.EliteStrategyId?.Value ?? "(none)");

            // I10: Prune over-capacity pool after evaluation.
            if (_strategyPool.Count > _options.Population.MaxPopulation)
            {
                _strategyPool.Prune(_options.Population.MaxPopulation);
                _logger.LogInformation(
                    "Pool pruned to {MaxPopulation} entries.",
                    _options.Population.MaxPopulation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run evaluation cycle.");
        }
    }

    private async Task WaitForResumeOrStopAsync(CancellationToken stoppingToken)
    {
        while (_state.StopRequested && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _resumeGate.WaitAsync(TimeSpan.FromMilliseconds(250), stoppingToken);
                if (!_state.StopRequested)
                    return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<bool> RunSingleSessionAsync(
        long iterationNumber,
        int sessionIndex,
        int baseSeed,
        CancellationToken cancellationToken)
    {
        SessionId? workflowSessionId = null;
        var sessionSeed = BonesSeedDerivation.Derive(baseSeed, (int)iterationNumber, sessionIndex);
        var sessionLabel = _options.ParallelSessionCount > 1
            ? $"iter{iterationNumber}/s{sessionIndex}"
            : $"iter{iterationNumber}";
        string? sessionId = null;
        string? matchId = null;

        try
        {
            var parameters = _options.DefaultParameters with { Seed = sessionSeed };
            var context = _sessionStore.BeginIteration(
                iterationNumber,
                parameters,
                Path.GetFullPath(_options.DataDirectory));

            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                iterationNumber,
                $"{sessionLabel}: Starting",
                null,
                null,
                false));

            Directory.CreateDirectory(context.RepositoryPath);
            Directory.CreateDirectory(context.WorktreePath);

            var snapshot = await _orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: BonesLearningWorkflowIds.WorkflowId,
                    TaskDescription: context.TaskDescription,
                    RepositoryPath: context.RepositoryPath,
                    WorktreePath: context.WorktreePath),
                cancellationToken);

            workflowSessionId = snapshot.SessionId;
            matchId = BonesViewerUrlBuilder.CreateLearningMatchId(snapshot.SessionId).Value;
            sessionId = snapshot.SessionId.Value;
            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                iterationNumber,
                $"{sessionLabel}: Observe",
                sessionId,
                matchId,
                false));

            await _strategyBootstrapper.BootstrapAsync(
                _artifactStore,
                snapshot.SessionId,
                context.RepositoryPath,
                _options.ResumeFromBest,
                cancellationToken);

            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                iterationNumber,
                $"{sessionLabel}: Ponder",
                sessionId,
                matchId,
                false));

            using var stageMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stageMonitorTask = BonesLearningLoopStageMonitor.MonitorAsync(
                _artifactStore,
                snapshot.SessionId,
                iterationNumber,
                sessionId,
                matchId,
                _state,
                stageMonitorCts.Token,
                _logger);

            try
            {
                await using var runScope = _services.CreateAsyncScope();
                await _orchestrator.RunWorkflowAsync(
                    snapshot.SessionId,
                    _workflowBuilder,
                    BonesLearningWorkflowIds.WorkflowId,
                    runScope.ServiceProvider,
                    cancellationToken,
                    _artifactStore);
            }
            finally
            {
                stageMonitorCts.Cancel();
                try
                {
                    await stageMonitorTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _state.ClearLastIterationError();
            _state.LastPromotionOutcome = await TryReadLastPromotionOutcomeAsync(
                snapshot.SessionId,
                cancellationToken);

            var promotionOutcomes = await TryReadAllSeatPromotionOutcomesAsync(
                snapshot.SessionId,
                cancellationToken);
            if (promotionOutcomes.Count > 0)
            {
                _state.CoLearningMetrics.RecordIterationCompleted(promotionOutcomes);
            }

            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                iterationNumber,
                $"{sessionLabel}: Complete",
                sessionId,
                matchId,
                true));
            return true;
        }
        catch (BonesGameSimulationBudgetExhaustedException exception)
        {
            _state.CapReached = true;
            _state.StopReason = "GameBudgetExhausted";
            _state.SetLastIterationError(new BonesLoopIterationError(exception.Message, "Budget"));
            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                iterationNumber,
                $"{sessionLabel}: CapReached",
                sessionId,
                matchId,
                true,
                "Budget"));
            _logger.LogWarning(
                "Bones learning {SessionLabel} stopped at the configured game budget: {Message}",
                sessionLabel,
                exception.Message);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failureStage = "Workflow";
            if (workflowSessionId is not null)
            {
                try
                {
                    var descriptors = await _artifactStore.ListAsync(workflowSessionId.Value, cancellationToken);
                    failureStage = BonesLearningLoopStageMonitor.InferFailureStage(descriptors) ?? failureStage;

                    await BonesWorkflowStageFailureRecorder.SaveAsync(
                        _artifactStore,
                        workflowSessionId.Value,
                        failureStage,
                        exception.Message,
                        cancellationToken);
                }
                catch (Exception recordException)
                {
                    _logger.LogWarning(
                        recordException,
                        "Failed to record workflow stage failure artifact for session {SessionId}.",
                        workflowSessionId.Value.Value);
                }
            }

            _logger.LogError(
                exception,
                "Bones learning {SessionLabel} failed at stage {FailureStage} for session {SessionId}.",
                sessionLabel,
                failureStage,
                sessionId ?? "(none)");

            _state.SetLastIterationError(new BonesLoopIterationError(exception.Message, failureStage));
            _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                iterationNumber,
                $"{sessionLabel}: Failed",
                sessionId,
                matchId,
                true,
                failureStage));
            return false;
        }
    }

    private static async Task<string> ReadAllTextSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<string?> TryReadLastPromotionOutcomeAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        var descriptors = await _artifactStore.ListAsync(sessionId, cancellationToken);
        var promotionArtifact = descriptors
            .Where(descriptor => descriptor.RelativePath.Contains("bones-strategy-promotion", StringComparison.Ordinal))
            .OrderByDescending(descriptor => descriptor.ProducedAtUtc)
            .FirstOrDefault();

        if (promotionArtifact is null)
            return null;

        var artifactPath = Path.Combine(
            Path.GetFullPath(_options.DataDirectory),
            promotionArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            var content = await ReadAllTextSharedAsync(artifactPath, cancellationToken);
            using var document = System.Text.Json.JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("Outcome", out var outcome))
                return outcome.GetString();

            if (document.RootElement.TryGetProperty("outcome", out var camelOutcome))
                return camelOutcome.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read promotion outcome from {ArtifactPath}.", artifactPath);
        }

        return null;
    }

    private async Task<IReadOnlyList<BonesSeatPromotionOutcome>> TryReadAllSeatPromotionOutcomesAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        var descriptors = await _artifactStore.ListAsync(sessionId, cancellationToken);
        var promotionDescriptors = descriptors
            .Where(descriptor => descriptor.RelativePath.Contains("bones-strategy-promotion", StringComparison.Ordinal))
            .ToList();

        if (promotionDescriptors.Count == 0)
            return Array.Empty<BonesSeatPromotionOutcome>();

        var results = new List<BonesSeatPromotionOutcome>();
        foreach (var descriptor in promotionDescriptors)
        {
            var artifactPath = Path.Combine(
                Path.GetFullPath(_options.DataDirectory),
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                var content = await ReadAllTextSharedAsync(artifactPath, cancellationToken);
                using var document = System.Text.Json.JsonDocument.Parse(content);

                var root = document.RootElement;
                string? outcome = null;
                if (root.TryGetProperty("Outcome", out var pascalOutcome))
                    outcome = pascalOutcome.GetString();
                else if (root.TryGetProperty("outcome", out var camelOutcome))
                    outcome = camelOutcome.GetString();

                int seat = 0;
                if (root.TryGetProperty("Seat", out var pascalSeat))
                    seat = pascalSeat.GetInt32();
                else if (root.TryGetProperty("seat", out var camelSeat))
                    seat = camelSeat.GetInt32();
                else if (root.TryGetProperty("LearningPlayerSeat", out var pascalLearningSeat))
                    seat = pascalLearningSeat.GetInt32();
                else if (root.TryGetProperty("learningPlayerSeat", out var camelLearningSeat))
                    seat = camelLearningSeat.GetInt32();

                if (seat > 0)
                    results.Add(new BonesSeatPromotionOutcome(seat, outcome));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read promotion outcome from {ArtifactPath}.", artifactPath);
            }
        }

        return results;
    }
}
