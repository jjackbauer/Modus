using System.Text.Json;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Runtime;
using Wip.Bones.Agents.Script;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Ponder;

public sealed class BonesPonderAgent : IAgent<BonesPonderRequest, BonesPonderResult>
{
    private const string ProducerType = "Wip.Bones.Agents.BonesPonderAgent";
    private const string ProducerVersion = "1.0.0";
    private const string DefaultModelId = "bones-strategy-author";

    private readonly IArtifactStore _artifactStore;
    private readonly IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>? _modelProvider;
    private readonly BonesStrategyScriptHost _scriptHost;
    private readonly BonesCompileRetryOptions _compileRetryOptions;
    private readonly BonesAgentAdapter<PonderModelTurnRequest, PonderModelTurnResult>? _adapter;
    private readonly IAgentMemory? _memory;
    private readonly BonesAgentLoopOptions _loopOptions;

    /// <summary>
    /// Legacy compatibility path: calls <see cref="IModelProvider{TRequestPayload,TResultPayload}"/>
    /// directly. Kept source-compatible for the existing tests and the current host wiring; the
    /// runtime path is composed through <see cref="IAgentExecutor"/> by the production constructor.
    /// </summary>
    public BonesPonderAgent(
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult> modelProvider,
        BonesStrategyScriptHost? scriptHost = null,
        BonesCompileRetryOptions? compileRetryOptions = null)
        : this(
            artifactStore,
            modelProvider ?? throw new ArgumentNullException(nameof(modelProvider)),
            executor: null,
            scriptHost,
            compileRetryOptions,
            memory: null,
            loopOptions: null)
    {
    }

    /// <summary>
    /// Production constructor: every model turn runs through the shared runtime agent loop
    /// (<see cref="IAgentExecutor"/>) via the WP1 adapter, with prompts from
    /// <see cref="BonesPonderPrompts"/>, budgets from <see cref="BonesAgentLoopOptions"/> and
    /// optional session memory.
    /// </summary>
    public BonesPonderAgent(
        IArtifactStore artifactStore,
        IAgentExecutor executor,
        BonesStrategyScriptHost? scriptHost = null,
        BonesCompileRetryOptions? compileRetryOptions = null,
        IAgentMemory? memory = null,
        BonesAgentLoopOptions? loopOptions = null)
        : this(
            artifactStore,
            modelProvider: null,
            executor ?? throw new ArgumentNullException(nameof(executor)),
            scriptHost,
            compileRetryOptions,
            memory,
            loopOptions)
    {
    }

    private BonesPonderAgent(
        IArtifactStore artifactStore,
        IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>? modelProvider,
        IAgentExecutor? executor,
        BonesStrategyScriptHost? scriptHost,
        BonesCompileRetryOptions? compileRetryOptions,
        IAgentMemory? memory,
        BonesAgentLoopOptions? loopOptions)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _modelProvider = modelProvider;
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
        _compileRetryOptions = compileRetryOptions ?? new BonesCompileRetryOptions();
        _memory = memory;
        _loopOptions = loopOptions ?? new BonesAgentLoopOptions();

        _adapter = executor is null
            ? null
            : new BonesAgentAdapter<PonderModelTurnRequest, PonderModelTurnResult>(
                executor,
                (turn, context) => BonesPonderPrompts.CreateExecutionRequest(
                    turn.Messages,
                    turn.ModelId,
                    context.SessionId,
                    turn.MemoryMessages,
                    _loopOptions.ToAgentLoopOptions()),
                (_, _, executionResult, _) => ValueTask.FromResult(
                    new PonderModelTurnResult(BonesPonderPrompts.ReadCompletedMarkdown(executionResult))));
    }

    public async ValueTask<BonesPonderResult> ExecuteAsync(
        BonesPonderRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(request.RepositoryPath));

        var knowledgeStore = new BonesPlayerKnowledgeStore(
            _artifactStore,
            request.RepositoryPath,
            context.SessionId).ForPlayer(request.PlayerId);

        var bootstrapState = await BonesStrategyBootstrapState.TryLoadAsync(
            _artifactStore,
            context.SessionId,
            request.RepositoryPath,
            cancellationToken);

        if (bootstrapState?.BootstrappedFromLibrarySeats.Contains(request.PlayerId) == true)
            return await ReturnBootstrappedResultAsync(
                request,
                context,
                knowledgeStore,
                cancellationToken);

        var observations = await knowledgeStore.LoadObservationTranscripts(request.PlayerId, cancellationToken);
        var observationPaths = observations
            .Select(observation => BuildObservationPath(request.PlayerId, observation.GameId))
            .ToArray();

        var authoringRequest = BonesPonderPromptBuilder.BuildAuthoringRequest(
            request.PlayerId,
            observations,
            observationPaths);

        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? DefaultModelId : request.ModelId;
        var correlationId = $"bones-ponder-seat-{request.PlayerId.Seat}-{context.SessionId.Value}";
        var memoryMessages = await LoadMemorySnapshotAsync(context.SessionId, cancellationToken);

        var budget = new BonesDeepSeekPonderRetryBudget(_compileRetryOptions.MaxCompileRetries);
        string? scriptSource = null;
        string? lastCompileFailureReason = null;
        var compileAttempts = 0;
        var lastModelResponseMarkdown = (string?)null;

        while (!budget.IsExhausted)
        {
            compileAttempts++;
            budget.Consume();

            lastModelResponseMarkdown = await ExecuteModelTurnAsync(
                authoringRequest,
                modelId,
                $"{correlationId}-attempt-{compileAttempts}",
                memoryMessages,
                context,
                cancellationToken);
            scriptSource = BonesStrategyScriptResponseParser.TryParseScriptSource(lastModelResponseMarkdown);
            if (scriptSource is null)
            {
                lastCompileFailureReason = "Model response did not contain a compilable IBonesPlayerSlot script.";
                if (budget.IsExhausted)
                {
                    await SavePonderFailureArtifactAsync(
                        context.SessionId,
                        request.PlayerId,
                        compileAttempts,
                        lastCompileFailureReason,
                        lastModelResponseMarkdown,
                        cancellationToken);

                    throw new InvalidOperationException(lastCompileFailureReason);
                }

                authoringRequest = BonesPonderPromptBuilder.AppendCompileRetryMessage(
                    authoringRequest,
                    lastCompileFailureReason,
                    lastModelResponseMarkdown);
                continue;
            }

            var strategyId = new BonesStrategyId($"initial-seat-{request.PlayerId.Seat}-v1");
            var compileResult = _scriptHost.TryCompile(strategyId, scriptSource);
            if (compileResult.Succeeded)
            {
                lastCompileFailureReason = null;

                var authoredStrategy = new BonesStrategyArtifact(
                    strategyId,
                    request.PlayerId,
                    BonesStrategyKind.Script,
                    scriptSource,
                    BonesPromotionStatus.Active);

                var strategyArtifact = await knowledgeStore.SaveStrategyArtifact(
                    request.PlayerId,
                    authoredStrategy,
                    cancellationToken);

                var strategyDocument = new BonesStrategyDocument(
                    strategyId,
                    request.PlayerId,
                    scriptSource);

                var executionArtifact = await SaveExecutionArtifactAsync(
                    context.SessionId,
                    request,
                    strategyId,
                    observations.Count,
                    bootstrapSource: null,
                    compileAttempts,
                    finalCompileSucceeded: true,
                    lastCompileFailureReason: null,
                    cancellationToken);

                await AppendMemoryExchangeAsync(
                    context.SessionId,
                    authoringRequest,
                    lastModelResponseMarkdown,
                    cancellationToken);

                return new BonesPonderResult(
                    strategyId,
                    strategyDocument,
                    authoredStrategy,
                    strategyArtifact,
                    observations.Count,
                    executionArtifact);
            }

            lastCompileFailureReason = compileResult.FailureReason
                ?? "Strategy script compilation failed before persistence.";

            if (budget.IsExhausted)
            {
                await SavePonderFailureArtifactAsync(
                    context.SessionId,
                    request.PlayerId,
                    compileAttempts,
                    lastCompileFailureReason,
                    lastModelResponseMarkdown,
                    cancellationToken);

                throw new InvalidOperationException(lastCompileFailureReason);
            }

            authoringRequest = BonesPonderPromptBuilder.AppendCompileRetryMessage(
                authoringRequest,
                lastCompileFailureReason,
                lastModelResponseMarkdown);
        }

        throw new InvalidOperationException(
            lastCompileFailureReason
            ?? "Strategy script compilation failed before persistence.");
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
        BonesStrategyAuthoringRequest authoringRequest,
        string assistantMarkdown,
        CancellationToken cancellationToken)
    {
        if (_memory is null)
            return;

        var finalUserPrompt = authoringRequest.Messages
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
    /// The single model-call seam of the compile-retry loop: the runtime path maps the turn through
    /// <see cref="BonesAgentAdapter{TRequest,TResult}"/> and the provider-neutral executor, while the
    /// legacy path keeps calling the provider directly. Compile/persist logic is not forked.
    /// </summary>
    private async ValueTask<string> ExecuteModelTurnAsync(
        BonesStrategyAuthoringRequest authoringRequest,
        string modelId,
        string correlationId,
        IReadOnlyList<ChatMessage> memoryMessages,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (_adapter is not null)
        {
            var turnResult = await _adapter.ExecuteAsync(
                new PonderModelTurnRequest(authoringRequest.Messages, modelId, memoryMessages),
                context,
                cancellationToken);

            return turnResult.Markdown;
        }

        var modelResponse = await _modelProvider!.ExecuteAsync(
            new ModelProviderRequest<BonesStrategyAuthoringRequest>(
                payload: authoringRequest,
                modelId: modelId,
                correlationId: correlationId),
            context,
            cancellationToken);

        return modelResponse.Payload.Markdown;
    }

    private async ValueTask<BonesPonderResult> ReturnBootstrappedResultAsync(
        BonesPonderRequest request,
        CapabilityContext context,
        BonesPlayerKnowledgeStore knowledgeStore,
        CancellationToken cancellationToken)
    {
        var activeStrategy = await knowledgeStore.LoadActiveStrategy(request.PlayerId, cancellationToken);
        var bootstrappedDocument = new BonesStrategyDocument(
            activeStrategy.StrategyId,
            request.PlayerId,
            activeStrategy.Source);

        var bootstrappedArtifactDescriptor = await FindStrategyArtifactDescriptorAsync(
            context.SessionId,
            request.PlayerId,
            activeStrategy.StrategyId,
            cancellationToken);

        var bootstrappedExecutionArtifact = await SaveExecutionArtifactAsync(
            context.SessionId,
            request,
            activeStrategy.StrategyId,
            observationsLoaded: 0,
            bootstrapSource: BonesBootstrapSource.Library,
            compileAttempts: 0,
            finalCompileSucceeded: true,
            lastCompileFailureReason: null,
            cancellationToken);

        return new BonesPonderResult(
            activeStrategy.StrategyId,
            bootstrappedDocument,
            activeStrategy,
            bootstrappedArtifactDescriptor,
            0,
            bootstrappedExecutionArtifact);
    }

    private static string BuildObservationPath(BonesPlayerId playerId, BonesGameId gameId)
        => $"bones-player-{playerId.Seat}-observation-{gameId.Value}";

    private async ValueTask<ArtifactDescriptor> FindStrategyArtifactDescriptorAsync(
        SessionId sessionId,
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        CancellationToken cancellationToken)
    {
        var artifactId = new ArtifactId($"bones-strategy-{strategyId.Value}");
        var descriptors = await _artifactStore.ListAsync(sessionId, cancellationToken);
        return descriptors.FirstOrDefault(descriptor => descriptor.ArtifactId == artifactId)
            ?? throw new InvalidOperationException(
                $"Bootstrapped strategy artifact '{strategyId.Value}' was not found for seat {playerId.Seat}.");
    }

    private async ValueTask<ArtifactDescriptor> SaveExecutionArtifactAsync(
        SessionId sessionId,
        BonesPonderRequest request,
        BonesStrategyId strategyId,
        int observationsLoaded,
        string? bootstrapSource,
        int compileAttempts,
        bool finalCompileSucceeded,
        string? lastCompileFailureReason,
        CancellationToken cancellationToken)
    {
        var payload = new BonesPonderExecutionLog(
            SessionId: sessionId,
            PlayerSeat: request.PlayerId.Seat,
            StrategyId: strategyId.Value,
            ModelId: string.IsNullOrWhiteSpace(request.ModelId) ? DefaultModelId : request.ModelId,
            ObservationsLoaded: observationsLoaded,
            BootstrapSource: bootstrapSource,
            CompileAttempts: compileAttempts,
            FinalCompileSucceeded: finalCompileSucceeded,
            LastCompileFailureReason: lastCompileFailureReason);

        return await _artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-ponder-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-ponder-execution",
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async ValueTask<ArtifactDescriptor> SavePonderFailureArtifactAsync(
        SessionId sessionId,
        BonesPlayerId playerId,
        int compileAttempts,
        string lastCompileFailureReason,
        string? lastModelResponseMarkdown,
        CancellationToken cancellationToken)
    {
        var retryAttempts = new List<BonesPonderFailureRetryAttempt>();
        if (lastModelResponseMarkdown is not null)
        {
            retryAttempts.Add(new BonesPonderFailureRetryAttempt(
                Attempt: compileAttempts,
                FailureReason: lastCompileFailureReason,
                ModelResponseMarkdown: lastModelResponseMarkdown));
        }

        var payload = new BonesPonderFailureLog(
            SessionId: sessionId,
            PlayerSeat: playerId.Seat,
            MaxRetries: _compileRetryOptions.MaxCompileRetries,
            RetriesUsed: compileAttempts,
            LastCompileFailureReason: lastCompileFailureReason,
            RetryAttempts: retryAttempts.ToArray());

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

    private sealed record PonderModelTurnRequest(
        IReadOnlyList<BonesStrategyAuthoringMessage> Messages,
        string ModelId,
        IReadOnlyList<ChatMessage> MemoryMessages);

    private sealed record PonderModelTurnResult(string Markdown);

    private sealed record BonesPonderExecutionLog(
        SessionId SessionId,
        int PlayerSeat,
        string StrategyId,
        string ModelId,
        int ObservationsLoaded,
        string? BootstrapSource,
        int CompileAttempts,
        bool FinalCompileSucceeded,
        string? LastCompileFailureReason);

    private sealed record BonesPonderFailureLog(
        SessionId SessionId,
        int PlayerSeat,
        int MaxRetries,
        int RetriesUsed,
        string LastCompileFailureReason,
        BonesPonderFailureRetryAttempt[] RetryAttempts);

    private sealed record BonesPonderFailureRetryAttempt(
        int Attempt,
        string FailureReason,
        string ModelResponseMarkdown);
}
