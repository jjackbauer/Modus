using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Play;
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

    private readonly IArtifactStore _artifactStore;
    private readonly IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult> _modelProvider;
    private readonly BonesStrategyScriptHost _scriptHost;
    private readonly BonesStrategyPromotionEvaluator _promotionEvaluator;
    private readonly BonesStrategyPromotionOptions _promotionOptions;
    private readonly BonesStrategyLibrary? _strategyLibrary;
    private readonly int? _forcedEvaluationSeed;
    private readonly string _playModelId;

    public BonesEnhanceStrategyAgent(
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult> modelProvider,
        BonesStrategyScriptHost? scriptHost = null,
        BonesStrategyPromotionEvaluator? promotionEvaluator = null,
        BonesStrategyPromotionOptions? promotionOptions = null,
        BonesGameSimulationBudget? gameBudget = null,
        BonesStrategyLibrary? strategyLibrary = null,
        int? forcedEvaluationSeed = null,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider = null,
        string? playModelId = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
        _promotionEvaluator = promotionEvaluator
            ?? new BonesStrategyPromotionEvaluator(
                new BonesMatchSimulator(),
                _scriptHost,
                gameBudget,
                playModelProvider);
        _promotionOptions = promotionOptions ?? new BonesStrategyPromotionOptions();
        _strategyLibrary = strategyLibrary;
        _forcedEvaluationSeed = forcedEvaluationSeed;
        _playModelId = string.IsNullOrWhiteSpace(playModelId) ? DefaultPlayModelId : playModelId;
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

        var enhancementRequest = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            request.PlayerId,
            priorStrategy,
            matchHistories,
            matchHistoryPaths,
            opponentStrategies);

        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? DefaultModelId : request.ModelId;
        var correlationId = $"bones-enhance-seat-{request.PlayerId.Seat}-{context.SessionId.Value}";

        const int maxCompileRetries = 3;
        string? scriptSource = null;
        string? lastCompileFailureReason = null;
        var compileAttempts = 0;
        var lastModelResponseMarkdown = (string?)null;
        BonesStrategyScriptCompileResult? compileResult = null;

        var existingDescriptors = await scopedKnowledgeStore.ListStrategyArtifacts(request.PlayerId, cancellationToken);
        const string artifactIdPrefix = "bones-strategy-";
        var existingIds = existingDescriptors
            .Select(descriptor => descriptor.ArtifactId.Value.StartsWith(artifactIdPrefix, StringComparison.Ordinal)
                ? descriptor.ArtifactId.Value[artifactIdPrefix.Length..]
                : descriptor.ArtifactId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var strategyId = BonesStrategyVersioning.NextVersion(priorStrategy.StrategyId, request.PlayerId, existingIds);

        for (var attempt = 1; attempt <= maxCompileRetries; attempt++)
        {
            compileAttempts = attempt;

            var modelResponse = await _modelProvider.ExecuteAsync(
                new ModelProviderRequest<BonesStrategyEnhancementRequest>(
                    payload: enhancementRequest,
                    modelId: modelId,
                    correlationId: $"{correlationId}-attempt-{attempt}"),
                context,
                cancellationToken);

            lastModelResponseMarkdown = modelResponse.Payload.Markdown;
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

            compileResult = _scriptHost.TryCompile(strategyId, scriptSource);
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

        if (compileResult is not { Succeeded: true } || scriptSource is null)
        {
            throw new InvalidOperationException(
                lastCompileFailureReason
                ?? "Enhanced strategy script compilation failed before persistence.");
        }

        var candidateArtifact = new BonesStrategyArtifact(
            strategyId,
            request.PlayerId,
            BonesStrategyKind.Script,
            scriptSource,
            BonesPromotionStatus.Candidate);

        var strategyArtifact = await scopedKnowledgeStore.SaveCandidateStrategy(
            request.PlayerId,
            candidateArtifact,
            cancellationToken);

        var evalOpponentStrategies = await LoadOpponentActiveStrategiesAsync(
            knowledgeStore,
            request.PlayerId,
            cancellationToken);

        var evaluationRequest = new BonesStrategyPromotionEvaluationRequest(
            request.PlayerId,
            priorArtifact,
            candidateArtifact,
            evalOpponentStrategies,
            _promotionOptions,
            request.EvaluationSeed ?? _forcedEvaluationSeed ?? ResolveEvaluationSeed(context, priorArtifact.StrategyId),
            request.EvaluationTargetScore ?? DefaultEvaluationTargetScore,
            context,
            _playModelId);

        var promotionDecision = await _promotionEvaluator.EvaluateAsync(evaluationRequest, cancellationToken);

        BonesStrategyId activeStrategyId;
        if (promotionDecision.Outcome == BonesPromotionDecisionOutcome.Promoted)
        {
            await scopedKnowledgeStore.PromoteStrategy(request.PlayerId, strategyId, cancellationToken);
            activeStrategyId = strategyId;
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

            await scopedKnowledgeStore.SaveStrategyArtifact(request.PlayerId, rejected, cancellationToken);
            activeStrategyId = priorArtifact.StrategyId;
        }

        var promotionDecisionArtifact = await SavePromotionDecisionArtifactAsync(
            context.SessionId,
            request.PlayerId,
            promotionDecision,
            cancellationToken);

        var strategyDocument = new BonesStrategyDocument(
            strategyId,
            request.PlayerId,
            scriptSource);

        var executionArtifact = await SaveExecutionArtifactAsync(
            context.SessionId,
            request,
            priorStrategy.StrategyId,
            strategyId,
            matchHistories.Count,
            compileAttempts,
            promotionDecision.Outcome,
            cancellationToken);

        return new BonesEnhanceStrategyResult(
            priorStrategy.StrategyId,
            strategyId,
            strategyDocument,
            strategyArtifact,
            matchHistories.Count,
            executionArtifact,
            promotionDecision.Outcome,
            activeStrategyId,
            promotionDecisionArtifact);
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

        _strategyLibrary.UpsertBestStrategy(playerId, artifact, effectiveness);
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

    private static int ResolveEvaluationSeed(CapabilityContext context, BonesStrategyId incumbentStrategyId)
    {
        var testSeed = Environment.GetEnvironmentVariable("WIP_BONES_TEST_PROMOTION_EVALUATION_SEED");
        if (int.TryParse(testSeed, out var parsedSeed))
            return parsedSeed;

        return HashCode.Combine(context.SessionId.Value, incumbentStrategyId.Value);
    }

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
