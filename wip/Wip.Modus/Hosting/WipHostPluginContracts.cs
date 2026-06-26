using Modus.Core.Plugins;

namespace Wip.Modus.Hosting;

// Marker interface for plugins that explicitly opt into Wip shell-host loading.
public interface IWipHostPluginContract : IPluginContract
{
}