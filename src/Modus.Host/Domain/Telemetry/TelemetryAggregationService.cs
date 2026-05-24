using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.Host.Domain.Telemetry;

internal sealed class TelemetryAggregationService
{
    public TelemetryAggregationService(
        IEnumerable<IHostTelemetryPluginContract> hostProviders,
        IEnumerable<IMachineTelemetryPluginContract> machineProviders)
    {
        ArgumentNullException.ThrowIfNull(hostProviders);
        ArgumentNullException.ThrowIfNull(machineProviders);

        HostProviders = OrderProviders(hostProviders);
        MachineProviders = OrderProviders(machineProviders);
    }

    public IReadOnlyList<IHostTelemetryPluginContract> HostProviders { get; }

    public IReadOnlyList<IMachineTelemetryPluginContract> MachineProviders { get; }

    public IReadOnlyList<TelemetryResult> CollectHostTelemetry(CorrelationId? correlationId = null)
        => CollectTelemetry(
            HostProviders,
            expectedOperationPrefix: "Telemetry.Host.",
            correlationId);

    public IReadOnlyList<TelemetryResult> CollectMachineTelemetry(CorrelationId? correlationId = null)
        => CollectTelemetry(
            MachineProviders,
            expectedOperationPrefix: "Telemetry.Machine.",
            correlationId);

    private static IReadOnlyList<TProvider> OrderProviders<TProvider>(IEnumerable<TProvider> providers)
        where TProvider : IPluginContract
    {
        return providers
            .OrderBy(static provider => provider.PluginId.Value, StringComparer.Ordinal)
            .ThenBy(static provider => provider.GetType().AssemblyQualifiedName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<TelemetryResult> CollectTelemetry<TProvider>(
        IReadOnlyList<TProvider> providers,
        string expectedOperationPrefix,
        CorrelationId? correlationId)
        where TProvider : IPluginContract
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOperationPrefix);

        if (providers.Count == 0)
        {
            return [];
        }

        var results = new List<TelemetryResult>(providers.Count);
        foreach (var provider in providers)
        {
            var operationCatalog = provider as IPluginOperationCatalog
                ?? throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' must implement {nameof(IPluginOperationCatalog)}.");

            var operation = ResolveTelemetryOperation(operationCatalog, expectedOperationPrefix, provider.PluginId);
            var response = InvokeProvider(provider, operation, correlationId);

            if (!response.Success || response.Status != SyncResponseStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' failed with status '{response.Status}'.");
            }

            var telemetry = response.Payload switch
            {
                TelemetryResult direct => direct,
                TelemetryOperationPayload { Result: not null } envelope => envelope.Result,
                _ => throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' did not return a {nameof(TelemetryResult)} payload.")
            };

            results.Add(telemetry);
        }

        return results;
    }

    private static SyncResponse InvokeProvider<TProvider>(
        TProvider provider,
        OperationName operation,
        CorrelationId? correlationId)
        where TProvider : IPluginContract
    {
        ArgumentNullException.ThrowIfNull(provider);

        var request = SyncRequest.ForStandardPath(operation, correlationId);

        if (provider is ISyncResponder responder)
        {
            return responder.Handle(request);
        }

        var typedResponderInterface = provider.GetType()
            .GetInterfaces()
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(ISyncResponder<,>)
                && candidate.GenericTypeArguments[0] == typeof(SyncRequest));

        if (typedResponderInterface is null)
        {
            throw new InvalidOperationException(
                $"Telemetry provider '{provider.PluginId.Value}' must implement {nameof(ISyncResponder)} or a typed sync responder contract.");
        }

        var handleMethod = typedResponderInterface.GetMethod("Handle", [typeof(SyncRequest)]);
        if (handleMethod is null)
        {
            throw new InvalidOperationException(
                $"Telemetry provider '{provider.PluginId.Value}' does not expose a callable Handle(SyncRequest) method.");
        }

        var typedResponse = handleMethod.Invoke(provider, [request])
            ?? throw new InvalidOperationException(
                $"Telemetry provider '{provider.PluginId.Value}' returned null response.");

        var responseType = typedResponse.GetType();
        var successProperty = responseType.GetProperty(nameof(SyncResponse.Success));
        var payloadProperty = responseType.GetProperty(nameof(SyncResponse.Payload));
        var statusProperty = responseType.GetProperty(nameof(SyncResponse.Status));
        var correlationProperty = responseType.GetProperty(nameof(SyncResponse.CorrelationId));

        if (successProperty is null || payloadProperty is null || statusProperty is null)
        {
            throw new InvalidOperationException(
                $"Telemetry provider '{provider.PluginId.Value}' returned an unsupported typed response contract.");
        }

        var success = (bool)successProperty.GetValue(typedResponse)!;
        var payload = payloadProperty.GetValue(typedResponse)
            ?? throw new InvalidOperationException(
                $"Telemetry provider '{provider.PluginId.Value}' returned null payload.");
        var status = (SyncResponseStatus)statusProperty.GetValue(typedResponse)!;
        var correlation = correlationProperty?.GetValue(typedResponse) is CorrelationId correlationValue
            ? correlationValue
            : (CorrelationId?)null;

        return new SyncResponse(
            Success: success,
            Payload: payload,
            Status: status,
            CorrelationId: correlation ?? request.CorrelationId);
    }

    private static OperationName ResolveTelemetryOperation(
        IPluginOperationCatalog catalog,
        string expectedOperationPrefix,
        PluginId pluginId)
    {
        var matches = catalog.SupportedOperations
            .Where(operation => operation.Value.StartsWith(expectedOperationPrefix, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Telemetry provider '{pluginId.Value}' does not expose an operation starting with '{expectedOperationPrefix}'."),
            _ => throw new InvalidOperationException(
                $"Telemetry provider '{pluginId.Value}' exposes multiple operations starting with '{expectedOperationPrefix}'.")
        };
    }
}