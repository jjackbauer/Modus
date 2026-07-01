using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Budget;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Microsoft.Extensions.Hosting;

namespace Wip.Bones.Host;

public static class BonesHostApplication
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = ResolveContentRoot(),
        });

        var options = BonesHostOptions.Bind(builder.Configuration);
        foreach (var diagnostic in options.GetStartupDiagnostics())
        {
            Console.Out.WriteLine($"startup-config: {diagnostic}");
        }

        var listenUrls = builder.Configuration["ASPNETCORE_URLS"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? options.ListenUrl.ToString();
        builder.WebHost.UseUrls(listenUrls);

        var workflowBuilder = builder.Services.AddBonesHostServices(options);
        builder.Services.AddSingleton(workflowBuilder);

        var app = builder.Build();

        BonesWebHost.MapMatchRoutes(app);
        BonesWebHost.MapStaticViewer(app);

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapGet("/api/bones/status", async (
            HttpContext httpContext,
            BonesLearningLoopHost loopHost,
            BonesHostOptions hostOptions,
            BonesGameSimulationBudget gameBudget,
            BonesStrategyLibrary strategyLibrary,
            BonesStrategyScriptHost scriptHost,
            IArtifactStore artifactStore,
            CancellationToken cancellationToken) =>
        {
            var state = loopHost.CurrentState;
            var current = state.CurrentIteration;
            var modelProvider = hostOptions.GetModelProviderDiagnostics();
            string? viewerUrl = null;
            if (!string.IsNullOrWhiteSpace(current.SessionId) && !string.IsNullOrWhiteSpace(current.MatchId))
            {
                var requestBaseUrl = new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}");
                viewerUrl = BonesViewerUrlBuilder
                    .BuildMatchViewUrl(
                        requestBaseUrl,
                        new SessionId(current.SessionId),
                        new BonesGameId(current.MatchId))
                    .ToString();
            }

            object? learningPlayer = null;
            string? learningPlayerStatus = null;
            string? learningPlayerError = null;
            object? libraryEffectiveness = null;

            if (!string.IsNullOrWhiteSpace(current.SessionId))
            {
                try
                {
                    var sessionId = new SessionId(current.SessionId);
                    var repositoryPath = Path.GetFullPath(hostOptions.DataDirectory);
                    var knowledgeStore = new BonesPlayerKnowledgeStore(
                        artifactStore,
                        repositoryPath,
                        sessionId);

                    var learningPlayerId = hostOptions.DefaultParameters.LearningPlayerId;
                    var activeStrategy = await knowledgeStore.TryLoadActiveStrategy(learningPlayerId, cancellationToken);
                    if (activeStrategy is not null)
                    {
                        var candidateStrategyId = await knowledgeStore.TryLoadCandidateStrategyId(
                            learningPlayerId,
                            cancellationToken);
                        var effectiveness = await knowledgeStore.GetEffectiveness(
                            learningPlayerId,
                            activeStrategy.StrategyId,
                            cancellationToken);

                        learningPlayer = new
                        {
                            seat = learningPlayerId.Seat,
                            activeStrategyId = activeStrategy.StrategyId.Value,
                            candidateStrategyId = candidateStrategyId?.Value,
                            effectiveness = new
                            {
                                matchesPlayed = effectiveness.MatchesPlayed,
                                wins = effectiveness.Wins,
                                losses = effectiveness.Losses,
                                cumulativeScoreDifferential = effectiveness.CumulativeScoreDifferential,
                            },
                        };
                    }
                    else if (IsPonderPendingStage(current.StageName))
                    {
                        learningPlayerStatus = "pending";
                    }

                    var libraryRanking = strategyLibrary.GetRanking(learningPlayerId);
                    var libraryEntry = libraryRanking.Count > 0 ? libraryRanking[0] : null;
                    if (libraryEntry is not null)
                    {
                        libraryEffectiveness = new
                        {
                            strategyId = libraryEntry.StrategyId.Value,
                            matchesPlayed = libraryEntry.Effectiveness.MatchesPlayed,
                            wins = libraryEntry.Effectiveness.Wins,
                            losses = libraryEntry.Effectiveness.Losses,
                            cumulativeScoreDifferential = libraryEntry.Effectiveness.CumulativeScoreDifferential,
                        };
                    }
                }
                catch (Exception exception)
                {
                    learningPlayerError = exception.Message;
                }
            }

            var lastIterationError = state.LastIterationError is null
                ? null
                : new
                {
                    message = state.LastIterationError.Message,
                    stage = state.LastIterationError.FailureStage,
                };

            var libraryIntegrity = strategyLibrary.VerifyIntegrity();

            // Build per-seat strategies from library for co-learning status
            var allSeatsStrategies = strategyLibrary.LoadBestStrategiesForAllSeats();
            var perSeatStrategies = new List<object>();
            foreach (var seat in new[] { 1, 2, 3, 4 })
            {
                var playerId = new BonesPlayerId(seat);
                if (allSeatsStrategies.TryGetValue(playerId, out var artifact))
                {
                    var ranking = strategyLibrary.GetRanking(playerId);
                    var bestEntry = ranking.Count > 0 ? ranking[0] : null;
                    perSeatStrategies.Add(new
                    {
                        seat,
                        strategyId = artifact.StrategyId.Value,
                        kind = artifact.Kind.ToString(),
                        winRate = bestEntry is not null && bestEntry.Effectiveness.MatchesPlayed > 0
                            ? (double)bestEntry.Effectiveness.Wins / bestEntry.Effectiveness.MatchesPlayed
                            : 0.0,
                        matchesPlayed = bestEntry?.Effectiveness.MatchesPlayed ?? 0,
                    });
                }
            }

            var coLearningMetrics = state.CoLearningMetrics;
            var perSeatLastPromotionOutcome = coLearningMetrics.LastPromotionOutcomes
                .Select(o => new { seat = o.Seat, outcome = o.Outcome })
                .ToList();

            return Results.Json(new
            {
                isRunning = state.IsRunning,
                iterationCount = state.IterationCount,
                currentStage = current.StageName,
                failureStage = current.FailureStage,
                viewerUrl,
                hostSessionId = state.HostSessionId,
                gamesSimulated = gameBudget.GamesSimulated,
                maxGamesPerRun = gameBudget.MaxGamesPerRun,
                resumeFromBest = hostOptions.ResumeFromBest,
                capReached = state.CapReached,
                learningPlayer,
                learningPlayerStatus,
                learningPlayerError,
                lastIterationError,
                lastPromotionOutcome = state.LastPromotionOutcome,
                libraryEffectiveness,
                deepSeekRetriesUsed = 0,
                strategiesCompiled = scriptHost.CompileInvocationCount,
                strategiesPromoted = coLearningMetrics.LastPromotionOutcomes
                    .Count(o => string.Equals(o.Outcome, "Promoted", StringComparison.OrdinalIgnoreCase)),
                strategiesRejected = coLearningMetrics.LastPromotionOutcomes
                    .Count(o => string.Equals(o.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase)),
                libraryEntryCount = strategyLibrary.AllEntries.Count,
                parallelSessionCount = hostOptions.ParallelSessionCount,
                compiledScriptsCached = scriptHost.CompiledScriptsCached,
                maxCompiledScripts = scriptHost.MaxCompiledScripts,
                libraryIntegrity = libraryIntegrity.Select(static i => new
                {
                    seat = i.Seat,
                    status = i.Status.ToString(),
                }),
                lastPonderFailureReason = state.LastIterationError?.FailureStage == "Ponder"
                    ? state.LastIterationError.Message
                    : null,
                allSeatsLearning = hostOptions.AllSeatsLearning,
                perSeatStrategies,
                coLearningIterationsCompleted = coLearningMetrics.CoLearningIterationsCompleted,
                perSeatLastPromotionOutcome,
                modelProvider = new
                {
                    provider = modelProvider.Provider,
                    model = modelProvider.Model,
                    baseUrl = modelProvider.BaseUrl,
                    timeoutSeconds = modelProvider.TimeoutSeconds,
                    apiKeySource = modelProvider.ApiKeySource,
                },
                perStageModels = new
                {
                    ponderModelId = (string?)hostOptions.PonderModelId,
                    playModelId = (string?)hostOptions.PlayModelId,
                    enhanceModelId = (string?)hostOptions.EnhanceModelId,
                },
            }, BonesWebHost.JsonOptions);
        });

        app.MapGet("/api/bones/library", (BonesStrategyLibrary strategyLibrary) =>
        {
            var entries = strategyLibrary.AllEntries
                .Select(entry => new
                {
                    seat = entry.PlayerId.Seat,
                    strategyId = entry.StrategyId.Value,
                    kind = entry.Kind.ToString(),
                    sourceExcerpt = entry.Source.Length > 200
                        ? entry.Source[..200]
                        : entry.Source,
                    winRate = entry.Effectiveness.MatchesPlayed > 0
                        ? (double)entry.Effectiveness.Wins / entry.Effectiveness.MatchesPlayed
                        : 0.0,
                    lastUpdatedUtc = entry.LastUpdatedUtc,
                })
                .ToList();

            return Results.Json(entries, BonesWebHost.JsonOptions);
        });

        app.MapPost("/api/bones/stop", (BonesLearningLoopHost loopHost, IHostApplicationLifetime lifetime) =>
        {
            loopHost.RequestStop();
            if (ShouldShutdownProcessAfterStop())
            {
                lifetime.StopApplication();
            }

            var state = loopHost.CurrentState;
            return Results.Json(new
            {
                isRunning = state.IsRunning,
                stopRequested = true,
                iterationCount = state.IterationCount,
            }, BonesWebHost.JsonOptions);
        });

        app.MapPost("/api/bones/start", (BonesLearningLoopHost loopHost) =>
        {
            loopHost.RequestStart();
            var state = loopHost.CurrentState;
            return Results.Json(new
            {
                isRunning = state.IsRunning,
                stopRequested = state.StopRequested,
                iterationCount = state.IterationCount,
            }, BonesWebHost.JsonOptions);
        });

        return app;
    }

    internal static bool ShouldShutdownProcessAfterStop()
        => string.Equals(
            Environment.GetEnvironmentVariable("BONES_HOST_STOP_SHUTDOWN"),
            "1",
            StringComparison.Ordinal);

    private static bool IsPonderPendingStage(string stageName)
        => string.Equals(stageName, "Observe", StringComparison.Ordinal)
            || string.Equals(stageName, "Ponder", StringComparison.Ordinal)
            || string.Equals(stageName, "Starting", StringComparison.Ordinal);

    private static string ResolveContentRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDirectory, "wwwroot", "viewer")))
            return baseDirectory;

        var projectRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", ".."));
        if (Directory.Exists(Path.Combine(projectRoot, "wwwroot", "viewer")))
            return projectRoot;

        return baseDirectory;
    }

    private static async Task ShutdownAfterLoopStopsAsync(
        BonesLearningLoopHost loopHost,
        IHostApplicationLifetime lifetime)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (!loopHost.CurrentState.IsRunning)
            {
                lifetime.StopApplication();
                return;
            }

            await Task.Delay(100);
        }

        lifetime.StopApplication();
    }
}