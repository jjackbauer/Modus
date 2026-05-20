using Modus.Host.Plugins.Descriptors;

namespace Modus.Host.Plugins.Scanning;

public sealed class PluginDiscoveryService
{
    public IReadOnlyList<PluginDescriptor> Discover(IEnumerable<PluginDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        return descriptors
            .OrderBy(x => x.PluginId.Value, StringComparer.Ordinal)
            .ToList();
    }
}