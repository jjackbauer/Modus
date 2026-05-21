using Modus.Core.Hosting;

namespace Modus.Host.Domain.WebApi;

internal sealed record ManagementStatusLoadedPluginResponse(
    string PluginId,
    string AssemblyName,
    string Version,
    string LifecycleState,
    IReadOnlyList<string> Capabilities);

internal sealed record ManagementStatusCapabilityOwnershipResponse(
    string Capability,
    string OwnerPluginId);

internal sealed record ManagementStatusEndpointResponse(
    string State,
    IReadOnlyList<ManagementStatusLoadedPluginResponse> LoadedPlugins,
    IReadOnlyList<ManagementStatusCapabilityOwnershipResponse> CapabilityOwnership,
    IReadOnlyList<string> Diagnostics)
{
    public static ManagementStatusEndpointResponse FromSnapshot(
        HostStatusSnapshot snapshot,
        IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new ManagementStatusEndpointResponse(
            State: snapshot.State.ToString(),
            LoadedPlugins: snapshot.LoadedPlugins
                .Select(static plugin => new ManagementStatusLoadedPluginResponse(
                    PluginId: plugin.PluginId.Value,
                    AssemblyName: plugin.AssemblyName,
                    Version: plugin.Version.ToString(),
                    LifecycleState: plugin.LifecycleState.ToString(),
                    Capabilities: plugin.Capabilities
                        .Select(static capability => capability.Value)
                        .ToArray()))
                .ToArray(),
            CapabilityOwnership: snapshot.CapabilityOwnership
                .Select(static ownership => new ManagementStatusCapabilityOwnershipResponse(
                    Capability: ownership.Capability.Value,
                    OwnerPluginId: ownership.OwnerPluginId.Value))
                .ToArray(),
            Diagnostics: diagnostics.ToArray());
    }
}