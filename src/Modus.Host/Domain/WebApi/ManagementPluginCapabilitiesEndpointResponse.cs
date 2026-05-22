using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;

namespace Modus.Host.Domain.WebApi;

internal sealed record ManagementPluginCapabilitiesOwnerResponse(
    string Capability,
    string OwnerPluginId);

internal sealed record ManagementPluginCapabilitiesPluginResponse(
    string PluginId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Operations);

internal sealed record ManagementPluginCapabilitiesEndpointResponse(
    IReadOnlyList<ManagementPluginCapabilitiesOwnerResponse> Capabilities,
    IReadOnlyList<ManagementPluginCapabilitiesPluginResponse> Plugins)
{
    public static ManagementPluginCapabilitiesEndpointResponse FromStatus(
        ManagementStatusEndpointResponse status,
        RuntimePluginSnapshot runtimeSnapshot)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(runtimeSnapshot);

        var operationsByPlugin = runtimeSnapshot.Catalogs
            .Select(catalog => (
                Contract: catalog as IPluginContract,
                Catalog: catalog))
            .Where(entry => entry.Contract is not null)
            .GroupBy(
                entry => entry.Contract!.PluginId.Value,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .SelectMany(static entry => entry.Catalog.SupportedOperations)
                    .Select(static operation => operation.Value)
                    .Where(static operation => !string.IsNullOrWhiteSpace(operation))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static operation => operation, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return new ManagementPluginCapabilitiesEndpointResponse(
            Capabilities: status.CapabilityOwnership
                .OrderBy(static ownership => ownership.Capability, StringComparer.Ordinal)
                .Select(static ownership => new ManagementPluginCapabilitiesOwnerResponse(
                    Capability: ownership.Capability,
                    OwnerPluginId: ownership.OwnerPluginId))
                .ToArray(),
            Plugins: status.LoadedPlugins
                .OrderBy(static plugin => plugin.PluginId, StringComparer.Ordinal)
                .Select(plugin => new ManagementPluginCapabilitiesPluginResponse(
                    PluginId: plugin.PluginId,
                    Capabilities: plugin.Capabilities
                        .OrderBy(static capability => capability, StringComparer.Ordinal)
                        .ToArray(),
                    Operations: operationsByPlugin.TryGetValue(plugin.PluginId, out var operations)
                        ? operations
                        : []))
                .ToArray());
    }
}