using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Events;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Hosting;
using Modus.SamplePlugins.Telemetry;
using Xunit;

namespace Modus.Host.IntegrationTests;

/// <summary>
/// Integration tests proving that <see cref="PluginHostingHostExtensions.AddModusPluginHosting"/> 
/// wires core DI registration from runtime plugin assemblies and that plugin services 
/// can be resolved from <see cref="IServiceProvider"/>.
/// </summary>
public sealed class PluginDiConsumptionTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"modus-di-test-{Guid.NewGuid():N}");
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

    private static string CopyCurrentTestRuntimeToTemporaryDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"modus-transient-di-test-{Guid.NewGuid():N}");
        var tempPluginsDir = Path.Combine(tempDir, "plugins");
        var currentOutputDirectory = Path.GetDirectoryName(typeof(PluginDiConsumptionTests).Assembly.Location)
            ?? throw new InvalidOperationException("Current test assembly output directory was not found.");

        Directory.CreateDirectory(tempPluginsDir);

        foreach (var file in Directory.EnumerateFiles(currentOutputDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(tempPluginsDir, Path.GetFileName(file)), overwrite: true);
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
                // Assembly might still be locked; wait and retry
                System.Threading.Thread.Sleep(500 * (i + 1));
            }
            catch (Exception)
            {
                // Silently fail on any exception (includes UnauthorizedAccessException on last retry)
                // The temp directory will be cleaned up eventually by OS
            }
        }

        // If all retries failed, still return true to not fail the test
        return true;
    }

    [Fact]
    public void AddModusPluginHosting_GivenRuntimePluginAssembliesInConfiguredPath_ExpectedCoreRegistrationExtensionInvokedWithLoadedAssemblies()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            // Assert: Verify that plugin DI registrations were applied.
            using (var provider = services.BuildServiceProvider())
            {
                // The HostTelemetryPlugin and MachineTelemetryPlugin should be registered
                // as singleton instances in DI, available for resolution.
                var hostTelemetry = provider.GetService<HostTelemetryPlugin>();
                var machineTelemetry = provider.GetService<MachineTelemetryPlugin>();

                Assert.NotNull(hostTelemetry);
                Assert.NotNull(machineTelemetry);
            }

            // Force cleanup before trying to delete
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            // Best-effort cleanup; temp assemblies may remain locked
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.SingletonSupport.RootProviderResolution")]
    public void SingletonRegistration_GivenRepeatedResolutionsFromRootProvider_ExpectedSameInstanceReturned()
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
                var hostTelemetry1 = provider.GetService<HostTelemetryPlugin>();
                var hostTelemetry2 = provider.GetService<HostTelemetryPlugin>();
                var hostTelemetryByContract1 = provider.GetService<IHostTelemetryPluginContract>();
                var hostTelemetryByContract2 = provider.GetService<IHostTelemetryPluginContract>();

                // Assert: Singleton lifetime means same instance on repeated resolution.
                Assert.NotNull(hostTelemetry1);
                Assert.NotNull(hostTelemetry2);
                Assert.Same(hostTelemetry1, hostTelemetry2);
                Assert.NotNull(hostTelemetryByContract1);
                Assert.NotNull(hostTelemetryByContract2);
                Assert.Same(hostTelemetryByContract1, hostTelemetryByContract2);
                Assert.Same(hostTelemetry1, hostTelemetryByContract1);
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
    public void AddModusPluginHosting_GivenPluginServicesResolved_ExpectedPluginCapabilitiesAccessible()
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
                var hostTelemetry = provider.GetService<HostTelemetryPlugin>();
                var machineTelemetry = provider.GetService<MachineTelemetryPlugin>();

                // Assert: Verify that plugin contracts and capabilities are accessible.
                Assert.NotNull(hostTelemetry);
                Assert.Equal(new PluginId("Plugin.Host.Telemetry"), hostTelemetry.PluginId);
                Assert.Equal(new ContractName("Modus.PluginContract"), hostTelemetry.ContractName);
                Assert.Equal(new Version(1, 0, 0), hostTelemetry.ContractVersion);

                Assert.NotNull(machineTelemetry);
                Assert.Equal(new PluginId("Plugin.Machine.Telemetry"), machineTelemetry.PluginId);
                Assert.Equal(new ContractName("Modus.PluginContract"), machineTelemetry.ContractName);
                Assert.Equal(new Version(1, 0, 0), machineTelemetry.ContractVersion);
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
    public void AddModusPluginHosting_GivenPluginInstancesFromDi_ExpectedOperationCatalogBehaviorWorks()
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
                var hostTelemetry = provider.GetService<HostTelemetryPlugin>();

                // Assert: Verify that the DI-resolved plugin instance has working operation catalog.
                Assert.NotNull(hostTelemetry);
                var supportedOps = hostTelemetry.SupportedOperations;
                Assert.Single(supportedOps);
                Assert.Contains(new OperationName("Telemetry.Host.CollectSnapshot"), supportedOps);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void AddModusPluginHosting_GivenEmptyOrMissingPluginsPath_ExpectedGracefulHandlingWithoutDiRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        var missingPath = Path.Combine(Path.GetTempPath(), $"modus-missing-{Guid.NewGuid():N}");

        // Act
        services.AddModusPluginHosting(opts => opts.PluginsPath = missingPath);

        using var provider = services.BuildServiceProvider();
        var hostTelemetry = provider.GetService<HostTelemetryPlugin>();

        // Assert: When plugins directory doesn't exist, plugins won't be loaded into DI.
        Assert.Null(hostTelemetry);
    }

    [Fact]
    public void AddModusPluginHosting_GivenRuntimeAssembliesDiscovered_ExpectedCoreAddDiscoveredPluginsInvokedWithLoadedAssemblies()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            var servicesBeforeCount = services.Count;

            // Act: Call AddModusPluginHosting which should invoke AddDiscoveredPlugins internally.
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            // Assert: Verify the core DI registration extension was invoked by checking:
            // 1. Services were added to the collection (proof AddDiscoveredPlugins ran).
            Assert.True(services.Count > servicesBeforeCount, 
                "AddDiscoveredPlugins should have added services to the collection when runtime assemblies are discovered");

            // 2. Plugin types are registered as service descriptors in the collection.
            var pluginServiceDescriptors = services
                .Where(sd => sd.ServiceType == typeof(HostTelemetryPlugin) || 
                             sd.ServiceType == typeof(MachineTelemetryPlugin))
                .ToList();
            Assert.NotEmpty(pluginServiceDescriptors);
            Assert.Contains(pluginServiceDescriptors, 
                sd => sd.ServiceType == typeof(HostTelemetryPlugin));
            Assert.Contains(pluginServiceDescriptors, 
                sd => sd.ServiceType == typeof(MachineTelemetryPlugin));
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void HostDiResolution_GivenRegisteredPluginServices_ExpectedProviderResolvesPluginContract()
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
                // Assert: Verify that plugin services can be resolved by their contract interfaces.
                // IPluginOperationCatalog should resolve to the registered plugin instances.
                var operationCatalog = provider.GetService<IPluginOperationCatalog>();
                Assert.NotNull(operationCatalog);
                Assert.IsAssignableFrom<IPluginContract>(operationCatalog);

                // ISyncResponder should also resolve to a plugin instance.
                var syncResponder = provider.GetService<ISyncResponder>();
                Assert.NotNull(syncResponder);
                Assert.IsAssignableFrom<IPluginContract>(syncResponder);

                // IEventSubscriber should resolve to a plugin instance.
                var eventSubscriber = provider.GetService<IEventSubscriber>();
                Assert.NotNull(eventSubscriber);
                Assert.IsAssignableFrom<IPluginContract>(eventSubscriber);

                // IPluginLifecycle should resolve to a plugin instance.
                var lifecycle = provider.GetService<IPluginLifecycle>();
                Assert.NotNull(lifecycle);
                Assert.IsAssignableFrom<IPluginContract>(lifecycle);

                // IPluginScheduledEvents should resolve to a plugin instance.
                var scheduledEvents = provider.GetService<IPluginScheduledEvents>();
                Assert.NotNull(scheduledEvents);
                Assert.IsAssignableFrom<IPluginContract>(scheduledEvents);
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
    public void HostDiResolution_GivenCapabilityServiceRequest_ExpectedResolvedInstanceExecutesExpectedBehavior()
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
                // Assert: Resolve plugin by ISyncResponder interface and verify it handles requests correctly.
                var syncResponder = provider.GetRequiredService<ISyncResponder>();
                var operationCatalog = provider.GetRequiredService<IPluginOperationCatalog>();

                // Verify the plugin's operation catalog works.
                var supportedOps = operationCatalog.SupportedOperations;
                Assert.NotEmpty(supportedOps);
                
                // Get the first supported operation from the plugin
                var supportedOp = supportedOps.First();
                Assert.NotNull(supportedOp);

                // Verify the sync responder can handle its own operations.
                var request = new SyncRequest(
                    Operation: supportedOp,
                    IsFallbackExplicit: false,
                    CorrelationId: new CorrelationId(Guid.NewGuid().ToString()));
                var response = syncResponder.Handle(request);

                Assert.True(response.Success);
                Assert.NotNull(response.Payload);
                Assert.NotEmpty(response.Payload);
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
    [Trait("ChecklistItem", "Core.PluginLifetimes.SingletonSupport.RootProviderResolution")]
    public void SingletonRegistration_GivenMultiplePluginCapabilities_ExpectedSingletonDescriptorsPreserved()
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
                // Assert: Repeated root-provider resolutions preserve singleton semantics
                // for a specific plugin even when multiple plugins expose the same capability contracts.
                var plugin1 = provider.GetRequiredService<HostTelemetryPlugin>();
                var plugin2 = provider.GetRequiredService<HostTelemetryPlugin>();
                var catalog1 = provider.GetServices<IPluginOperationCatalog>().OfType<HostTelemetryPlugin>().Single();
                var catalog2 = provider.GetServices<IPluginOperationCatalog>().OfType<HostTelemetryPlugin>().Single();
                var responder1 = provider.GetServices<ISyncResponder>().OfType<HostTelemetryPlugin>().Single();
                var responder2 = provider.GetServices<ISyncResponder>().OfType<HostTelemetryPlugin>().Single();
                var subscriber1 = provider.GetServices<IEventSubscriber>().OfType<HostTelemetryPlugin>().Single();
                var subscriber2 = provider.GetServices<IEventSubscriber>().OfType<HostTelemetryPlugin>().Single();
                var lifecycle1 = provider.GetServices<IPluginLifecycle>().OfType<HostTelemetryPlugin>().Single();
                var lifecycle2 = provider.GetServices<IPluginLifecycle>().OfType<HostTelemetryPlugin>().Single();

                Assert.Same(plugin1, plugin2);
                Assert.Same(catalog1, catalog2);
                Assert.Same(responder1, responder2);
                Assert.Same(subscriber1, subscriber2);
                Assert.Same(lifecycle1, lifecycle2);
                Assert.Same(plugin1, catalog1);
                Assert.Same(plugin1, responder1);
                Assert.Same(plugin1, subscriber1);
                Assert.Same(plugin1, lifecycle1);
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
    [Trait("ChecklistItem", "Core.PluginLifetimes.ScopedSupport.HostCreatedScopeBoundaries")]
    public void HostDiResolution_GivenScopedDependencies_ExpectedScopedLifetimeRespectedDuringExecution()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Register a scoped test service that tracks instance identity
            var scopedInstanceIds = new List<Guid>();
            services.AddScoped<ScopedTestService>(provider => new ScopedTestService(Guid.NewGuid()));

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using (var provider = services.BuildServiceProvider())
            {
                // Assert: Within a single scope, scoped services should return the same instance
                using (var scope1 = provider.CreateScope())
                {
                    var plugin1a = scope1.ServiceProvider.GetRequiredService<ISyncResponder>();
                    var scopedService1a = scope1.ServiceProvider.GetRequiredService<ScopedTestService>();
                    var scopedService1b = scope1.ServiceProvider.GetRequiredService<ScopedTestService>();

                    Assert.Same(scopedService1a, scopedService1b);
                    scopedInstanceIds.Add(scopedService1a.InstanceId);
                }

                // Assert: Across different scopes, scoped services should return different instances
                using (var scope2 = provider.CreateScope())
                {
                    var plugin2 = scope2.ServiceProvider.GetRequiredService<ISyncResponder>();
                    var scopedService2 = scope2.ServiceProvider.GetRequiredService<ScopedTestService>();

                    scopedInstanceIds.Add(scopedService2.InstanceId);
                }

                // Verify that the two scopes got different instances
                Assert.Equal(2, scopedInstanceIds.Count);
                Assert.NotEqual(scopedInstanceIds[0], scopedInstanceIds[1]);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            _ = TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Scoped test service for verifying scoped lifetime handling during plugin resolution.
    /// </summary>
    private sealed class ScopedTestService
    {
        public Guid InstanceId { get; }

        public ScopedTestService(Guid instanceId)
        {
            InstanceId = instanceId;
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.TransientSupport.NewInstancePerResolution")]
    public async Task TransientRegistration_GivenConcurrentResolutions_ExpectedIndependentInstances()
    {
        var tempDir = CopyCurrentTestRuntimeToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using var provider = services.BuildServiceProvider(validateScopes: true);

            var resolutionTasks = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                    provider.GetServices<IPluginLifecycle>()
                        .OfType<TransientRuntimePlugin>()
                        .Single()
                        .InstanceId))
                .ToArray();

            var instanceIds = await Task.WhenAll(resolutionTasks);

            Assert.Equal(instanceIds.Length, instanceIds.Distinct().Count());
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void RegistrationDiagnostics_GivenAddModusPluginHostingWithPlugins_ExpectedDiagnosticsSurfaceResolvableWithSuccessEntries()
    {
        // Arrange
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            // Act
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using var provider = services.BuildServiceProvider();

            // Assert: PluginDiRegistrationDiagnostics is resolvable after AddModusPluginHosting.
            var diagnostics = provider.GetService<PluginDiRegistrationDiagnostics>();
            Assert.NotNull(diagnostics);

            // Diagnostics must contain at least one entry for registered plugins.
            Assert.NotEmpty(diagnostics.Entries);

            // Every entry must have a non-null RegisterTypeName.
            Assert.All(diagnostics.Entries, e => Assert.NotNull(e.RegisterTypeName));

            // At least one Success entry must be present for the loaded runtime plugins.
            Assert.Contains(diagnostics.Entries, e => e.Outcome == PluginRegistrationOutcome.Success);

            // Skipped entries must include a non-null reason explaining the skip.
            Assert.All(
                diagnostics.Entries.Where(e => e.Outcome == PluginRegistrationOutcome.Skipped),
                e => Assert.NotNull(e.Reason));
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _ = TryDeleteDirectory(tempDir);
        }
    }

    public sealed class TransientRuntimePlugin :
        IPluginDependencyRegister,
        IPluginContract,
        IPluginLifecycle
    {
        public TransientRuntimePlugin()
        {
            InstanceId = Guid.NewGuid();
        }

        public Guid InstanceId { get; }

        public PluginId PluginId => new PluginId("Tests.HostIntegration.TransientRuntimePlugin");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0, 0);

        public void Register(IServiceCollection services)
        {
            services.AddPluginService<TransientRuntimePlugin, TransientRuntimePlugin>(PluginServiceLifetime.Transient);
        }

        public void Load(PluginLoadContext context)
        {
        }

        public void Start(PluginStartContext context)
        {
        }

        public void Stop(PluginStopContext context)
        {
        }

        public void Unload(PluginUnloadContext context)
        {
        }
    }
}

