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
            var responder = provider as ISyncResponder
                ?? throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' must implement {nameof(ISyncResponder)}.");
            var operationCatalog = provider as IPluginOperationCatalog
                ?? throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' must implement {nameof(IPluginOperationCatalog)}.");

            var operation = ResolveTelemetryOperation(operationCatalog, expectedOperationPrefix, provider.PluginId);
            var response = responder.Handle(SyncRequest.ForStandardPath(operation, correlationId));

            if (!response.Success || response.Status != SyncResponseStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' failed with status '{response.Status}'.");
            }

            if (response.PayloadObject is not TelemetryResult telemetry)
            {
                throw new InvalidOperationException(
                    $"Telemetry provider '{provider.PluginId.Value}' did not return a {nameof(TelemetryResult)} payload.");
            }

            results.Add(telemetry);
        }

        return results;
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