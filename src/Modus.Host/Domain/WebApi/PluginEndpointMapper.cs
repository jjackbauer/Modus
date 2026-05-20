using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.Host.Domain.WebApi;

/// <summary>
/// Maps plugin operations to HTTP endpoints registered on a minimal-API WebApplication.
/// Joins IPluginContract and IPluginOperationCatalog metadata to register POST routes
/// at /api/{pluginId}/{operation} for each declared plugin operation.
/// </summary>
public class PluginEndpointMapper
{
    private readonly IEnumerable<IPluginContract> _contracts;
    private readonly IEnumerable<IPluginOperationCatalog> _catalogs;

    public PluginEndpointMapper(
        IEnumerable<IPluginContract> contracts,
        IEnumerable<IPluginOperationCatalog> catalogs)
    {
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
    }

    /// <summary>
    /// Registers plugin operation endpoints on the provided WebApplication.
    /// Iterates through each plugin contract and its corresponding operation catalog,
    /// registering a POST route for each supported operation at /api/{pluginId}/{operation}.
    /// </summary>
    /// <param name="app">The WebApplication to register routes on.</param>
    /// <returns>The same WebApplication instance to allow method chaining.</returns>
    public WebApplication Map(WebApplication app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        // For each operation catalog, find its corresponding contract and register routes
        var processedCatalogs = new HashSet<IPluginOperationCatalog>();
        foreach (var catalog in _catalogs)
        {
            if (processedCatalogs.Contains(catalog))
            {
                continue;
            }

            processedCatalogs.Add(catalog);

            // Try to get the contract for this catalog
            // In many cases, the catalog instance also implements IPluginContract
            var contract = catalog as IPluginContract;
            if (contract == null)
            {
                // Fallback: try to find a matching contract by plugin ID
                // If the catalog doesn't implement IPluginContract, we skip it
                // because we have no way to determine which plugin it belongs to
                continue;
            }

            var pluginId = contract.PluginId;
            
            // Register a POST route for each supported operation
            foreach (var operation in catalog.SupportedOperations)
            {
                var routePattern = $"/api/{pluginId}/{operation}";
                var capturedOperation = operation;
                var capturedContract = contract;

                app.MapPost(
                    routePattern,
                    async (PluginOperationHttpRequest request, HttpContext httpContext) =>
                    {
                        return await HandlePluginOperation(
                            request,
                            capturedOperation.Value,
                            capturedContract.PluginId.Value,
                            httpContext.RequestServices);
                    })
                    .WithName($"PluginOperation_{pluginId}_{operation}")
                    .WithOpenApi()
                    .WithTags(pluginId.Value)
                    .WithSummary(operation.Value)
                    .WithDescription($"{capturedContract.ContractName} v{capturedContract.ContractVersion}");
            }
        }

        return app;
    }

    /// <summary>
    /// Handles a plugin operation request by dispatching it through ISyncResponder
    /// and mapping the response to an HTTP response with appropriate status code.
    /// </summary>
    private async Task<IResult> HandlePluginOperation(
        PluginOperationHttpRequest request,
        string operation,
        string pluginId,
        IServiceProvider requestServices)
    {
        try
        {
            // Create a SyncRequest from the HTTP request
            var syncRequest = new SyncRequest(
                Operation: new OperationName(operation),
                IsFallbackExplicit: false,
                FallbackReason: SyncFallbackReason.None,
                FallbackReasonCode: null,
                CorrelationId: request.CorrelationId is not null ? new CorrelationId(request.CorrelationId) : null);

            var pluginScopedResponders = requestServices
                .GetServices<ISyncResponder>()
                .Where(responder => responder is IPluginContract contract
                    && string.Equals(contract.PluginId.Value, pluginId, StringComparison.Ordinal))
                .ToArray();

            if (pluginScopedResponders.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No ISyncResponder registered in request scope for plugin '{pluginId}'.");
            }

            if (pluginScopedResponders.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple ISyncResponder registrations matched plugin '{pluginId}'.");
            }

            var responder = pluginScopedResponders[0];
            
            var response = responder.Handle(syncRequest);

            // Map the SyncResponse to an HTTP response
            var httpResponse = new PluginOperationHttpResponse
            {
                Success = response.Success,
                Payload = response.Payload,
                Status = response.Status,
                CorrelationId = response.CorrelationId?.Value
            };

            // Determine HTTP status code based on SyncResponseStatus
            var statusCode = response.Status switch
            {
                SyncResponseStatus.Success => StatusCodes.Status200OK,
                SyncResponseStatus.Rejected => StatusCodes.Status422UnprocessableEntity,
                SyncResponseStatus.Failed => StatusCodes.Status500InternalServerError,
                _ => StatusCodes.Status500InternalServerError
            };

            return Results.Json(httpResponse, statusCode: statusCode);
        }
        catch (Exception ex)
        {
            // On unhandled error, return 500 with error details
            var errorResponse = new PluginOperationHttpResponse
            {
                Success = false,
                Payload = ex.Message,
                Status = SyncResponseStatus.Failed,
                CorrelationId = request.CorrelationId
            };

            return Results.Json(errorResponse, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
