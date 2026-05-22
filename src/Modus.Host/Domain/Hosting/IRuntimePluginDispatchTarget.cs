using Modus.Core.Plugins;

namespace Modus.Host.Domain.Hosting;

public interface IRuntimePluginDispatchTarget : IPluginContract, IPluginOperationCatalog
{
    string? PluginTypeFullName { get; }

    PluginServiceLifetime? ServiceLifetime { get; }
}