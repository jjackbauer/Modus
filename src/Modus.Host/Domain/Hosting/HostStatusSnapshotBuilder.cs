using Modus.Core.Hosting;
using Modus.Core.Plugins;
using Modus.Host.Plugins.Descriptors;

namespace Modus.Host.Domain.Hosting;

internal sealed class HostStatusSnapshotBuilder
{
    public HostStatusSnapshot Build(
        bool hostHealthy,
        IReadOnlyCollection<PluginDescriptor> descriptors,
        IReadOnlyCollection<string> activatedPluginIds,
        IReadOnlyCollection<string> failedPluginIds,
        IReadOnlyDictionary<string, string>? capabilityOwners)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(activatedPluginIds);
        ArgumentNullException.ThrowIfNull(failedPluginIds);

        var activated = activatedPluginIds.ToHashSet(StringComparer.Ordinal);

        var loadedPlugins = descriptors
            .Where(descriptor => activated.Contains(descriptor.PluginId.Value))
            .OrderBy(descriptor => descriptor.PluginId.Value, StringComparer.Ordinal)
            .Select(static descriptor => new LoadedPluginMetadata(
                PluginId: descriptor.PluginId,
                AssemblyName: descriptor.AssemblyName,
                Version: descriptor.Version,
                LifecycleState: PluginRuntimeState.Active,
                Capabilities: descriptor.Capabilities
                    .OrderBy(capability => capability.Value, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        var ownership = (capabilityOwners ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => new CapabilityOwnershipSnapshot(
                Capability: new CapabilityName(entry.Key),
                OwnerPluginId: new PluginId(entry.Value)))
            .ToArray();

        var state = hostHealthy
            ? (failedPluginIds.Count > 0 ? HostRuntimeState.Degraded : HostRuntimeState.Running)
            : HostRuntimeState.Failed;

        return new HostStatusSnapshot(
            State: state,
            LoadedPlugins: loadedPlugins,
            CapabilityOwnership: ownership);
    }
}