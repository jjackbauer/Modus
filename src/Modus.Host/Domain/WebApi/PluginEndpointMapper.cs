using System.Collections.Concurrent;
using System.Reflection;
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
            catch (InvalidOperationException ex) when (TryClassifyDispatchFailureReason(ex.Message, out var reasonCode))
            {
                RecordDispatchFailure(requestServices, pluginId, operation, reasonCode!, ex.Message);
                throw;
            }

            var pluginScopedResponders = requestServices
                .GetServices<ISyncResponder>()
                .Where(responder => responder is IPluginContract contract
                    && string.Equals(contract.PluginId.Value, ownerPluginId, StringComparison.Ordinal))
                .ToArray();

            ResolvedResponderLease responderLease;
            try
            {
                responderLease = ResolveResponder(snapshot, ownerPluginId, pluginScopedResponders, requestServices);
            }
            catch (InvalidOperationException ex) when (TryClassifyDispatchFailureReason(ex.Message, out var reasonCode))
            {
                RecordDispatchFailure(requestServices, pluginId, operation, reasonCode!, ex.Message);
                throw;
            }

            using (responderLease)
            {
                var responder = responderLease.Responder;

                var response = responder.Handle(syncRequest);

                // Map the SyncResponse to an HTTP response
                var httpResponse = new PluginOperationHttpResponse
                {
                    Success = response.Success,
                    Payload = response.Payload,
                    Status = response.Status,
                    CorrelationId = response.CorrelationId?.Value ?? request.CorrelationId
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
        }
        catch (Exception ex)
        {
            // On unhandled error, return 500 with error details
            var errorResponse = new PluginOperationHttpResponse
            {
                Success = false,
                Payload = new SyncErrorPayload(
                    Code: "dispatch-failure",
                    Message: ex.Message),
                Status = SyncResponseStatus.Failed,
                CorrelationId = request.CorrelationId
            };

            return Results.Json(errorResponse, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static void RecordDispatchFailure(IServiceProvider requestServices, string pluginId, string operation, string reasonCode, string reason)
    {
        var statusRegistry = requestServices.GetService<HostStatusRegistry>();
        statusRegistry?.AppendDiagnostics([
            $"stage=dispatch outcome=failure reason={reasonCode} plugin={pluginId} operation={operation} detail={reason}",
            $"stage=dispatch outcome=miss plugin={pluginId} operation={operation} reason={reason}"
        ]);
    }

    private static bool TryClassifyDispatchFailureReason(string message, out string? reasonCode)
    {
        reasonCode = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (message.StartsWith("No runtime plugin operation owner found", StringComparison.Ordinal)
            || message.StartsWith("Multiple runtime plugin operation owners found", StringComparison.Ordinal))
        {
            reasonCode = "owner-mismatch";
            return true;
        }

        if (message.StartsWith("No ISyncResponder registered in request scope", StringComparison.Ordinal)
            || message.StartsWith("No ISyncResponder could be resolved for plugin type", StringComparison.Ordinal)
            || message.StartsWith("Multiple ISyncResponder registrations matched plugin", StringComparison.Ordinal)
            || message.StartsWith("Multiple runtime dispatch targets matched plugin", StringComparison.Ordinal)
            || message.StartsWith("Runtime dispatch target", StringComparison.Ordinal))
        {
            reasonCode = "unresolved-responder";
            return true;
        }

        return false;
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
            try
            {
                return ResolveRuntimeDispatchTarget(runtimeDispatchTargets[0], requestServices);
            }
            catch (InvalidOperationException)
            {
                var fallbackResponders = ResolveRuntimeContractResponders(snapshot, ownerPluginId);
                if (fallbackResponders.Length == 1)
                {
                    return new ResolvedResponderLease(fallbackResponders[0], scope: null);
                }

                throw;
            }
        }

        if (runtimeDispatchTargets.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple runtime dispatch targets matched plugin '{ownerPluginId}'.");
        }

        var runtimeResponders = ResolveRuntimeContractResponders(snapshot, ownerPluginId);

        return runtimeResponders.Length switch
        {
            1 => new ResolvedResponderLease(runtimeResponders[0], scope: null),
            0 => throw new InvalidOperationException(
                $"No ISyncResponder registered in request scope for plugin '{ownerPluginId}'."),
            _ => throw new InvalidOperationException(
                $"Multiple ISyncResponder registrations matched plugin '{ownerPluginId}'.")
        };
    }

    private static ISyncResponder[] ResolveRuntimeContractResponders(RuntimePluginSnapshot snapshot, string ownerPluginId)
    {
        return snapshot.Contracts
            .Where(contract => string.Equals(contract.PluginId.Value, ownerPluginId, StringComparison.Ordinal))
            .Select(contract =>
            {
                if (contract is ISyncResponder responder)
                {
                    return responder;
                }

                var pluginContract = contract as IPluginContract;
                if (pluginContract is null)
                {
                    return null;
                }

                return TryResolveTypedOrLegacyResponder(
                        pluginContract,
                        pluginContract.GetType().FullName ?? pluginContract.GetType().Name,
                        out var adapted)
                    ? adapted
                    : null;
            })
            .Where(static responder => responder is not null)
            .Cast<ISyncResponder>()
            .ToArray();
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
            && resolvedPlugin is not null
            && TryResolveTypedOrLegacyResponder(resolvedPlugin, pluginTypeFullName, out var responder))
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

            if (!typeof(IPluginContract).IsAssignableFrom(pluginType)
                || !ImplementsSupportedSyncResponderContract(pluginType))
            {
                continue;
            }

            try
            {
                var activated = ActivatorUtilities.CreateInstance(serviceProvider, pluginType);
                if (activated is IPluginContract activatedPlugin
                    && TryResolveTypedOrLegacyResponder(activatedPlugin, pluginTypeFullName, out var activatedResponder))
                {
                    return activatedResponder;
                }
            }
            catch
            {
                // Fall back to parameterless activation for plugin types that cannot be fully
                // constructed through the current request scope but still expose a valid responder.
                if (pluginType.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                try
                {
                    var activated = Activator.CreateInstance(pluginType);
                    if (activated is IPluginContract activatedPlugin
                        && TryResolveTypedOrLegacyResponder(activatedPlugin, pluginTypeFullName, out var activatedResponder))
                    {
                        return activatedResponder;
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        throw new InvalidOperationException(
            $"No ISyncResponder could be resolved for plugin type '{pluginTypeFullName}'.");
    }

    private static bool ImplementsSupportedSyncResponderContract(Type pluginType)
    {
        if (typeof(ISyncResponder).IsAssignableFrom(pluginType))
        {
            return true;
        }

        return pluginType
            .GetInterfaces()
            .Any(static candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(ISyncResponder<,>)
                && candidate.GenericTypeArguments[0] == typeof(SyncRequest)
                && candidate.GenericTypeArguments[1].IsGenericType
                && candidate.GenericTypeArguments[1].GetGenericTypeDefinition() == typeof(SyncResponse<>));
    }

    private static bool TryResolveTypedOrLegacyResponder(IPluginContract plugin, string pluginTypeFullName, out ISyncResponder responder)
    {
        if (plugin is ISyncResponder typedResponder)
        {
            responder = typedResponder;
            return true;
        }

        var legacySyncResponderInterface = plugin.GetType()
            .GetInterfaces()
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(ISyncResponder<,>)
                && candidate.GenericTypeArguments[0] == typeof(SyncRequest));

        if (legacySyncResponderInterface is null)
        {
            responder = null!;
            return false;
        }

        var responseType = legacySyncResponderInterface.GenericTypeArguments[1];
        var handleMethod = legacySyncResponderInterface.GetMethod(nameof(ISyncResponder<SyncRequest, SyncResponse<object>>.Handle));

        if (handleMethod is null)
        {
            responder = new LegacyUnsupportedResponderAdapter(
                pluginTypeFullName,
                responseType,
                "Legacy responder does not expose a callable Handle(SyncRequest) method.");
            return true;
        }

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(SyncResponse<>))
        {
            responder = new LegacyUnsupportedResponderAdapter(
                pluginTypeFullName,
                responseType,
                $"Legacy responder returns '{responseType.FullName}', expected SyncResponse<TPayload>.");
            return true;
        }

        responder = new LegacySyncResponseAdapter(plugin, handleMethod, pluginTypeFullName, responseType);
        return true;
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

    private sealed class LegacySyncResponseAdapter : ISyncResponder
    {
        private readonly IPluginContract _plugin;
        private readonly MethodInfo _handleMethod;
        private readonly string _pluginTypeFullName;
        private readonly Type _legacyResponseType;

        public LegacySyncResponseAdapter(
            IPluginContract plugin,
            MethodInfo handleMethod,
            string pluginTypeFullName,
            Type legacyResponseType)
        {
            _plugin = plugin;
            _handleMethod = handleMethod;
            _pluginTypeFullName = pluginTypeFullName;
            _legacyResponseType = legacyResponseType;
        }

        public SyncResponse Handle(SyncRequest request)
        {
            var response = _handleMethod.Invoke(_plugin, [request]);
            if (response is null)
            {
                return LegacyUnsupportedResponderAdapter.Reject(
                    request,
                    _pluginTypeFullName,
                    _legacyResponseType,
                    "Legacy responder returned null instead of SyncResponse<TPayload>.");
            }

            var responseType = response.GetType();
            var successProperty = responseType.GetProperty(nameof(SyncResponse.Success));
            var payloadProperty = responseType.GetProperty(nameof(SyncResponse.Payload));
            var statusProperty = responseType.GetProperty(nameof(SyncResponse.Status));
            var correlationIdProperty = responseType.GetProperty(nameof(SyncResponse.CorrelationId));

            if (successProperty is null || payloadProperty is null || statusProperty is null)
            {
                return LegacyUnsupportedResponderAdapter.Reject(
                    request,
                    _pluginTypeFullName,
                    _legacyResponseType,
                    "Legacy SyncResponse<TPayload> does not expose required response properties.");
            }

            var success = (bool)successProperty.GetValue(response)!;
            var payload = payloadProperty.GetValue(response);
            var status = (SyncResponseStatus)statusProperty.GetValue(response)!;
            var correlation = correlationIdProperty?.GetValue(response) is CorrelationId correlationValue
                ? correlationValue
                : (CorrelationId?)null;

            if (payload is null)
            {
                return LegacyUnsupportedResponderAdapter.Reject(
                    request,
                    _pluginTypeFullName,
                    _legacyResponseType,
                    "Legacy SyncResponse<TPayload> returned a null payload.");
            }

            return new SyncResponse(
                Success: success,
                Payload: payload,
                Status: status,
                CorrelationId: correlation ?? request.CorrelationId);
        }
    }

    private sealed class LegacyUnsupportedResponderAdapter : ISyncResponder
    {
        private readonly string _pluginTypeFullName;
        private readonly Type _responseType;
        private readonly string _reason;

        public LegacyUnsupportedResponderAdapter(string pluginTypeFullName, Type responseType, string reason)
        {
            _pluginTypeFullName = pluginTypeFullName;
            _responseType = responseType;
            _reason = reason;
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return Reject(request, _pluginTypeFullName, _responseType, _reason);
        }

        public static SyncResponse Reject(SyncRequest request, string pluginTypeFullName, Type responseType, string reason)
        {
            return new SyncResponse(
                Success: false,
                Payload: new SyncErrorPayload(
                    Code: "unsupported-operation",
                    Message: $"Plugin '{pluginTypeFullName}' cannot be adapted from legacy response type '{responseType.FullName}'. {reason}"),
                Status: SyncResponseStatus.Rejected,
                CorrelationId: request.CorrelationId);
        }
    }
}
