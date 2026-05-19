using Modus.Host.Plugins.Descriptors;
using PluginLoadResult = Modus.Host.Plugins.PluginLoadResult;

namespace Modus.Host.Plugins.Scanning;

public static class InMemoryPluginLoader
{
    public static PluginLoadResult Load(PluginDescriptor descriptor)
    {
        if (!descriptor.UsesOnlyStandardLibrary)
        {
            return new PluginLoadResult(false, true, ["Third-party dependency detected in runtime plugin path."]);
        }

        return new PluginLoadResult(true, false, []);
    }
}
