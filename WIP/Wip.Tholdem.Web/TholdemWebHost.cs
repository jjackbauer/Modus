using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Wip.Tholdem.Web.Live;
using Wip.Tholdem.Web.Recording;
using Wip.Tholdem.Web.Strategies;
using Wip.Tholdem.Web.Training;

namespace Wip.Tholdem.Web;

/// <summary>
/// Composition root for the Tholdem spectator viewer: a live seeded match feed, a read-only
/// recorded-session browser, read-only strategy/ledger views over the frozen data directory, and the
/// training/mini-RSI job surface. Everything is loopback-only and offline; no external URL is ever
/// contacted.
/// </summary>
public static class TholdemWebHost
{
    public const string DefaultUrl = "http://127.0.0.1:5099";

    public const string ViewerRequestPath = "/viewer";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void AddTholdemViewerServices(this IServiceCollection services, TholdemViewerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<LiveMatchRegistry>();
        services.AddSingleton<JobScheduler>();
        services.AddSingleton<TrainingSeedRegistry>();
        services.AddSingleton<TrainingJobRegistry>();
        services.AddSingleton<RsiJobRegistry>();
        services.AddHostedService<BackgroundJobWorker>();
    }

    public static void MapLiveRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            "/api/live",
            (StartLiveMatchRequest? request, LiveMatchRegistry registry, TholdemViewerOptions options) =>
            {
                if (request is null)
                {
                    return Results.BadRequest(new { error = "missing-body" });
                }

                // The deal is deterministic from the seed, so a guessable seed lets an observer
                // enumerate candidates and rebuild the deck. Weak seeds need the explicit opt-in.
                if (request.AllowWeakSeed is not true && !SessionSeedPolicy.IsStrong(request.Seed))
                {
                    return Results.BadRequest(
                        new { error = "weak-seed", minimum = SessionSeedPolicy.MinimumSeed });
                }

                if (request.Hands < 1 || request.Hands > options.MaxLiveHands)
                {
                    return Results.BadRequest(
                        new { error = "invalid-hands", min = 1, max = options.MaxLiveHands });
                }

                var delayMs = request.DelayMs ?? options.DefaultDelayMs;
                if (delayMs < 0 || delayMs > 5000)
                {
                    return Results.BadRequest(new { error = "invalid-delay", min = 0, max = 5000 });
                }

                var seats = request.Seats ?? 2;
                if (seats is < 2 or > 6)
                {
                    return Results.BadRequest(new { error = "invalid-seats", min = 2, max = 6, seats });
                }

                var minimumStack = new Wip.Tholdem.Agents.Simulation.SimulationOptions(
                    request.Seed,
                    request.Hands).BigBlind;
                var startingStacks = request.StartingStacks;
                if (startingStacks is not null)
                {
                    if (startingStacks.Count != seats)
                    {
                        return Results.BadRequest(
                            new { error = "invalid-starting-stacks", seats, count = startingStacks.Count });
                    }

                    for (var seat = 0; seat < startingStacks.Count; seat++)
                    {
                        if (startingStacks[seat] < minimumStack)
                        {
                            return Results.BadRequest(
                                new
                                {
                                    error = "invalid-starting-stacks",
                                    seats,
                                    seat,
                                    value = startingStacks[seat],
                                    minimum = minimumStack,
                                });
                        }
                    }
                }

                string[] names;
                if (request.Agents is not null)
                {
                    if (request.Agents.Count != seats)
                    {
                        return Results.BadRequest(
                            new { error = "invalid-agents", seats, count = request.Agents.Count });
                    }

                    names = request.Agents.ToArray();
                    for (var seat = 0; seat < names.Length; seat++)
                    {
                        if (!LiveAgentCatalog.TryCreate(names[seat], out _))
                        {
                            return Results.BadRequest(
                                new { error = "unknown-agent", seat, agent = names[seat] });
                        }
                    }
                }
                else if (seats == 2)
                {
                    // The pre-multiway two-seat body stays valid: seat0Agent/seat1Agent name the seats.
                    if (!LiveAgentCatalog.TryCreate(request.Seat0Agent, out _))
                    {
                        return Results.BadRequest(
                            new { error = "unknown-agent", seat = 0, agent = request.Seat0Agent });
                    }

                    if (!LiveAgentCatalog.TryCreate(request.Seat1Agent, out _))
                    {
                        return Results.BadRequest(
                            new { error = "unknown-agent", seat = 1, agent = request.Seat1Agent });
                    }

                    names = [request.Seat0Agent!, request.Seat1Agent!];
                }
                else
                {
                    // Three or more seats need one explicit name per seat; two names cannot describe
                    // the table and silently duplicating them would misreport the seated agents.
                    return Results.BadRequest(new { error = "invalid-agents", seats, count = 0 });
                }

                var agents = new Wip.Tholdem.Agents.IPokerAgent[seats];
                for (var seat = 0; seat < seats; seat++)
                {
                    LiveAgentCatalog.TryCreate(names[seat], out agents[seat]);
                }

                var match = registry.Start(request.Seed, request.Hands, delayMs, agents, startingStacks);
                var started = new LiveMatchStarted(
                    match.Id,
                    match.Seed,
                    match.Hands,
                    match.DelayMs,
                    match.Seat0Agent,
                    match.Seat1Agent,
                    match.SeatCount,
                    match.Agents);
                return Results.Json(started, JsonOptions, statusCode: StatusCodes.Status201Created);
            });

        app.MapGet(
            "/api/live/{id}",
            (string id, LiveMatchRegistry registry) =>
                registry.TryGet(id, out var match)
                    ? Results.Json(match!.Snapshot(), JsonOptions)
                    : Results.NotFound());

        app.MapDelete(
            "/api/live/{id}",
            (string id, LiveMatchRegistry registry) =>
                registry.Stop(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost(
            "/api/live/{id}/pause",
            (string id, LiveMatchRegistry registry) =>
            {
                if (!registry.TryGet(id, out var match))
                {
                    return Results.NotFound();
                }

                match!.Pause();
                return Results.Json(new { status = "paused" }, JsonOptions);
            });

        app.MapPost(
            "/api/live/{id}/resume",
            (string id, LiveMatchRegistry registry) =>
            {
                if (!registry.TryGet(id, out var match))
                {
                    return Results.NotFound();
                }

                match!.Resume();
                return Results.Json(new { status = "running" }, JsonOptions);
            });
    }

    public static void MapReplayRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/api/sessions",
            (TholdemViewerOptions options) =>
                Results.Json(SessionCatalog.List(options.DataDirectory), JsonOptions));

        app.MapGet(
            "/api/sessions/{sessionId}",
            (string sessionId, TholdemViewerOptions options) =>
            {
                var detail = SessionCatalog.TryGetDetail(options.DataDirectory, sessionId);
                return detail is null ? Results.NotFound() : Results.Json(detail, JsonOptions);
            });

        app.MapGet(
            "/api/sessions/{sessionId}/hands/{handNumber:int}",
            (string sessionId, int handNumber, TholdemViewerOptions options) =>
            {
                var recordedHand = SessionCatalog.TryReadHand(options.DataDirectory, sessionId, handNumber);
                return recordedHand is null
                    ? Results.NotFound()
                    : Results.Json(recordedHand, JsonOptions);
            });

        app.MapGet(
            "/viewer/table",
            (string? session, int? hand, TholdemViewerOptions options) =>
            {
                if (string.IsNullOrWhiteSpace(session) || hand is null || hand < 1)
                {
                    return Results.NotFound();
                }

                var recordedHand = SessionCatalog.TryReadHand(options.DataDirectory, session, hand.Value);
                return recordedHand is null
                    ? Results.NotFound()
                    : Results.Content(
                        ReplayTableRenderer.Render(session, recordedHand),
                        "text/html; charset=utf-8");
            });
    }

    public static void MapStrategyRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/api/strategies",
            (TholdemViewerOptions options) =>
                Results.Json(StrategyCatalog.ListStrategies(options.DataDirectory), JsonOptions));

        app.MapGet(
            "/api/strategies/{id}",
            (string id, TholdemViewerOptions options) =>
            {
                var detail = StrategyCatalog.TryGetStrategy(options.DataDirectory, id);
                return detail is null ? Results.NotFound() : Results.Json(detail, JsonOptions);
            });

        app.MapGet(
            "/api/ledger",
            (TholdemViewerOptions options) =>
                Results.Json(StrategyCatalog.ListLedger(options.DataDirectory), JsonOptions));

        app.MapGet(
            "/api/champion",
            (TholdemViewerOptions options) =>
            {
                var champion = StrategyCatalog.TryGetChampion(options.DataDirectory);
                return champion is null ? Results.NotFound() : Results.Json(champion.Value, JsonOptions);
            });

        app.MapGet(
            "/api/status",
            (TholdemViewerOptions options, LiveMatchRegistry registry) =>
            {
                var status = new ViewerStatus(
                    "spectator",
                    options.DataDirectory,
                    SessionCatalog.CountSessions(options.DataDirectory),
                    StrategyCatalog.CountStrategies(options.DataDirectory),
                    registry.ActiveCount,
                    true);
                return Results.Json(status, JsonOptions);
            });
    }

    public static void MapStaticViewer(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var viewerDirectory = ResolveViewerDirectory(app);
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = new PhysicalFileProvider(viewerDirectory),
            RequestPath = ViewerRequestPath,
            DefaultFileNames = ["index.html"],
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(viewerDirectory),
            RequestPath = ViewerRequestPath,
        });

        app.MapGet("/", () => Results.Redirect("/viewer/index.html"));
    }

    public static WebApplication Create(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = WebApplication.CreateBuilder(args);
        var dataDirectory = ResolveDataDirectory(args, builder.Environment.ContentRootPath);
        Directory.CreateDirectory(dataDirectory);

        builder.Services.AddTholdemViewerServices(new TholdemViewerOptions(dataDirectory));

        if (!HasUrlsArgument(args))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        var app = builder.Build();
        MapLiveRoutes(app);
        MapReplayRoutes(app);
        MapStrategyRoutes(app);
        app.MapTrainingRoutes();
        app.MapRsiRoutes();
        MapStaticViewer(app);
        return app;
    }

    internal static string ResolveDataDirectory(IReadOnlyList<string> args, string contentRootPath)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("--data-dir requires a directory path.", nameof(args));
                }

                return Path.GetFullPath(args[index + 1]);
            }

            const string prefix = "--data-dir=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = argument[prefix.Length..];
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("--data-dir requires a directory path.", nameof(args));
                }

                return Path.GetFullPath(value);
            }
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, "artifacts", "tholdem"));
    }

    private static string ResolveViewerDirectory(WebApplication app)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(TholdemWebHost).Assembly.Location);
        var candidates = new List<string>
        {
            Path.Combine(app.Environment.ContentRootPath, "wwwroot", "viewer"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "viewer"),
        };

        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            candidates.Add(Path.Combine(assemblyDirectory, "wwwroot", "viewer"));
            var repositoryRoot = FindRepositoryRoot(assemblyDirectory);
            if (repositoryRoot is not null)
            {
                candidates.Add(Path.Combine(repositoryRoot, "WIP", "Wip.Tholdem.Web", "wwwroot", "viewer"));
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the viewer assets (wwwroot/viewer/index.html). Build the Wip.Tholdem.Web project first.");
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool HasUrlsArgument(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--urls", StringComparison.OrdinalIgnoreCase)
                || args[index].StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
    }
}
