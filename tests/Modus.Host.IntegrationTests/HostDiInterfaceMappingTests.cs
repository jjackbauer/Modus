using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Plugins;
using Modus.Host.Domain.Telemetry;
using Modus.Host.Hosting;
using Modus.SamplePlugins.Telemetry;
using Xunit;

namespace Modus.Host.IntegrationTests;

/// <summary>
/// Integration tests proving that host DI composition correctly resolves plugin interface contracts
/// alongside concrete plugin types, preserving expected lifetime semantics.
/// </summary>
public sealed class HostDiInterfaceMappingTests
{
    private static string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Modus.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string CopyPluginsToTemporaryDirectory()
    {
        var repoRoot = FindRepositoryRoot();
        var pluginSourceDir = Path.Combine(repoRoot, "plugins", "bin", "Debug", "net10.0");
        var tempDir = Path.Combine(Path.GetTempPath(), $"modus-di-interface-test-{Guid.NewGuid():N}");
        var tempPluginsDir = Path.Combine(tempDir, "plugins");

        Directory.CreateDirectory(tempPluginsDir);

        // Copy plugin DLLs and their dependencies
        foreach (var file in Directory.EnumerateFiles(pluginSourceDir, "Plugin*.dll"))
        {
            File.Copy(file, Path.Combine(tempPluginsDir, Path.GetFileName(file)), overwrite: true);
        }

        // Copy Modus.Core.dll (required by plugins)
        var coreDll = Path.Combine(pluginSourceDir, "Modus.Core.dll");
        if (File.Exists(coreDll))
        {
            File.Copy(coreDll, Path.Combine(tempPluginsDir, "Modus.Core.dll"), overwrite: true);
        }

        return tempDir;
    }

    private static bool TryDeleteDirectory(string path, int retries = 3)
    {
        for (var i = 0; i < retries; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return true;
            }
            catch (UnauthorizedAccessException) when (i < retries - 1)
            {
                System.Threading.Thread.Sleep(500 * (i + 1));
            }
            catch (Exception)
            {
                // Silently fail on cleanup
            }
        }

        return true;
    }

    [Fact]
    [Trait("ChecklistItem", "Add integration coverage for host DI")]
    public void HostDiResolution_GivenPluginWithCustomContractMapping_ExpectedConcreteAndInterfaceResolveToExpectedLifetimeBehavior()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act: Add Modus hosting which will load and register plugins
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using (var provider = services.BuildServiceProvider())
            {
                // Assert: Verify that both concrete and interface resolutions work
                var hostTelemetryByType = provider.GetRequiredService<HostTelemetryPlugin>();
                var hostTelemetryByInterface = provider.GetRequiredService<IHostTelemetryPluginContract>();

                Assert.NotNull(hostTelemetryByType);
                Assert.NotNull(hostTelemetryByInterface);

                // For Singleton plugins, both should resolve to the same instance
                Assert.Same(hostTelemetryByType, hostTelemetryByInterface);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Add integration coverage for host DI")]
    public void HostDiResolution_GivenCustomContractInvocation_ExpectedResolvedInterfaceExecutesPluginBehavior()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using (var provider = services.BuildServiceProvider())
            {
                // Assert: Verify that interface-resolved plugin exhibits expected behavior
                var hostTelemetry = provider.GetRequiredService<IHostTelemetryPluginContract>();

                Assert.NotNull(hostTelemetry);
                Assert.IsAssignableFrom<IPluginContract>(hostTelemetry);
                Assert.Equal(new PluginId("Plugin.Host.Telemetry"), hostTelemetry.PluginId);
                Assert.Equal(new ContractName("Modus.PluginContract"), hostTelemetry.ContractName);
                Assert.Equal(new Version(1, 0, 0), hostTelemetry.ContractVersion);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Add integration coverage for host DI")]
    public void HostDiResolution_GivenMultiplePluginInterfaceContracts_ExpectedAllContractsResolvableThroughDi()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using (var provider = services.BuildServiceProvider())
            {
                // Assert: Verify all plugins are resolvable by both concrete type and interface
                var hostTelemetryByType = provider.GetService<HostTelemetryPlugin>();
                var hostTelemetryByInterface = provider.GetService<IHostTelemetryPluginContract>();

                var machineTelemetryByType = provider.GetService<MachineTelemetryPlugin>();
                var machineTelemetryByInterface = provider.GetService<IMachineTelemetryPluginContract>();

                Assert.NotNull(hostTelemetryByType);
                Assert.NotNull(hostTelemetryByInterface);
                Assert.Same(hostTelemetryByType, hostTelemetryByInterface);

                Assert.NotNull(machineTelemetryByType);
                Assert.NotNull(machineTelemetryByInterface);
                Assert.Same(machineTelemetryByType, machineTelemetryByInterface);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Add integration coverage for host DI")]
    public void HostDiResolution_GivenSingletonPluginInterfaceMapping_ExpectedRepeatedInterfaceResolutionReturnsSameInstance()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using (var provider = services.BuildServiceProvider())
            {
                // Assert: Verify singleton lifetime is preserved for interface mapping
                var hostTelemetry1 = provider.GetRequiredService<IHostTelemetryPluginContract>();
                var hostTelemetry2 = provider.GetRequiredService<IHostTelemetryPluginContract>();
                var hostTelemetry3 = provider.GetRequiredService<IHostTelemetryPluginContract>();

                Assert.Same(hostTelemetry1, hostTelemetry2);
                Assert.Same(hostTelemetry2, hostTelemetry3);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Add integration coverage for host DI")]
    public void HostDiResolution_GivenInterfaceMappingResolution_ExpectedOperationCatalogBehaviorWorks()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using (var provider = services.BuildServiceProvider())
            {
                // Assert: Verify that interface-resolved plugin instance can be cast to IPluginOperationCatalog
                var hostTelemetry = provider.GetRequiredService<IHostTelemetryPluginContract>();

                Assert.NotNull(hostTelemetry);
                
                // IHostTelemetryPluginContract extends IPluginContract, but to access operation catalog
                // we need to cast to IPluginOperationCatalog
                var operationCatalog = hostTelemetry as IPluginOperationCatalog;
                Assert.NotNull(operationCatalog);
                
                var supportedOps = operationCatalog.SupportedOperations;
                Assert.Single(supportedOps);
                Assert.Contains(new OperationName("Telemetry.Host.CollectSnapshot"), supportedOps);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Register telemetry provider abstractions and aggregation service in host DI with deterministic composition, without adding a new contract layer")]
    public void RegisterTelemetryAggregationServices_GivenHostStartup_ResolvesAllTelemetryProviders()
    {
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using var provider = services.BuildServiceProvider();

            var aggregation = provider.GetRequiredService<TelemetryAggregationService>();

            var hostProvider = Assert.Single(aggregation.HostProviders);
            Assert.Equal(new PluginId("Plugin.Host.Telemetry"), hostProvider.PluginId);

            var machineProvider = Assert.Single(aggregation.MachineProviders);
            Assert.Equal(new PluginId("Plugin.Machine.Telemetry"), machineProvider.PluginId);
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Register telemetry provider abstractions and aggregation service in host DI with deterministic composition, without adding a new contract layer")]
    public void RegisterTelemetryAggregationServices_GivenDuplicateProviderRegistration_PreservesDeterministicOrdering()
    {
        var services = new ServiceCollection();
        services.AddPluginHostingCore();

        services.AddSingleton<IHostTelemetryPluginContract>(new TestHostTelemetryProvider("Plugin.Host.Telemetry.Zeta"));
        services.AddSingleton<IHostTelemetryPluginContract>(new TestHostTelemetryProvider("Plugin.Host.Telemetry.Alpha"));
        services.AddModusPluginHostingRuntime();

        using var provider = services.BuildServiceProvider();

        var aggregation = provider.GetRequiredService<TelemetryAggregationService>();
        var orderedPluginIds = aggregation.HostProviders.Select(static plugin => plugin.PluginId.Value).ToArray();

        Assert.Equal(
            ["Plugin.Host.Telemetry.Alpha", "Plugin.Host.Telemetry.Zeta"],
            orderedPluginIds);
    }

    private sealed class TestHostTelemetryProvider(string pluginId) : IHostTelemetryPluginContract
    {
        public PluginId PluginId { get; } = new PluginId(pluginId);

        public ContractName ContractName { get; } = new ContractName("Modus.PluginContract");

        public Version ContractVersion { get; } = new(1, 0, 0);
    }
}
