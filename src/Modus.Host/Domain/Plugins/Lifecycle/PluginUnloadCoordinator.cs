using Modus.Core.Plugins;
using Modus.Host.Plugins.Results;

namespace Modus.Host.Plugins.Lifecycle;

public sealed class PluginUnloadCoordinator
{
    public LifecycleResult OrchestrateHotUnload(string pluginId, bool wasActive, bool hostHealthy)
    {
        if (!wasActive)
        {
            return new LifecycleResult([], ["plugin not active"], hostHealthy);
        }

        return new LifecycleResult(
            Transitions: [PluginRuntimeState.Deactivating, PluginRuntimeState.Unloaded],
            Diagnostics:
            [
                $"phase=deactivating plugin={pluginId} outcome=success",
                $"phase=unloaded plugin={pluginId} outcome=success",
            ],
            HostHealthy: hostHealthy);
    }
}