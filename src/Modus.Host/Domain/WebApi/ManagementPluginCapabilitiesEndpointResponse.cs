namespace Modus.Host.Domain.WebApi;

internal sealed record ManagementPluginCapabilitiesOwnerResponse(
    string Capability,
    string OwnerPluginId);

internal sealed record ManagementPluginCapabilitiesPluginResponse(
    string PluginId,
    IReadOnlyList<string> Capabilities);

internal sealed record ManagementPluginCapabilitiesEndpointResponse(
    IReadOnlyList<ManagementPluginCapabilitiesOwnerResponse> Capabilities,
    IReadOnlyList<ManagementPluginCapabilitiesPluginResponse> Plugins)
{
    public static ManagementPluginCapabilitiesEndpointResponse FromStatus(ManagementStatusEndpointResponse status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new ManagementPluginCapabilitiesEndpointResponse(
            Capabilities: status.CapabilityOwnership
                .OrderBy(static ownership => ownership.Capability, StringComparer.Ordinal)
                .Select(static ownership => new ManagementPluginCapabilitiesOwnerResponse(
                    Capability: ownership.Capability,
                    OwnerPluginId: ownership.OwnerPluginId))
                .ToArray(),
            Plugins: status.LoadedPlugins
                .OrderBy(static plugin => plugin.PluginId, StringComparer.Ordinal)
                .Select(static plugin => new ManagementPluginCapabilitiesPluginResponse(
                    PluginId: plugin.PluginId,
                    Capabilities: plugin.Capabilities
                        .OrderBy(static capability => capability, StringComparer.Ordinal)
                        .ToArray()))
                .ToArray());
    }
}