using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;

namespace Modus.Host.Domain.WebApi;

/// <summary>
/// Maps plugin operations to HTTP endpoints registered on a minimal-API WebApplication.
/// Registers one stable POST route at /api/{pluginId}/{operation} and resolves
/// the owning plugin operation from the runtime registry for each request.
/// </summary>
public class PluginEndpointMapper
{
    private readonly RuntimePluginRegistry _runtimePluginRegistry;
    private readonly ConcurrentDictionary<string, ISyncResponder> _singletonResponderCache = new(StringComparer.Ordinal);

    public PluginEndpointMapper(RuntimePluginRegistry runtimePluginRegistry)
    {
        _runtimePluginRegistry = runtimePluginRegistry ?? throw new ArgumentNullException(nameof(runtimePluginRegistry));
        _runtimePluginRegistry.Changed += OnRuntimePluginRegistryChanged;
    }

    public PluginEndpointMapper(
        IEnumerable<IPluginContract> contracts,
        IEnumerable<IPluginOperationCatalog> catalogs)
        : this(new RuntimePluginRegistry(contracts, catalogs))
    {
    }

    /// <summary>
    /// Registers plugin operation endpoints on the provided WebApplication.
    /// Maps one stable route that dispatches to plugin operations using runtime lookup.
    /// </summary>
    /// <param name="app">The WebApplication to register routes on.</param>
    /// <returns>The same WebApplication instance to allow method chaining.</returns>
    public WebApplication Map(WebApplication app)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        app.MapPost(
            "/api/{pluginId}/{operation}",
            async (string pluginId, string operation, PluginOperationHttpRequest request, HttpContext httpContext) =>
            {
                return await HandlePluginOperation(
                    request,
                    operation,
                    pluginId,
                    httpContext.RequestServices);
            })
            .WithName("PluginOperation")
            .WithOpenApi()
            .WithTags("Plugins")
            .WithSummary("Dispatch plugin operation")
            .WithDescription("Dynamically dispatches plugin operations resolved from the runtime registry.");

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
            var snapshot = _runtimePluginRegistry.GetSnapshot();

            // Create a SyncRequest from the HTTP request
            var syncRequest = new SyncRequest(
                Operation: new OperationName(operation),
                IsFallbackExplicit: false,
                FallbackReason: SyncFallbackReason.None,
                FallbackReasonCode: null,
                CorrelationId: request.CorrelationId is not null ? new CorrelationId(request.CorrelationId) : null);

            string ownerPluginId;
            try
            {
                ownerPluginId = ResolveOperationOwnerPluginId(snapshot, pluginId, operation);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("No runtime plugin operation owner found", StringComparison.Ordinal))
            {
                RecordDispatchMiss(requestServices, pluginId, operation, ex.Message);
                throw;
            }

            var pluginScopedResponders = requestServices
                .GetServices<ISyncResponder>()
                .Where(responder => responder is IPluginContract contract
                    && string.Equals(contract.PluginId.Value, ownerPluginId, StringComparison.Ordinal))
                .ToArray();

            using var responderLease = ResolveResponder(snapshot, ownerPluginId, pluginScopedResponders, requestServices);
            var responder = responderLease.Responder;

            var response = responder.Handle(syncRequest);

            // Map the SyncResponse to an HTTP response
            var httpResponse = new PluginOperationHttpResponse
            {
                Success = response.Success,
                Payload = response.Payload,
                PayloadObject = response.PayloadObject,
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

    private static void RecordDispatchMiss(IServiceProvider requestServices, string pluginId, string operation, string reason)
    {
        var statusRegistry = requestServices.GetService<HostStatusRegistry>();
        statusRegistry?.AppendDiagnostics([$"stage=dispatch outcome=miss plugin={pluginId} operation={operation} reason={reason}"]);
    }

    private static string ResolveOperationOwnerPluginId(RuntimePluginSnapshot snapshot, string pluginId, string operation)
    {
        var matchingOwners = snapshot.Catalogs
            .OfType<IPluginContract>()
            .Where(contract => contract is IPluginOperationCatalog catalog
                && string.Equals(contract.PluginId.Value, pluginId, StringComparison.Ordinal)
                && catalog.SupportedOperations.Any(candidate => string.Equals(candidate.Value, operation, StringComparison.Ordinal)))
            .Select(static contract => contract.PluginId.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return matchingOwners.Length switch
        {
            1 => matchingOwners[0],
            0 => throw new InvalidOperationException(
                $"No runtime plugin operation owner found for plugin '{pluginId}' and operation '{operation}'."),
            _ => throw new InvalidOperationException(
                $"Multiple runtime plugin operation owners found for plugin '{pluginId}' and operation '{operation}'.")
        };
    }

    private ResolvedResponderLease ResolveResponder(
        RuntimePluginSnapshot snapshot,
        string ownerPluginId,
        IReadOnlyList<ISyncResponder> pluginScopedResponders,
        IServiceProvider requestServices)
    {
        if (pluginScopedResponders.Count == 1)
        {
            return new ResolvedResponderLease(pluginScopedResponders[0], scope: null);
        }

        if (pluginScopedResponders.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple ISyncResponder registrations matched plugin '{ownerPluginId}'.");
        }

        var runtimeDispatchTargets = snapshot.Contracts
            .Where(contract => string.Equals(contract.PluginId.Value, ownerPluginId, StringComparison.Ordinal))
            .OfType<IRuntimePluginDispatchTarget>()
            .ToArray();

        if (runtimeDispatchTargets.Length == 1)
        {
            return ResolveRuntimeDispatchTarget(runtimeDispatchTargets[0], requestServices);
        }

        if (runtimeDispatchTargets.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple runtime dispatch targets matched plugin '{ownerPluginId}'.");
        }

        var runtimeResponders = snapshot.Contracts
            .Where(contract => string.Equals(contract.PluginId.Value, ownerPluginId, StringComparison.Ordinal))
            .OfType<ISyncResponder>()
            .ToArray();

        return runtimeResponders.Length switch
        {
            1 => new ResolvedResponderLease(runtimeResponders[0], scope: null),
            0 => throw new InvalidOperationException(
                $"No ISyncResponder registered in request scope for plugin '{ownerPluginId}'."),
            _ => throw new InvalidOperationException(
                $"Multiple ISyncResponder registrations matched plugin '{ownerPluginId}'.")
        };
    }

    private ResolvedResponderLease ResolveRuntimeDispatchTarget(
        IRuntimePluginDispatchTarget dispatchTarget,
        IServiceProvider requestServices)
    {
        if (string.IsNullOrWhiteSpace(dispatchTarget.PluginTypeFullName))
        {
            throw new InvalidOperationException(
                $"Runtime dispatch target '{dispatchTarget.PluginId.Value}' does not declare a plugin type name.");
        }

        if (dispatchTarget.ServiceLifetime is null)
        {
            // Backward-compatible fallback for descriptors that provide plugin type metadata
            // but were authored before explicit lifetime projection was introduced.
            return new ResolvedResponderLease(
                ResolveResponderByTypeName(requestServices, dispatchTarget.PluginTypeFullName),
                scope: null);
        }

        return dispatchTarget.ServiceLifetime.Value switch
        {
            PluginServiceLifetime.Singleton => new ResolvedResponderLease(
                _singletonResponderCache.GetOrAdd(
                    dispatchTarget.PluginId.Value,
                    _ => ResolveResponderByTypeName(requestServices, dispatchTarget.PluginTypeFullName)),
                scope: null),
            PluginServiceLifetime.Scoped => new ResolvedResponderLease(
                ResolveResponderByTypeName(requestServices, dispatchTarget.PluginTypeFullName),
                scope: null),
            PluginServiceLifetime.Transient => CreateTransientResponderLease(dispatchTarget, requestServices),
            _ => throw new InvalidOperationException(
                $"Unsupported plugin service lifetime '{dispatchTarget.ServiceLifetime.Value}' for plugin '{dispatchTarget.PluginId.Value}'.")
        };
    }

    private static ResolvedResponderLease CreateTransientResponderLease(
        IRuntimePluginDispatchTarget dispatchTarget,
        IServiceProvider requestServices)
    {
        var scopeFactory = requestServices.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateScope();

        try
        {
            var responder = ResolveResponderByTypeName(scope.ServiceProvider, dispatchTarget.PluginTypeFullName!);
            return new ResolvedResponderLease(responder, scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static ISyncResponder ResolveResponderByTypeName(IServiceProvider serviceProvider, string pluginTypeFullName)
    {
        if (serviceProvider.TryResolvePluginByTypeName(pluginTypeFullName, out var resolvedPlugin)
            && resolvedPlugin is ISyncResponder responder)
        {
            return responder;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var pluginType = assembly.GetType(pluginTypeFullName, throwOnError: false, ignoreCase: false);
            if (pluginType is null)
            {
                continue;
            }

            if (!typeof(ISyncResponder).IsAssignableFrom(pluginType) || !typeof(IPluginContract).IsAssignableFrom(pluginType))
            {
                break;
            }

            try
            {
                var activated = ActivatorUtilities.CreateInstance(serviceProvider, pluginType);
                if (activated is ISyncResponder activatedResponder)
                {
                    return activatedResponder;
                }
            }
            catch
            {
                break;
            }
        }

        throw new InvalidOperationException(
            $"No ISyncResponder could be resolved for plugin type '{pluginTypeFullName}'.");
    }

    private void OnRuntimePluginRegistryChanged(object? sender, RuntimePluginRegistryChangedEventArgs change)
    {
        foreach (var pluginId in change.RemovedPluginIds)
        {
            if (_singletonResponderCache.TryRemove(pluginId, out var responder))
            {
                DisposeResponder(responder);
            }
        }
    }

    private static void DisposeResponder(ISyncResponder responder)
    {
        switch (responder)
        {
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private sealed class ResolvedResponderLease : IDisposable
    {
        private readonly IServiceScope? _scope;

        public ResolvedResponderLease(ISyncResponder responder, IServiceScope? scope)
        {
            Responder = responder;
            _scope = scope;
        }

        public ISyncResponder Responder { get; }

        public void Dispose()
        {
            _scope?.Dispose();
        }
    }
}
