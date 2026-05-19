using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.Core.Hosting;

internal sealed class PluginDiDescriptorPolicy
{
    private static readonly Type[] KnownCapabilityContracts =
    [
        typeof(IPluginDependencyRegister),
        typeof(IPluginContract),
        typeof(IPluginLifecycle),
        typeof(IPluginOperationCatalog),
        typeof(IPluginScheduledEvents),
        typeof(IPluginRegistrationPolicy),
        typeof(IEventSubscriber),
        typeof(ISyncResponder)
    ];

    public IReadOnlyList<ServiceDescriptor> BuildDescriptors(Type pluginType, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(pluginType);

        if (!typeof(IPluginContract).IsAssignableFrom(pluginType))
        {
            return [];
        }

        return KnownCapabilityContracts
            .Where(contractType => contractType.IsAssignableFrom(pluginType))
            .OrderBy(contractType => contractType.FullName, StringComparer.Ordinal)
            .Select(contractType => ServiceDescriptor.Describe(
                contractType,
                provider => provider.GetRequiredService(pluginType),
                lifetime))
            .ToArray();
    }
}