using Modus.Core.Plugins;

namespace Modus.Core.Hosting;

public enum HostRuntimeState
{
    Running,
    Degraded,
    Failed,
}

public sealed record LoadedPluginMetadata(
    PluginId PluginId,
    string AssemblyName,
    Version Version,
    PluginRuntimeState LifecycleState,
    IReadOnlyList<CapabilityName> Capabilities);

public sealed record CapabilityOwnershipSnapshot(
    CapabilityName Capability,
    PluginId OwnerPluginId);

public sealed record HostStatusSnapshot(
    HostRuntimeState State,
    IReadOnlyList<LoadedPluginMetadata> LoadedPlugins,
    IReadOnlyList<CapabilityOwnershipSnapshot> CapabilityOwnership);