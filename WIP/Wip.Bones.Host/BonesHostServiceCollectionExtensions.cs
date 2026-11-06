using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Runtime;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Wip.Builder;
using Wip.Runtime;
using Wip.Runtime.Runtime;

namespace Wip.Bones.Host;

/// <summary>
/// Stub <see cref="IChatModel"/> used when the host runs with
/// <see cref="BonesModelProviderKind.Stub"/>. It returns the same canned authoring and enhancement
/// scripts the retired per-stage stub model providers produced and selects a legal move id for
/// play-turn prompts, so the stub path still drives the full learning loop without a network.
/// </summary>
internal sealed class StubBonesChatModel : IChatModel
{
    private const string GameAwareStrategy = """
        ```csharp
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                var best = legalMoves[0];
                var bestPips = 0;
                foreach (var move in legalMoves)
                {
                    if (move.IsPass) continue;
                    var tile = move.Tile!.Value;
                    if (tile.TotalPips > bestPips) { best = move; bestPips = tile.TotalPips; }
                }
                return best;
            }
        }
        ```
        """;

    private const string GameAwareEnhancement = """
        ```csharp
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                var best = legalMoves[0];
                var bestScore = int.MinValue;
                foreach (var move in legalMoves)
                {
                    if (move.IsPass) continue;
                    var tile = move.Tile!.Value;
                    var score = tile.TotalPips;
                    if (tile.IsDouble) score += 4;
                    if (score > bestScore) { best = move; bestScore = score; }
                }
                return best;
            }
        }
        ```
        """;

    private const string EnhancementPromptMarker = "Strategy script enhancement request";

    private static readonly Regex MoveIdPattern = new(
        @"\(move id:\s*(?<moveId>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ValueTask<ChatResponse> CompleteAsync(
        ChatRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lastUserMessage = request.Messages.LastOrDefault(message => message.Role == ChatRole.User);
        var content = SelectResponse(lastUserMessage?.Content ?? string.Empty);

        return ValueTask.FromResult(new ChatResponse(
            message: ChatMessage.Assistant(content),
            modelId: request.ModelId,
            providerId: "bones-stub",
            usage: new ModelProviderUsage(160, 64),
            correlationId: request.CorrelationId));
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        CapabilityContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await CompleteAsync(request, context, cancellationToken);
        yield return new StreamingChunk(response.Message.Content);
        yield return new StreamingChunk(string.Empty, isFinal: true, usage: response.Usage);
    }

    private static string SelectResponse(string userContent)
    {
        if (userContent.Contains(BonesPlayMatchPromptBuilder.AllowedMovesHeader, StringComparison.Ordinal)
            || userContent.Contains("# Play turn request", StringComparison.Ordinal))
        {
            return SelectLegalMoveId(userContent);
        }

        if (userContent.Contains(EnhancementPromptMarker, StringComparison.Ordinal))
        {
            return GameAwareEnhancement;
        }

        return GameAwareStrategy;
    }

    private static string SelectLegalMoveId(string userContent)
    {
        var moveIds = MoveIdPattern
            .Matches(userContent)
            .Select(match => match.Groups["moveId"].Value.Trim())
            .Where(moveId => !string.IsNullOrWhiteSpace(moveId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return moveIds.Length == 0 ? string.Empty : moveIds[0];
    }
}

public static class BonesHostServiceCollectionExtensions
{
    public static WipBuilder AddBonesHostServices(this IServiceCollection services, BonesHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(options.DataDirectory);

        var artifactStore = new WipArtifactStoreLocal(options.DataDirectory);
        services.AddSingleton<IArtifactStore>(artifactStore);
        services.AddSingleton(artifactStore);

        services.AddSingleton(options);
        services.AddSingleton(options.Ponder);
        services.AddSingleton(options.Promotion);
        services.AddSingleton(options.ScriptHost);
        services.AddSingleton(new BonesGameSimulationBudget(options.MaxGamesPerRun));
        services.AddSingleton(new BonesMatchSimulator());
        services.AddSingleton(new BonesStrategyLibrary(options.DataDirectory));
        services.AddSingleton(sp =>
        {
            var library = sp.GetRequiredService<BonesStrategyLibrary>();
            var opts = sp.GetRequiredService<BonesHostOptions>();
            var snapshotPath = StrategyPool.GetPoolSnapshotPath(opts.DataDirectory);
            var logger = sp.GetService<ILogger<StrategyPool>>();

            // The pool is a DI singleton: the status endpoint, pool routes and agents all resolve
            // this same instance. Nothing else may create or mutate a process-wide pool.
            // I2: Always attempt to load a snapshot when the file exists.
            // ResumeFromBest no longer gates snapshot loading — it only controls
            // whether library entries are used as fallback during bootstrapping.
            var loaded = StrategyPool.TryLoadFromDisk(snapshotPath, logger);
            if (loaded is not null)
            {
                // I3: Verify pool integrity after loading from disk.
                var issues = loaded.VerifyIntegrity();
                if (issues.Count == 0)
                {
                    loaded.SnapshotPath = snapshotPath;
                    // Log success via a simple console/debug emit since we don't have ILogger<BonesHostServiceCollectionExtensions>
                    System.Diagnostics.Debug.WriteLine(
                        $"Loaded pool snapshot with {loaded.Count} entries from {snapshotPath}");

                    // R5: Re-write snapshot at startup to ensure the pool/ directory
                    // exists (might have been deleted) and snapshot is immediately current.
                    SaveInitialPoolSnapshot(loaded, snapshotPath, logger);

                    return loaded;
                }

                // Integrity check failed — log issues and fall back to library seeding.
                if (logger is not null)
                {
                    foreach (var issue in issues)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Pool snapshot integrity issue: {issue}");
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"Pool snapshot integrity check failed ({issues.Count} issue(s)). Falling back to SeedFromLibrary.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    "No pool snapshot found — seeding from library.");
            }

            // Fallback: seed pool from the strategy library.
            var pool = StrategyPool.SeedFromLibrary(library, targetPoolSize: opts.Population.PoolSize);

            // Configure auto-persist path for the freshly seeded pool.
            pool.SnapshotPath = snapshotPath;

            // R5: Write the initial pool snapshot at startup so the pool/ directory
            // is created immediately, decoupled from the periodic 10-iteration timer.
            SaveInitialPoolSnapshot(pool, snapshotPath, logger);

            return pool;
        });
        services.AddSingleton(sp => new BonesStrategyBootstrapper(sp.GetRequiredService<BonesStrategyLibrary>()));
        services.AddSingleton(sp => BonesScriptHostRuntime.Create(
            options.ScriptHost,
            sp.GetService<ILoggerFactory>()));
        services.AddSingleton(sp => sp.GetRequiredService<BonesScriptHostRuntime>().Host);
        services.AddSingleton<PopulationMatchComposer>();

        RegisterModelProviders(services, options);

        var matchCatalog = new InMemoryBonesMatchCatalog();
        services.AddSingleton<IBonesMatchCatalog>(matchCatalog);
        services.AddSingleton<BonesGameEngine>();
        services.AddSingleton<BonesMatchViewerService>();
        services.AddSingleton<IBonesPublicMatchFeed>(new BonesCatalogPublicMatchFeed(matchCatalog));
        services.AddSingleton<StrategyPoolJsonService>();

        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<ISessionEventPublisher, NoOpSessionEventPublisher>();
        services.AddSingleton<WipRuntimeOrchestrator>();
        services.AddSingleton<BonesHostSessionStore>();
        services.AddSingleton<BonesLoopState>();
        services.AddSingleton<BonesLearningLoopHost>();
        services.AddHostedService(sp => sp.GetRequiredService<BonesLearningLoopHost>());

        var workflowBuilder = services.AddWipCapabilities().AddBonesLearningWorkflowWithViewer(options.ListenUrl);

        // WP4: the host owns the runtime-agent composition. These explicit factories are the last
        // registration for each agent type, so the co-learning wrappers registered by
        // AddBonesLearningWorkflowWithViewer resolve the runtime-backed instances.
        RegisterRuntimeAgents(services, options);

        return workflowBuilder;
    }

    /// <summary>
    /// Writes the initial pool snapshot at startup so the pool/ directory is created
    /// immediately. This is decoupled from the periodic 10-iteration snapshot timer.
    /// Failures are logged as warnings and do not prevent startup.
    /// </summary>
    private static void SaveInitialPoolSnapshot(
        StrategyPool pool,
        string snapshotPath,
        ILogger<StrategyPool>? logger)
    {
        try
        {
            pool.PersistToDisk(snapshotPath);
            if (logger is not null)
            {
                logger.LogInformation(
                    "Initial pool snapshot saved: {EntryCount} entries to {SnapshotPath}",
                    pool.Count,
                    snapshotPath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Initial pool snapshot saved: {pool.Count} entries to {snapshotPath}");
            }
        }
        catch (Exception ex)
        {
            if (logger is not null)
                logger.LogWarning(ex, "Failed to save initial pool snapshot to {SnapshotPath}.", snapshotPath);
            else
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to save initial pool snapshot to {snapshotPath}: {ex.Message}");
        }
    }

    private static void RegisterModelProviders(IServiceCollection services, BonesHostOptions options)
    {
        switch (options.ModelProvider)
        {
            case BonesModelProviderKind.DeepSeek:
                var deepSeekOptions = options.DeepSeekProviderOptions
                    ?? throw new InvalidOperationException(
                        "Bones host configuration is invalid: DeepSeek provider options are unavailable.");

                services.AddDeepSeekChatModel(deepSeekOptions.ToDeepSeekModelProviderOptions());

                // Decorate the concrete chat model with the Bones stage-model adapter: agent stage ids
                // are rewritten to the configured DeepSeek model, supported ids pass through.
                var configuredModelId = deepSeekOptions.Model.Value;
                services.Replace(ServiceDescriptor.Singleton<IChatModel>(serviceProvider =>
                    new BonesStageChatModel(
                        serviceProvider.GetRequiredService<DeepSeekChatModel>(),
                        configuredModelId)));
                break;
            case BonesModelProviderKind.Stub:
                services.AddSingleton<IChatModel>(new StubBonesChatModel());
                break;
            default:
                throw new InvalidOperationException(
                    $"Bones host configuration is invalid: ModelProvider '{options.ModelProvider}' is not supported.");
        }

        // Agent-loop tools are runtime capabilities, not workflow capabilities: the executor's
        // RuntimeToolInvoker resolves the compile tool by its descriptor type.
        services.AddSingleton(serviceProvider => new BonesStrategyCompileTool(
            serviceProvider.GetRequiredService<BonesStrategyScriptHost>()));

        // The play-turn stage now runs over the provider-neutral chat model.
        services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>(serviceProvider =>
            new BonesChatModelPlayTurnProvider(serviceProvider.GetRequiredService<IChatModel>()));
    }

    private static void RegisterRuntimeAgents(IServiceCollection services, BonesHostOptions options)
    {
        services.RemoveAll<BonesPonderAgent>();
        services.AddSingleton(serviceProvider => CreatePonderAgent(serviceProvider, options));

        services.RemoveAll<BonesEnhanceStrategyAgent>();
        services.AddSingleton(serviceProvider => CreateEnhanceStrategyAgent(serviceProvider, options));
    }

    /// <summary>
    /// Host-owned Ponder composition: the shared runtime agent loop over the host <see cref="IChatModel"/>,
    /// the Bones compile tool, artifact-backed session memory and the host loop budget.
    /// </summary>
    internal static BonesPonderAgent CreatePonderAgent(IServiceProvider serviceProvider, BonesHostOptions options)
        => new(
            serviceProvider.GetRequiredService<IArtifactStore>(),
            CreateAgentExecutor(serviceProvider),
            serviceProvider.GetRequiredService<BonesStrategyScriptHost>(),
            options.Ponder,
            CreateAgentMemory(serviceProvider),
            options.AgentLoop);

    /// <summary>
    /// Host-owned Enhance composition: the shared runtime agent loop plus the promotion, aggressive
    /// iteration, game-budget and strategy-library dependencies the enhancement stage owns.
    /// </summary>
    internal static BonesEnhanceStrategyAgent CreateEnhanceStrategyAgent(
        IServiceProvider serviceProvider,
        BonesHostOptions options,
        BonesStrategyPromotionOptions? promotionOptions = null,
        int? forcedEvaluationSeed = null)
        => new(
            serviceProvider.GetRequiredService<IArtifactStore>(),
            CreateAgentExecutor(serviceProvider),
            serviceProvider.GetRequiredService<BonesStrategyScriptHost>(),
            promotionEvaluator: null,
            promotionOptions: promotionOptions ?? options.Promotion,
            compileRetryOptions: options.Ponder,
            aggressiveIterationOptions: options.AggressiveIteration,
            gameBudget: serviceProvider.GetRequiredService<BonesGameSimulationBudget>(),
            strategyLibrary: serviceProvider.GetRequiredService<BonesStrategyLibrary>(),
            forcedEvaluationSeed: forcedEvaluationSeed,
            playModelProvider: serviceProvider.GetRequiredService<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>(),
            playModelId: options.PlayModelId,
            logger: serviceProvider.GetService<ILogger<BonesEnhanceStrategyAgent>>(),
            memory: CreateAgentMemory(serviceProvider),
            loopOptions: options.AgentLoop);

    private static AgentExecutor CreateAgentExecutor(IServiceProvider serviceProvider)
        => new(
            serviceProvider.GetRequiredService<IChatModel>(),
            new RuntimeToolInvoker(serviceProvider, BonesAgentToolDescriptors.Create()));

    private static ArtifactAgentMemory CreateAgentMemory(IServiceProvider serviceProvider)
        => new(serviceProvider.GetRequiredService<IArtifactStore>());

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_sessions.TryGetValue(sessionId, out var snapshot) ? snapshot : null);
        }
    }

    private sealed class NoOpSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}