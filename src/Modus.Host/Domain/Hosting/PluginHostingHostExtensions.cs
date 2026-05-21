using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.Telemetry;
using Modus.Host.Domain.WebApi;
using Modus.Host.Plugins.Scanning;
using Modus.Host.Plugins;
using Modus.Host.Plugins.Validation;
using System.Reflection;

namespace Modus.Host.Hosting;

public static class PluginHostingHostExtensions
{
    public static IServiceCollection AddModusPluginHosting(
        this IServiceCollection services,
        Action<PluginHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        PluginHostingOptions? options = null;
        services.TryAddSingleton(_ =>
        {
            options = new PluginHostingOptions();
            configure?.Invoke(options);
            return options;
        });

        services.AddPluginHostingCore();

        // Build a temporary service provider to get the options for plugin scanning.
        using var tempProvider = services.BuildServiceProvider();
        var pluginOptions = tempProvider.GetRequiredService<PluginHostingOptions>();

        // Wire the core DI registration extension with runtime plugin assemblies.
        var loader = new PluginLoader();
        var scanResult = loader.ScanRuntimeAssemblies(pluginOptions.PluginsPath);
        
        if (scanResult.Descriptors.Count > 0)
        {
            var loadedAssemblies = new List<Assembly>();
            foreach (var descriptor in scanResult.Descriptors)
            {
                if (string.IsNullOrWhiteSpace(descriptor.AssemblyPath))
                {
                    continue;
                }

                try
                {
                    var asm = Assembly.LoadFrom(descriptor.AssemblyPath);
                    loadedAssemblies.Add(asm);
                }
                catch
                {
                    // Assembly load failure already logged in scan diagnostics.
                }
            }

            if (loadedAssemblies.Count > 0)
            {
                services.AddDiscoveredPlugins(loadedAssemblies);
            }
        }

        services.AddModusPluginHostingRuntime();

        return services;
    }

    public static IServiceCollection AddModusPluginHostingRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PluginDiscoveryService>();
        services.TryAddSingleton<PluginValidationService>();
        services.TryAddSingleton<InMemoryHostRuntime>();
        services.TryAddSingleton<PluginFolderWatcher>(
            static sp => new PluginFolderWatcher(sp));
        services.TryAddSingleton<HostRunner>(
            static sp => new HostRunner(
                sp.GetRequiredService<PluginHostingOptions>(),
                sp,
                sp.GetRequiredService<PluginFolderWatcher>()));
        services.TryAddSingleton<HostStatusRegistry>();
        services.TryAddSingleton<HostStatusSnapshotBuilder>();
        services.TryAddSingleton<TelemetryAggregationService>();
        
        // Register the endpoint mapper that registers plugin operation routes
        services.TryAddSingleton<PluginEndpointMapper>();
        services.TryAddSingleton<ManagementTelemetryEndpointMapper>();
        services.TryAddSingleton<ManagementStatusEndpointMapper>();

        return services;
    }
}
