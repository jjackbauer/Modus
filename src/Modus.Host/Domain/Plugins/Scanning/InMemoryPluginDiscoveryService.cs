using Modus.Host.Plugins.Descriptors;

namespace Modus.Host.Plugins.Scanning;

public static class InMemoryPluginDiscoveryService
{
    public static List<PluginDescriptor> Discover(IEnumerable<PluginDescriptor> descriptors)
    {
        var service = new PluginDiscoveryService();
        return service.Discover(descriptors).ToList();
    }
}
