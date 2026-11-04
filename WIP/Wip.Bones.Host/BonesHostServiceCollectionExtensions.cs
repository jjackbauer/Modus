using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Wip.Builder;
using Wip.Runtime.Runtime;

namespace Wip.Bones.Host;

internal static class BonesHostStubModelProviders
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, StubBonesStrategyModelProvider>();
        services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>, StubBonesPlayTurnModelProvider>();
        services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, StubBonesEnhancementModelProvider>();
    }

    private sealed class StubBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
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

        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(GameAwareStrategy),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(160, 64),
                    correlationId: request.CorrelationId));
    }

    private sealed class StubBonesPlayTurnModelProvider
        : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesPlayTurnResult>(
                    payload: new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value),
                    providerId: "bones-play-turn-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(64, 16),
                    correlationId: request.CorrelationId));
    }

    private sealed class StubBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
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

        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(GameAwareEnhancement),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(180, 80),
                    correlationId: request.CorrelationId));
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

            // A second service provider (host vs. workflow/agent container) must share the
            // same pool. Creating a fresh instance per provider made status report poolSize=0
            // while the co-learning agent mutated a different pool.
            var existingPool = StrategyPool.Instance;
            if (existingPool is not null
                && string.Equals(existingPool.SnapshotPath, snapshotPath, StringComparison.OrdinalIgnoreCase))
            {
                return existingPool;
            }

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
                    // R4: Explicitly unify the static Instance with the DI singleton.
                    // TryLoad sets Instance via the constructor, but this assignment
                    // guarantees the status API and Web pool routes read the same pool.
                    StrategyPool.Instance = loaded;
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

            // R4: Explicitly unify the static Instance with the DI singleton.
            // SeedFromLibrary sets Instance via the constructor, but this assignment
            // guarantees the status API and Web pool routes read the same pool.
            StrategyPool.Instance = pool;

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

        return services.AddWipCapabilities().AddBonesLearningWorkflowWithViewer(options.ListenUrl);
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
                services.AddBonesDeepSeekModelProviders(
                    options.DeepSeekProviderOptions
                    ?? throw new InvalidOperationException(
                        "Bones host configuration is invalid: DeepSeek provider options are unavailable."),
                    options.Resilience);
                break;
            case BonesModelProviderKind.Stub:
                BonesHostStubModelProviders.Register(services);
                break;
            default:
                throw new InvalidOperationException(
                    $"Bones host configuration is invalid: ModelProvider '{options.ModelProvider}' is not supported.");
        }
    }

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