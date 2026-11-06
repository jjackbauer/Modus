using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Runtime;
using Wip.Bones.Agents.Script;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Enhance;

public sealed class BonesEnhanceStrategyAgent : IAgent<BonesEnhanceStrategyRequest, BonesEnhanceStrategyResult>
{
    private const string ProducerType = "Wip.Bones.Agents.BonesEnhanceStrategyAgent";
    private const string ProducerVersion = "1.0.0";
    private const string DefaultModelId = "bones-strategy-enhance";
    private const string DefaultPlayModelId = "bones-play-turn";
    private const int DefaultEvaluationTargetScore = 8;
    private const double DefaultBaseTemperature = 0.7;
    private const double TemperatureIncrement = 0.2;
    private const double MaxTemperature = 2.0;

    private readonly IArtifactStore _artifactStore;
    private readonly IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>? _modelProvider;
    private readonly BonesStrategyScriptHost _scriptHost;
    private readonly BonesStrategyPromotionEvaluator _promotionEvaluator;
    private readonly BonesStrategyPromotionOptions _promotionOptions;
    private readonly BonesCompileRetryOptions _compileRetryOptions;
    private readonly BonesAggressiveIterationOptions _aggressiveIterationOptions;
    private readonly BonesStrategyLibrary? _strategyLibrary;
    private readonly int? _forcedEvaluationSeed;
    private readonly string _playModelId;
    private readonly ILogger<BonesEnhanceStrategyAgent>? _logger;
    private readonly BonesAgentAdapter<EnhanceModelTurnRequest, EnhanceModelTurnResult>? _adapter;
    private readonly IAgentMemory? _memory;
    private readonly BonesAgentLoopOptions _loopOptions;

    /// <summary>
    /// Legacy compatibility path: calls <see cref="IModelProvider{TRequestPayload,TResultPayload}"/>
    /// directly. Kept source-compatible for the existing tests and the current host wiring; the
    /// runtime path is composed through <see cref="IAgentExecutor"/> by the production constructor.
    /// </summary>
    public BonesEnhanceStrategyAgent(
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult> modelProvider,
        BonesStrategyScriptHost? scriptHost = null,
        BonesStrategyPromotionEvaluator? promotionEvaluator = null,
        BonesStrategyPromotionOptions? promotionOptions = null,
        BonesCompileRetryOptions? compileRetryOptions = null,
        BonesAggressiveIterationOptions? aggressiveIterationOptions = null,
        BonesGameSimulationBudget? gameBudget = null,
        BonesStrategyLibrary? strategyLibrary = null,
        int? forcedEvaluationSeed = null,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider = null,
        string? playModelId = null,
        ILogger<BonesEnhanceStrategyAgent>? logger = null)
        : this(
            artifactStore,
            modelProvider ?? throw new ArgumentNullException(nameof(modelProvider)),
            executor: null,
            scriptHost,
            promotionEvaluator,
            promotionOptions,
            compileRetryOptions,
            aggressiveIterationOptions,
            gameBudget,
            strategyLibrary,
            forcedEvaluationSeed,
            playModelProvider,
            playModelId,
            logger,
            memory: null,
            loopOptions: null)
    {
    }

    /// <summary>
    /// Production constructor: every enhancement model turn runs through the shared runtime agent
    /// loop (<see cref="IAgentExecutor"/>) via the WP1 adapter, with prompts from
    /// <see cref="BonesEnhancePrompts"/>, budgets from <see cref="BonesAgentLoopOptions"/> and
    /// optional session memory.
    /// </summary>
    public BonesEnhanceStrategyAgent(
        IArtifactStore artifactStore,
        IAgentExecutor executor,
        BonesStrategyScriptHost? scriptHost = null,
        BonesStrategyPromotionEvaluator? promotionEvaluator = null,
        BonesStrategyPromotionOptions? promotionOptions = null,
        BonesCompileRetryOptions? compileRetryOptions = null,
        BonesAggressiveIterationOptions? aggressiveIterationOptions = null,
        BonesGameSimulationBudget? gameBudget = null,
        BonesStrategyLibrary? strategyLibrary = null,
        int? forcedEvaluationSeed = null,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider = null,
        string? playModelId = null,
        ILogger<BonesEnhanceStrategyAgent>? logger = null,
        IAgentMemory? memory = null,
        BonesAgentLoopOptions? loopOptions = null)
        : this(
            artifactStore,
            modelProvider: null,
            executor ?? throw new ArgumentNullException(nameof(executor)),
            scriptHost,
            promotionEvaluator,
            promotionOptions,
            compileRetryOptions,
            aggressiveIterationOptions,
            gameBudget,
            strategyLibrary,
            forcedEvaluationSeed,
            playModelProvider,
            playModelId,
            logger,
            memory,
            loopOptions)
    {
    }

    private BonesEnhanceStrategyAgent(
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>? modelProvider,
        IAgentExecutor? executor,
        BonesStrategyScriptHost? scriptHost,
        BonesStrategyPromotionEvaluator? promotionEvaluator,
        BonesStrategyPromotionOptions? promotionOptions,
        BonesCompileRetryOptions? compileRetryOptions,
        BonesAggressiveIterationOptions? aggressiveIterationOptions,
        BonesGameSimulationBudget? gameBudget,
        BonesStrategyLibrary? strategyLibrary,
        int? forcedEvaluationSeed,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider,
        string? playModelId,
        ILogger<BonesEnhanceStrategyAgent>? logger,
        IAgentMemory? memory,
        BonesAgentLoopOptions? loopOptions)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _modelProvider = modelProvider;
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
        _promotionEvaluator = promotionEvaluator
            ?? new BonesStrategyPromotionEvaluator(
                new BonesMatchSimulator(),
                _scriptHost,
                gameBudget,
                playModelProvider);
        _promotionOptions = promotionOptions ?? new BonesStrategyPromotionOptions();
        _compileRetryOptions = compileRetryOptions ?? new BonesCompileRetryOptions();
        _aggressiveIterationOptions = aggressiveIterationOptions ?? new BonesAggressiveIterationOptions();
        _strategyLibrary = strategyLibrary;
        _forcedEvaluationSeed = forcedEvaluationSeed;
        _playModelId = string.IsNullOrWhiteSpace(playModelId) ? DefaultPlayModelId : playModelId;
        _logger = logger;
        _memory = memory;
        _loopOptions = loopOptions ?? new BonesAgentLoopOptions();

        _adapter = executor is null
            ? null
            : new BonesAgentAdapter<EnhanceModelTurnRequest, EnhanceModelTurnResult>(
                executor,
                (turn, context) => BonesEnhancePrompts.CreateExecutionRequest(
                    turn.Messages,
                    turn.ModelId,
                    context.SessionId,
                    turn.MemoryMessages,
                    _loopOptions.ToAgentLoopOptions()),
                (_, _, executionResult, _) => ValueTask.FromResult(
                    new EnhanceModelTurnResult(BonesEnhancePrompts.ReadCompletedMarkdown(executionResult))));
    }

    public async ValueTask<BonesEnhanceStrategyResult> ExecuteAsync(
        BonesEnhanceStrategyRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.RepositoryPath));

        var knowledgeStore = new BonesPlayerKnowledgeStore(
            _artifactStore,
            request.RepositoryPath,
            context.SessionId);

        var scopedKnowledgeStore = knowledgeStore.ForPlayer(request.PlayerId);

        var priorArtifact = await scopedKnowledgeStore.LoadActiveStrategy(request.PlayerId, cancellationToken);
        var priorStrategy = new BonesStrategyDocument(
            priorArtifact.StrategyId,
            request.PlayerId,
            priorArtifact.Source);

        var allMatchHistories = await scopedKnowledgeStore.LoadMatchHistoryTranscripts(
            request.PlayerId,
            cancellationToken);

        // Always feed all available match histories into the enhancement prompt.
        // Prior to T1.2, a non-null MatchGameId filtered to a single match, starving the LLM of context.
        var matchHistories = allMatchHistories;

        var matchHistoryPaths = matchHistories
            .Select(history => BuildMatchHistoryPath(request.PlayerId, history.GameId))
            .ToArray();

        var opponentStrategies = await LoadOpponentActiveStrategiesAsync(
            knowledgeStore,
            request.PlayerId,
            cancellationToken);

        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? DefaultModelId : request.ModelId;
        var correlationId = $"bones-enhance-seat-{request.PlayerId.Seat}-{context.SessionId.Value}";
        var memoryMessages = await LoadMemorySnapshotAsync(context.SessionId, cancellationToken);

        // ---- F2: EnhanceIterationsPerMatch loop ----
        var totalIterations = Math.Max(1, _aggressiveIterationOptions.EnhanceIterationsPerMatch);
        var totalCompileAttempts = 0;
        var totalStagnationDetected = 0;
        BonesStrategyDocument currentPriorStrategy = priorStrategy;
        BonesStrategyArtifact currentPriorArtifact = priorArtifact;
        BonesStrategyId currentStrategyId = BonesStrategyVersioning.NextVersion(
            priorStrategy.StrategyId,
            request.PlayerId,
            Array.Empty<string>().ToHashSet(StringComparer.Ordinal));
        string? finalScriptSource = null;
        BonesPromotionDecision? finalPromotionDecision = null;
        BonesStrategyId finalActiveStrategyId = priorArtifact.StrategyId;
        ArtifactDescriptor? finalPromotionDecisionArtifact = null;
        ArtifactDescriptor? finalStrategyArtifact = null;
        ArtifactDescriptor? finalExecutionArtifact = null;
        BonesStrategyEnhancementRequest? finalEnhancementRequest = null;
        string? finalAssistantMarkdown = null;
        var lastIteration = 0;

        for (var iteration = 1; iteration <= totalIterations; iteration++)
        {
            lastIteration = iteration;
            cancellationToken.ThrowIfCancellationRequested();

            var enhancementRequest = BonesEnhancePromptBuilder.BuildEnhancementRequest(
                request.PlayerId,
                currentPriorStrategy,
                matchHistories,
                matchHistoryPaths,
                opponentStrategies);

            var existingDescriptors = await scopedKnowledgeStore.ListStrategyArtifacts(
                request.PlayerId,
                cancellationToken);
            const string artifactIdPrefix = "bones-strategy-";
            var existingIds = existingDescriptors
                .Select(descriptor => descriptor.ArtifactId.Value.StartsWith(artifactIdPrefix, StringComparison.Ordinal)
                    ? descriptor.ArtifactId.Value[artifactIdPrefix.Length..]
                    : descriptor.ArtifactId.Value)
                .ToHashSet(StringComparer.Ordinal);
            currentStrategyId = BonesStrategyVersioning.NextVersion(
                currentPriorStrategy.StrategyId,
                request.PlayerId,
                existingIds);

            var maxCompileRetries = _compileRetryOptions.MaxCompileRetries;
            string? scriptSource = null;
            string? lastCompileFailureReason = null;
            var compileAttempts = 0;
            var lastModelResponseMarkdown = (string?)null;
            BonesStrategyScriptCompileResult? compileResult = null;

            // ---- F3: stagnation detection state ----
            var stagnationCount = 0;
            var currentTemperature = DefaultBaseTemperature + ((iteration - 1) * TemperatureIncrement);
            if (currentTemperature > MaxTemperature)
                currentTemperature = MaxTemperature;
            var distinctOutputRetries = _aggressiveIterationOptions.RequireDistinctOutput
                ? _aggressiveIterationOptions.DistinctOutputMaxRetries
                : 0;

            for (var attempt = 1; attempt <= maxCompileRetries; attempt++)
            {
                compileAttempts = attempt;

                lastModelResponseMarkdown = await ExecuteModelTurnAsync(
                    enhancementRequest,
                    modelId,
                    $"{correlationId}-iter-{iteration}-attempt-{attempt}",
                    memoryMessages,
                    context,
                    cancellationToken);

                scriptSource = BonesStrategyScriptResponseParser.TryParseScriptSource(lastModelResponseMarkdown);

                if (scriptSource is null)
                {
                    lastCompileFailureReason = "Model response did not contain a compilable IBonesPlayerSlot script.";
                    if (attempt >= maxCompileRetries)
                    {
                        await SaveEnhanceFailureArtifactAsync(
                            context.SessionId,
                            request.PlayerId,
                            maxCompileRetries,
                            lastCompileFailureReason,
                            lastModelResponseMarkdown,
                            cancellationToken);

                        throw new InvalidOperationException(lastCompileFailureReason);
                    }

                    enhancementRequest = BonesEnhancePromptBuilder.AppendCompileRetryMessage(
                        enhancementRequest,
                        lastCompileFailureReason,
                        lastModelResponseMarkdown);
                    continue;
                }

                // ---- F3: RequireDistinctOutput gate ----
                if (_aggressiveIterationOptions.RequireDistinctOutput
                    && IsSourceIdentical(scriptSource, currentPriorStrategy.Markdown))
                {
                    stagnationCount++;
                    totalStagnationDetected++;

                    if (stagnationCount <= distinctOutputRetries)
                    {
                        currentTemperature = Math.Min(
                            currentTemperature + TemperatureIncrement,
                            MaxTemperature);

                        _logger?.LogWarning(
                            "Stagnation detected for seat {Seat} iteration {Iteration} attempt {Attempt}: "
                            + "model returned source identical to input. Temperature increased to {Temperature:F1}.",
                            request.PlayerId.Seat,
                            iteration,
                            attempt,
                            currentTemperature);

                        // Retry with higher temperature — append a retry message.
                        enhancementRequest = BonesEnhancePromptBuilder.AppendCompileRetryMessage(
                            enhancementRequest,
                            "Output was identical to the input strategy. Produce a meaningfully different strategy.",
                            lastModelResponseMarkdown);
                        continue;
                    }

                    var stagnationEx = new BonesStagnationException(
                        $"Model returned byte-identical output to input strategy after "
                        + $"{stagnationCount} stagnation detections and {distinctOutputRetries} temperature-ramp "
                        + $"retries for seat {request.PlayerId.Seat} iteration {iteration}.")
                    {
                        StagnationCount = stagnationCount,
                        FinalTemperature = currentTemperature,
                        PriorStrategyId = currentPriorStrategy.StrategyId.Value,
                    };

                    _logger?.LogError(stagnationEx, stagnationEx.Message);
                    throw stagnationEx;
                }

                compileResult = _scriptHost.TryCompile(currentStrategyId, scriptSource);
                if (compileResult.Succeeded)
                {
                    lastCompileFailureReason = null;
                    break;
                }

                lastCompileFailureReason = compileResult.FailureReason
                    ?? "Enhanced strategy script compilation failed before persistence.";

                if (attempt >= maxCompileRetries)
                {
                    await SaveEnhanceFailureArtifactAsync(
                        context.SessionId,
                        request.PlayerId,
                        maxCompileRetries,
                        lastCompileFailureReason,
                        lastModelResponseMarkdown,
                        cancellationToken);

                    throw new InvalidOperationException(lastCompileFailureReason);
                }

                enhancementRequest = BonesEnhancePromptBuilder.AppendCompileRetryMessage(
                    enhancementRequest,
                    lastCompileFailureReason,
                    lastModelResponseMarkdown);
            }

            // F3: If stagnation retries exhausted all loop attempts without
            // reaching the explicit throw inside the loop, raise it now.
            if (_aggressiveIterationOptions.RequireDistinctOutput
                && stagnationCount > distinctOutputRetries
                && (compileResult is not { Succeeded: true } || scriptSource is null))
            {
                var stagnationEx = new BonesStagnationException(
                    $"Model returned byte-identical output to input strategy after "
                    + $"{stagnationCount} stagnation detections and {distinctOutputRetries} temperature-ramp "
                    + $"retries for seat {request.PlayerId.Seat} iteration {iteration}.")
                {
                    StagnationCount = stagnationCount,
                    FinalTemperature = currentTemperature,
                    PriorStrategyId = currentPriorStrategy.StrategyId.Value,
                };

                _logger?.LogError(stagnationEx, stagnationEx.Message);
                throw stagnationEx;
            }

            if (compileResult is not { Succeeded: true } || scriptSource is null)
            {
                throw new InvalidOperationException(
                    lastCompileFailureReason
                    ?? "Enhanced strategy script compilation failed before persistence.");
            }

            totalCompileAttempts += compileAttempts;
            finalScriptSource = scriptSource;
            finalEnhancementRequest = enhancementRequest;
            finalAssistantMarkdown = lastModelResponseMarkdown;

            var candidateArtifact = new BonesStrategyArtifact(
                currentStrategyId,
                request.PlayerId,
                BonesStrategyKind.Script,
                scriptSource,
                BonesPromotionStatus.Candidate);

            finalStrategyArtifact = await scopedKnowledgeStore.SaveCandidateStrategy(
                request.PlayerId,
                candidateArtifact,
                cancellationToken);

            var evalOpponentStrategies = await LoadOpponentActiveStrategiesAsync(
                knowledgeStore,
                request.PlayerId,
                cancellationToken);

            // Evaluate against the *original* prior artifact (incumbent)
            var evaluationRequest = new BonesStrategyPromotionEvaluationRequest(
                request.PlayerId,
                priorArtifact,
                candidateArtifact,
                evalOpponentStrategies,
                _promotionOptions,
                request.EvaluationSeed ?? _forcedEvaluationSeed
                    ?? ResolveEvaluationSeed(context, priorArtifact.StrategyId),
                request.EvaluationTargetScore ?? DefaultEvaluationTargetScore,
                context,
                _playModelId);

            var promotionDecision = await _promotionEvaluator.EvaluateAsync(
                evaluationRequest,
                cancellationToken,
                scopedKnowledgeStore);

            finalPromotionDecision = promotionDecision;

            if (promotionDecision.Outcome == BonesPromotionDecisionOutcome.Promoted)
            {
                await scopedKnowledgeStore.PromoteStrategy(
                    request.PlayerId,
                    currentStrategyId,
                    cancellationToken);
                finalActiveStrategyId = currentStrategyId;
                await TryUpsertLibraryBestAsync(
                    scopedKnowledgeStore,
                    request.PlayerId,
                    candidateArtifact,
                    cancellationToken);
            }
            else
            {
                var rejected = new BonesStrategyArtifact(
                    candidateArtifact.StrategyId,
                    candidateArtifact.PlayerId,
                    candidateArtifact.Kind,
                    candidateArtifact.Source,
                    BonesPromotionStatus.Rejected);

                await scopedKnowledgeStore.SaveStrategyArtifact(
                    request.PlayerId,
                    rejected,
                    cancellationToken);

                // F2: Early exit — if rejected and not the final iteration, stop iterating
                if (iteration < totalIterations)
                {
                    _logger?.LogWarning(
                        "Enhance iteration {Iteration}/{Total} for seat {Seat} was rejected. "
                        + "Exiting early to avoid wasted work.",
                        iteration,
                        totalIterations,
                        request.PlayerId.Seat);

                    finalActiveStrategyId = priorArtifact.StrategyId;
                    break;
                }

                finalActiveStrategyId = priorArtifact.StrategyId;
            }

            finalPromotionDecisionArtifact = await SavePromotionDecisionArtifactAsync(
                context.SessionId,
                request.PlayerId,
                promotionDecision,
                cancellationToken);

            finalExecutionArtifact = await SaveExecutionArtifactAsync(
                context.SessionId,
                request,
                currentPriorStrategy.StrategyId,
                currentStrategyId,
                matchHistories.Count,
                compileAttempts,
                promotionDecision.Outcome,
                cancellationToken);

            // F2: If promoted and more iterations remain, feed output as input for next iteration.
            if (iteration < totalIterations
                && promotionDecision.Outcome == BonesPromotionDecisionOutcome.Promoted)
            {
                currentPriorStrategy = new BonesStrategyDocument(
                    currentStrategyId,
                    request.PlayerId,
                    scriptSource);

                currentPriorArtifact = new BonesStrategyArtifact(
                    currentStrategyId,
                    request.PlayerId,
                    BonesStrategyKind.Script,
                    scriptSource,
                    BonesPromotionStatus.Active);
            }
        }

        await AppendMemoryExchangeAsync(
            context.SessionId,
            finalEnhancementRequest,
            finalAssistantMarkdown,
            cancellationToken);

        var strategyDocument = new BonesStrategyDocument(
            currentStrategyId,
            request.PlayerId,
            finalScriptSource ?? priorStrategy.Markdown);

        return new BonesEnhanceStrategyResult(
            priorStrategy.StrategyId,
            currentStrategyId,
            strategyDocument,
            finalStrategyArtifact!,
            matchHistories.Count,
            finalExecutionArtifact!,
            finalPromotionDecision?.Outcome ?? BonesPromotionDecisionOutcome.Rejected,
            finalActiveStrategyId,
            finalPromotionDecisionArtifact!,
            StagnationDetected: totalStagnationDetected,
            EnhanceIterationsCompleted: lastIteration);
    }

    private async ValueTask TryUpsertLibraryBestAsync(
        BonesPlayerKnowledgeStore scopedKnowledgeStore,
        BonesPlayerId playerId,
        BonesStrategyArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (_strategyLibrary is null)
            return;

        var effectiveness = await scopedKnowledgeStore.GetEffectiveness(
            playerId,
            artifact.StrategyId,
            cancellationToken);

        var upserted = _strategyLibrary.UpsertBestStrategy(playerId, artifact, effectiveness);
        if (!upserted)
        {
            var reason = BonesStrategyLibraryRanking.IsEligible(effectiveness)
                ? "strategy does not rank higher than the incumbent"
                : "effectiveness is not eligible (MatchesPlayed < 1)";

            _logger?.LogWarning(
                "UpsertBestStrategy rejected for strategy {StrategyId} of player seat {Seat}: {Reason}.",
                artifact.StrategyId.Value,
                playerId.Seat,
                reason);
        }
    }

    private static async ValueTask<Dictionary<BonesPlayerId, BonesStrategyArtifact>> LoadOpponentActiveStrategiesAsync(
        BonesPlayerKnowledgeStore knowledgeStore,
        BonesPlayerId learningPlayerId,
        CancellationToken cancellationToken)
    {
        var opponents = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (playerId == learningPlayerId)
                continue;

            opponents[playerId] = await knowledgeStore.LoadActiveStrategy(playerId, cancellationToken);
        }

        return opponents;
    }

    private static string BuildMatchHistoryPath(BonesPlayerId playerId, BonesGameId gameId)
        => $"bones-player-{playerId.Seat}-match-{gameId.Value}";

    /// <summary>
    /// F3: Compares two C# source strings after normalization (line endings, whitespace trimming).
    /// Returns true when the sources are byte-identical or semantically indistinguishable.
    /// </summary>
    private static bool IsSourceIdentical(string sourceA, string sourceB)
    {
        if (ReferenceEquals(sourceA, sourceB))
            return true;

        var normalizedA = NormalizeSourceForComparison(sourceA);
        var normalizedB = NormalizeSourceForComparison(sourceB);
        return string.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
    }

    private static string NormalizeSourceForComparison(string source)
        => (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private async ValueTask<ArtifactDescriptor> SavePromotionDecisionArtifactAsync(
        SessionId sessionId,
        BonesPlayerId learningPlayerId,
        BonesPromotionDecision decision,
        CancellationToken cancellationToken)
    {
        var payload = BonesStrategyPromotionDecisionPersistence.FromDecision(learningPlayerId, decision);

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-strategy-promotion-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-strategy-promotion",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async ValueTask<ArtifactDescriptor> SaveExecutionArtifactAsync(
        SessionId sessionId,
        BonesEnhanceStrategyRequest request,
        BonesStrategyId priorStrategyId,
        BonesStrategyId strategyId,
        int matchHistoriesLoaded,
        int compileAttempts,
        BonesPromotionDecisionOutcome promotionOutcome,
        CancellationToken cancellationToken)
    {
        var payload = new BonesEnhanceStrategyExecutionLog(
            SessionId: sessionId,
            PlayerSeat: request.PlayerId.Seat,
            PriorStrategyId: priorStrategyId.Value,
            StrategyId: strategyId.Value,
            MatchGameId: request.MatchGameId?.Value,
            ModelId: string.IsNullOrWhiteSpace(request.ModelId) ? DefaultModelId : request.ModelId,
            MatchHistoriesLoaded: matchHistoriesLoaded,
            CompileAttempts: compileAttempts,
            PriorStage: "play",
            PromotionOutcome: promotionOutcome.ToString());

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-enhance-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-enhance-strategy-execution",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async ValueTask<ArtifactDescriptor> SaveEnhanceFailureArtifactAsync(
        SessionId sessionId,
        BonesPlayerId playerId,
        int maxRetries,
        string lastCompileFailureReason,
        string? lastModelResponseMarkdown,
        CancellationToken cancellationToken)
    {
        var retryAttempts = new List<BonesEnhanceFailureRetryAttempt>();
        if (lastModelResponseMarkdown is not null)
        {
            retryAttempts.Add(new BonesEnhanceFailureRetryAttempt(
                FailureReason: lastCompileFailureReason,
                ModelResponseMarkdown: lastModelResponseMarkdown));
        }

        var payload = new BonesEnhanceFailureLog(
            SessionId: sessionId,
            PlayerSeat: playerId.Seat,
            MaxRetries: maxRetries,
            LastCompileFailureReason: lastCompileFailureReason,
            RetryAttempts: retryAttempts.ToArray());

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-enhance-failure-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-enhance-failure",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ChatMessage>> LoadMemorySnapshotAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (_memory is null)
            return Array.Empty<ChatMessage>();

        var snapshot = await _memory.LoadAsync(sessionId, cancellationToken);
        return snapshot.Messages;
    }

    private async ValueTask AppendMemoryExchangeAsync(
        SessionId sessionId,
        BonesStrategyEnhancementRequest? enhancementRequest,
        string? assistantMarkdown,
        CancellationToken cancellationToken)
    {
        if (_memory is null || enhancementRequest is null)
            return;

        var finalUserPrompt = enhancementRequest.Messages
            .LastOrDefault(message => message.Role != "system");

        if (finalUserPrompt is null)
            return;

        await _memory.AppendAsync(
            sessionId,
            ChatMessage.User(finalUserPrompt.Content),
            cancellationToken);

        if (!string.IsNullOrEmpty(assistantMarkdown))
        {
            await _memory.AppendAsync(
                sessionId,
                ChatMessage.Assistant(assistantMarkdown),
                cancellationToken);
        }
    }

    /// <summary>
    /// The single enhancement model-call seam: the runtime path maps the turn through
    /// <see cref="BonesAgentAdapter{TRequest,TResult}"/> and the provider-neutral executor, while the
    /// legacy path keeps calling the provider directly. Compile/retry/persist logic is not forked.
    /// </summary>
    private async ValueTask<string> ExecuteModelTurnAsync(
        BonesStrategyEnhancementRequest enhancementRequest,
        string modelId,
        string correlationId,
        IReadOnlyList<ChatMessage> memoryMessages,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (_adapter is not null)
        {
            var turnResult = await _adapter.ExecuteAsync(
                new EnhanceModelTurnRequest(enhancementRequest.Messages, modelId, memoryMessages),
                context,
                cancellationToken);

            return turnResult.Markdown;
        }

        var modelResponse = await _modelProvider!.ExecuteAsync(
            new ModelProviderRequest<BonesStrategyEnhancementRequest>(
                payload: enhancementRequest,
                modelId: modelId,
                correlationId: correlationId),
            context,
            cancellationToken);

        return modelResponse.Payload.Markdown;
    }

    private static int ResolveEvaluationSeed(CapabilityContext context, BonesStrategyId incumbentStrategyId)
    {
        var testSeed = Environment.GetEnvironmentVariable("WIP_BONES_TEST_PROMOTION_EVALUATION_SEED");
        if (int.TryParse(testSeed, out var parsedSeed))
            return parsedSeed;

        return BonesSeedDerivation.DeriveWithLabel(0, $"{context.SessionId.Value}|{incumbentStrategyId.Value}");
    }

    private sealed record EnhanceModelTurnRequest(
        IReadOnlyList<BonesStrategyEnhancementMessage> Messages,
        string ModelId,
        IReadOnlyList<ChatMessage> MemoryMessages);

    private sealed record EnhanceModelTurnResult(string Markdown);

    private sealed record BonesEnhanceStrategyExecutionLog(
        SessionId SessionId,
        int PlayerSeat,
        string PriorStrategyId,
        string StrategyId,
        string? MatchGameId,
        string ModelId,
        int MatchHistoriesLoaded,
        int CompileAttempts,
        string PriorStage,
        string PromotionOutcome);

    private sealed record BonesEnhanceFailureLog(
        SessionId SessionId,
        int PlayerSeat,
        int MaxRetries,
        string LastCompileFailureReason,
        BonesEnhanceFailureRetryAttempt[] RetryAttempts);

    private sealed record BonesEnhanceFailureRetryAttempt(
        string FailureReason,
        string ModelResponseMarkdown);
}
