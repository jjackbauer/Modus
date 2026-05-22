using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Hosting;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginFolderWatcherRuntimeRegistryTests
{
    [Fact]
    [Trait("ChecklistItem", "Update PluginFolderWatcher onboarding flow to publish add/remove changes into RuntimePluginRegistry [depends on runtime registry abstraction]")]
    public void PluginFolderWatcher_GivenValidPluginOnboarded_ExpectedRegistryUpdatedAndDiagnosticEmitted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-runtime-add-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            using var provider = CreateProvider();
            var watcher = provider.GetRequiredService<PluginFolderWatcher>();
            var registry = provider.GetRequiredService<RuntimePluginRegistry>();

            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Runtime.Added.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusOperations>Orders.Accept</ModusOperations></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);
            var snapshot = registry.GetSnapshot();

            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.True(onboarding.PluginActivated);
            Assert.Equal(new PluginId("Plugin.Runtime.Added"), onboarding.PluginId);
            Assert.Contains(snapshot.Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Added", StringComparison.Ordinal));
            Assert.Contains(
                snapshot.Catalogs,
                x => string.Equals((x as IPluginContract)?.PluginId.Value, "Plugin.Runtime.Added", StringComparison.Ordinal)
                    && x.SupportedOperations.Contains(new OperationName("Orders.Accept")));
            Assert.Contains(
                onboarding.Diagnostics,
                x => x.Contains($"outcome=accepted path={Path.GetFullPath(projectPath)}", StringComparison.Ordinal));
            Assert.Contains(
                onboarding.Diagnostics,
                x => x.Contains("stage=registry-update outcome=success", StringComparison.Ordinal)
                    && x.Contains("added=Plugin.Runtime.Added", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Implement plugin unload path with deterministic disposal and registry eviction [depends on runtime registry abstraction]")]
    public void PluginFolderWatcher_GivenPluginUnloadEvent_ExpectedRegistryEvictedAndResourcesDisposed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-runtime-remove-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            using var provider = CreateProvider();
            var watcher = provider.GetRequiredService<PluginFolderWatcher>();
            var registry = provider.GetRequiredService<RuntimePluginRegistry>();

            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Runtime.Removed.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);
            Assert.True(onboarding.PluginActivated);
            Assert.Contains(registry.GetSnapshot().Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Removed", StringComparison.Ordinal));

            File.Delete(projectPath);
            var offboarding = watcher.OnProjectDeleted(projectPath);
            var snapshot = registry.GetSnapshot();

            Assert.True(offboarding.HostHealthy);
            Assert.True(offboarding.EventAccepted);
            Assert.Equal(new PluginId("Plugin.Runtime.Removed"), offboarding.PluginId);
            Assert.DoesNotContain(snapshot.Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Removed", StringComparison.Ordinal));
            Assert.DoesNotContain(snapshot.Catalogs, x => string.Equals((x as IPluginContract)?.PluginId.Value, "Plugin.Runtime.Removed", StringComparison.Ordinal));
            Assert.Contains(
                offboarding.Diagnostics,
                x => x.Contains("stage=unload", StringComparison.Ordinal)
                    && x.Contains("plugin=Plugin.Runtime.Removed", StringComparison.Ordinal)
                    && x.Contains("outcome=success", StringComparison.Ordinal));
            Assert.Contains(
                offboarding.Diagnostics,
                x => x.Contains("stage=registry-update outcome=success", StringComparison.Ordinal)
                    && x.Contains("removed=Plugin.Runtime.Removed", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PluginHostingOptions());
        services.AddModusPluginHostingRuntime();
        return services.BuildServiceProvider();
    }
}