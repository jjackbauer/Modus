using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;

namespace Modus.Core.Hosting;

public static class PluginHostingServiceProviderExtensions
{
    public static bool TryResolvePluginsByContractInterfaceName(
        this IServiceProvider provider,
        string contractInterfaceFullName,
        out IReadOnlyList<IPluginContract> plugins)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(contractInterfaceFullName))
        {
            plugins = [];
            return false;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var contractType = assembly.GetType(contractInterfaceFullName, throwOnError: false, ignoreCase: false);
            if (contractType is null || !contractType.IsInterface || !typeof(IPluginContract).IsAssignableFrom(contractType))
            {
                continue;
            }

            var resolved = provider
                .GetServices(contractType)
                .OfType<IPluginContract>()
                .OrderBy(static x => x.PluginId, StringComparer.Ordinal)
                .ToArray();

            if (resolved.Length > 0)
            {
                plugins = resolved;
                return true;
            }
        }

        plugins = [];
        return false;
    }

    public static bool TryResolvePluginByTypeName(this IServiceProvider provider, string pluginTypeFullName, out IPluginContract? plugin)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(pluginTypeFullName))
        {
            plugin = null;
            return false;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var pluginType = assembly.GetType(pluginTypeFullName, throwOnError: false, ignoreCase: false);
            if (pluginType is null)
            {
                continue;
            }

            if (provider.GetService(pluginType) is IPluginContract resolved)
            {
                plugin = resolved;
                return true;
            }
        }

        plugin = null;
        return false;
    }
}
