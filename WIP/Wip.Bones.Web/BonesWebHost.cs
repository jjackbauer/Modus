using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Web.Viewer;

namespace Wip.Bones.Web;

public static class BonesWebHost
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void AddBonesViewerServices(IServiceCollection services)
    {
        services.AddSingleton<BonesGameEngine>();
        services.AddSingleton<BonesBoardVisualLayoutBuilder>();
        services.AddSingleton<BonesMatchViewerService>();
        services.AddSingleton<IBonesMatchCatalog, ImmutableBonesMatchCatalog>();
        services.AddSingleton<IBonesPublicMatchFeed, BonesCatalogPublicMatchFeed>();
        services.AddSingleton<StrategyPoolJsonService>();
    }

    public static void MapMatchRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/bones/sessions/{sessionId}/matches/{matchId}",
            (string sessionId, string matchId, IBonesMatchCatalog catalog, BonesMatchViewerService viewer) =>
            {
                if (!TryResolveMatch(sessionId, matchId, catalog, out var registeredMatch, out var errorResult))
                    return errorResult!;

                var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registeredMatch);
                return Results.Json(snapshot, JsonOptions);
            });

        app.MapGet(
            "/bones/sessions/{sessionId}/matches/{matchId}/frames/{turnIndex}",
            (string sessionId, string matchId, int turnIndex, IBonesMatchCatalog catalog, BonesMatchViewerService viewer) =>
            {
                if (!TryResolveMatch(sessionId, matchId, catalog, out var registeredMatch, out var errorResult))
                    return errorResult!;

                var transcriptLength = registeredMatch.MatchResult.Transcript.Length;
                if (turnIndex < 0 || turnIndex >= transcriptLength)
                    return Results.NotFound();

                var frames = viewer.BuildMatchTimeline(registeredMatch);
                var frame = frames.FirstOrDefault(candidate => candidate.TurnIndex == turnIndex);
                if (frame is null)
                    return Results.NotFound();

                return Results.Json(frame, JsonOptions);
            });

        app.MapGet(
            "/bones/sessions/{sessionId}/matches/{matchId}/view",
            (string sessionId, string matchId, int? turnIndex, IBonesMatchCatalog catalog, BonesMatchViewerService viewer) =>
            {
                if (!TryResolveMatch(sessionId, matchId, catalog, out var registeredMatch, out var errorResult))
                    return errorResult!;

                var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registeredMatch);
                BonesMatchFrame? frame = null;
                if (turnIndex is not null)
                {
                    var frames = viewer.BuildMatchTimeline(registeredMatch);
                    frame = frames.FirstOrDefault(candidate => candidate.TurnIndex == turnIndex.Value);
                    if (frame is null)
                        return Results.NotFound();
                }

                var html = BonesViewerPageRenderer.Render(snapshot, frame, turnIndex);
                return Results.Content(html, "text/html; charset=utf-8");
            });
    }

    public static void MapPoolRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/bones/pool",
            (StrategyPoolJsonService service) =>
            {
                if (StrategyPool.Instance is null)
                    return Results.Json(new { error = "PoolUninitialized" }, JsonOptions, statusCode: 503);

                var snapshot = service.BuildPoolSnapshot();
                return Results.Json(snapshot, JsonOptions);
            });

        app.MapGet(
            "/bones/pool/{strategyId}",
            (string strategyId, StrategyPoolJsonService service) =>
            {
                if (StrategyPool.Instance is null)
                    return Results.Json(new { error = "PoolUninitialized" }, JsonOptions, statusCode: 503);

                var detail = service.BuildStrategyDetail(new BonesStrategyId(strategyId));
                if (detail is null)
                    return Results.NotFound();

                return Results.Json(detail, JsonOptions);
            });

        app.MapGet(
            "/bones/pool/{strategyId}/lineage",
            (string strategyId, StrategyPoolJsonService service) =>
            {
                if (StrategyPool.Instance is null)
                    return Results.Json(new { error = "PoolUninitialized" }, JsonOptions, statusCode: 503);

                var lineage = service.BuildLineageViewModel(new BonesStrategyId(strategyId));
                if (lineage is null)
                    return Results.NotFound();

                return Results.Json(lineage, JsonOptions);
            });

        app.MapGet(
            "/bones/pool/stats",
            (StrategyPoolJsonService service) =>
            {
                if (StrategyPool.Instance is null)
                    return Results.Json(new { error = "PoolUninitialized" }, JsonOptions, statusCode: 503);

                var stats = service.BuildPoolStats();
                return Results.Json(stats, JsonOptions);
            });

        app.MapGet(
            "/bones/pool/{strategyId}/view",
            (string strategyId, StrategyPoolJsonService service) =>
            {
                if (StrategyPool.Instance is null)
                    return Results.Content(
                        StrategyDetailPageRenderer.RenderNotFound(strategyId),
                        "text/html; charset=utf-8",
                        statusCode: 404);

                var detail = service.BuildStrategyDetail(new BonesStrategyId(strategyId));
                if (detail is null)
                    return Results.Content(
                        StrategyDetailPageRenderer.RenderNotFound(strategyId),
                        "text/html; charset=utf-8",
                        statusCode: 404);

                var html = StrategyDetailPageRenderer.Render(detail);
                return Results.Content(html, "text/html; charset=utf-8");
            });

        app.MapGet(
            "/bones/pool/view",
            (StrategyPoolJsonService service) =>
            {
                if (StrategyPool.Instance is null)
                    return Results.Content(
                        StrategyPoolPageRenderer.RenderUninitialized(),
                        "text/html; charset=utf-8");

                var snapshot = service.BuildPoolSnapshot();
                var html = StrategyPoolPageRenderer.Render(snapshot);
                return Results.Content(html, "text/html; charset=utf-8");
            });
    }

    public static void MapStaticViewer(WebApplication app)
    {
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "viewer")),
            RequestPath = "/viewer",
            DefaultFileNames = ["index.html"],
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "viewer")),
            RequestPath = "/viewer",
        });

        app.MapGet("/", () => Results.Redirect("/viewer/index.html"));
    }

    private static bool TryResolveMatch(
        string sessionId,
        string matchId,
        IBonesMatchCatalog catalog,
        out BonesRegisteredMatch registeredMatch,
        out IResult? errorResult)
    {
        registeredMatch = null!;
        errorResult = null;

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(matchId))
        {
            errorResult = Results.BadRequest();
            return false;
        }

        if (!catalog.TryGet(new SessionId(sessionId), new BonesGameId(matchId), out registeredMatch!))
        {
            errorResult = Results.NotFound();
            return false;
        }

        return true;
    }
}
