using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modus.Core.Messaging;
using Modus.Host.Domain.Telemetry;

namespace Modus.Host.Domain.WebApi;

internal sealed class ManagementTelemetryEndpointMapper
{
    private readonly TelemetryAggregationService _telemetryAggregation;

    public ManagementTelemetryEndpointMapper(TelemetryAggregationService telemetryAggregation)
    {
        _telemetryAggregation = telemetryAggregation ?? throw new ArgumentNullException(nameof(telemetryAggregation));
    }

    public WebApplication Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/management/telemetry/host",
            (HttpContext httpContext) => HandleHostTelemetry(httpContext))
            .WithName("ManagementTelemetry_Host")
            .WithOpenApi()
            .WithTags("Management")
            .WithSummary("Get host telemetry measurements")
            .WithDescription("Collects typed host telemetry measurements from registered host telemetry providers.");

        app.MapGet(
            "/management/telemetry/machine",
            (HttpContext httpContext) => HandleMachineTelemetry(httpContext))
            .WithName("ManagementTelemetry_Machine")
            .WithOpenApi()
            .WithTags("Management")
            .WithSummary("Get machine telemetry measurements")
            .WithDescription("Collects typed machine telemetry measurements from registered machine telemetry providers.");

        return app;
    }

    private IResult HandleHostTelemetry(HttpContext httpContext)
    {
        var correlationId = new CorrelationId($"management-host:{httpContext.TraceIdentifier}");

        try
        {
            var telemetry = _telemetryAggregation
                .CollectHostTelemetry(correlationId)
                .Select(TelemetryEndpointEnvelope.FromResult)
                .ToArray();
            return Results.Ok(telemetry);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Host telemetry collection failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["correlationId"] = correlationId.Value
                });
        }
    }

    private IResult HandleMachineTelemetry(HttpContext httpContext)
    {
        var correlationId = new CorrelationId($"management-machine:{httpContext.TraceIdentifier}");

        try
        {
            var telemetry = _telemetryAggregation
                .CollectMachineTelemetry(correlationId)
                .Select(TelemetryEndpointEnvelope.FromResult)
                .ToArray();
            return Results.Ok(telemetry);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Machine telemetry collection failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["correlationId"] = correlationId.Value
                });
        }
    }
}