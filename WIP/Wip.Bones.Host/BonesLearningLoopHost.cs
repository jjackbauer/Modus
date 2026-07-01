using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Microsoft.Extensions.Logging;

namespace Wip.Bones.Host;

public sealed class BonesLearningLoopHost : BackgroundService
{
    private readonly BonesHostOptions _options;
    private readonly BonesHostSessionStore _sessionStore;
    private readonly BonesLoopState _state;
    private readonly BonesGameSimulationBudget _gameBudget;
    private readonly BonesStrategyPromotionOptions _promotionOptions;
    private readonly BonesStrategyBootstrapper _strategyBootstrapper;
    private readonly BonesStrategyLibrary _strategyLibrary;
    private readonly WipRuntimeOrchestrator _orchestrator;
    private readonly WipBuilder _workflowBuilder;
    private readonly IArtifactStore _artifactStore;
    private readonly ILogger<BonesLearningLoopHost> _logger;
    private readonly SemaphoreSlim _resumeGate = new(0, 1);
    private long _nextIterationNumber;

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
        IArtifactStore artifactStore,
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
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
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

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_state.StopRequested)
                {
                    _state.IsRunning = false;
                    await WaitForResumeOrStopAsync(stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    _state.IsRunning = true;
                    continue;
                }

                var plannedGameBudget = ComputePlannedIterationGameBudget();
                if (!_gameBudget.TryReserve(plannedGameBudget))
                {
                    _state.CapReached = true;
                    _state.IsRunning = false;
                    _state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                        _state.IterationCount,
                        "CapReached",
                        _state.CurrentIteration.SessionId,
                        _state.CurrentIteration.MatchId,
                        true));
                    break;
                }

                var iterationNumber = Interlocked.Increment(ref _nextIterationNumber);
                var baseSeed = HashCode.Combine(
                    _options.DefaultParameters.Seed,
                    _sessionStore.HostSessionId.GetHashCode());

                var sessionTasks = new Task[_options.ParallelSessionCount];
                for (var sessionIndex = 0; sessionIndex < _options.ParallelSessionCount; sessionIndex++)
                {
                    var capturedSessionIndex = sessionIndex;
                    sessionTasks[sessionIndex] = Task.Run(async () =>
                    {
                        await concurrencyThrottle.WaitAsync(stoppingToken);
                        try
                        {
                            await RunSingleSessionAsync(
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
                    await Task.WhenAll(sessionTasks);
                    _state.IterationCount = iterationNumber;
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

    private async Task RunSingleSessionAsync(
        long iterationNumber,
        int sessionIndex,
        int baseSeed,
        CancellationToken cancellationToken)
    {
        SessionId? workflowSessionId = null;
        var sessionSeed = HashCode.Combine(baseSeed, (int)iterationNumber, sessionIndex);
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
                await _orchestrator.RunWorkflowAsync(
                    snapshot.SessionId,
                    _workflowBuilder,
                    BonesLearningWorkflowIds.WorkflowId,
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
        }
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
            var content = await File.ReadAllTextAsync(artifactPath, cancellationToken);
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
                var content = await File.ReadAllTextAsync(artifactPath, cancellationToken);
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
