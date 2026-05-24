using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Events;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Hosting;
using Modus.SamplePlugins.Lifetime;
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
        var pluginSourceDir = ResolvePluginOutputDirectory(repoRoot);
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

    private static string ResolvePluginOutputDirectory(string repoRoot)
    {
        var debugPath = Path.Combine(repoRoot, "plugins", "bin", "Debug", "net10.0");
        if (Directory.Exists(debugPath))
        {
            return debugPath;
        }

        var releasePath = Path.Combine(repoRoot, "plugins", "bin", "Release", "net10.0");
        if (Directory.Exists(releasePath))
        {
            return releasePath;
        }

        throw new DirectoryNotFoundException(
            $"Could not find plugin binaries. Checked '{debugPath}' and '{releasePath}'.");
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
                var operationCatalogs = provider.GetServices<IPluginOperationCatalog>().OfType<IPluginContract>().ToArray();
                var eventSubscribers = provider.GetServices<IEventSubscriber>().OfType<IPluginContract>().ToArray();
                var lifecycles = provider.GetServices<IPluginLifecycle>().OfType<IPluginContract>().ToArray();
                var scheduledEvents = provider.GetServices<IPluginScheduledEvents>().OfType<IPluginContract>().ToArray();

                Assert.NotEmpty(operationCatalogs);
                Assert.NotEmpty(eventSubscribers);
                Assert.NotEmpty(lifecycles);
                Assert.NotEmpty(scheduledEvents);

                // Typed-only migration: canonical object alias responder should not be required as a direct DI service.
                Assert.Null(provider.GetService<ISyncResponder>());
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
                var plugin = provider.GetRequiredService<HostTelemetryPlugin>();
                var supportedOp = Assert.Single(plugin.SupportedOperations);

                var request = new SyncRequest(
                    Operation: supportedOp,
                    IsFallbackExplicit: false,
                    CorrelationId: new CorrelationId(Guid.NewGuid().ToString()));
                var response = plugin.Handle(request);

                Assert.True(response.Success);
                Assert.NotNull(response.Payload);

                var envelope = Assert.IsType<TelemetryOperationPayload>(response.Payload);
                Assert.Null(envelope.Error);
                Assert.NotNull(envelope.Result);
                var telemetryPayload = envelope.Result!;
                Assert.Equal("Plugin.Host.Telemetry", telemetryPayload.PluginId);
                Assert.Equal("Telemetry.Host.CollectSnapshot", telemetryPayload.Operation);
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
                var subscriber1 = provider.GetServices<IEventSubscriber>().OfType<HostTelemetryPlugin>().Single();
                var subscriber2 = provider.GetServices<IEventSubscriber>().OfType<HostTelemetryPlugin>().Single();
                var lifecycle1 = provider.GetServices<IPluginLifecycle>().OfType<HostTelemetryPlugin>().Single();
                var lifecycle2 = provider.GetServices<IPluginLifecycle>().OfType<HostTelemetryPlugin>().Single();

                Assert.Same(plugin1, plugin2);
                Assert.Same(catalog1, catalog2);
                Assert.Same(subscriber1, subscriber2);
                Assert.Same(lifecycle1, lifecycle2);
                Assert.Same(plugin1, catalog1);
                Assert.Same(plugin1, subscriber1);
                Assert.Same(plugin1, lifecycle1);

                var response1 = plugin1.Handle(SyncRequest.ForStandardPath(new OperationName("Telemetry.Host.CollectSnapshot"), correlationId: new CorrelationId("singleton-corr-1")));
                var response2 = plugin2.Handle(SyncRequest.ForStandardPath(new OperationName("Telemetry.Host.CollectSnapshot"), correlationId: new CorrelationId("singleton-corr-2")));
                Assert.True(response1.Success);
                Assert.True(response2.Success);
                Assert.IsType<TelemetryOperationPayload>(response1.Payload);
                Assert.IsType<TelemetryOperationPayload>(response2.Payload);
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
                    var plugin1a = scope1.ServiceProvider.GetRequiredService<ScopedLifetimePlugin>();
                    var plugin1b = scope1.ServiceProvider.GetRequiredService<ScopedLifetimePlugin>();
                    var scopedService1a = scope1.ServiceProvider.GetRequiredService<ScopedTestService>();
                    var scopedService1b = scope1.ServiceProvider.GetRequiredService<ScopedTestService>();

                    Assert.Same(plugin1a, plugin1b);
                    Assert.Same(scopedService1a, scopedService1b);
                    scopedInstanceIds.Add(scopedService1a.InstanceId);

                    var response = plugin1a.Handle(SyncRequest.ForStandardPath(new OperationName("Lifetime.Scoped.PrintId"), correlationId: new CorrelationId("scope-1")));
                    Assert.True(response.Success);
                    Assert.NotNull(response.Payload);
                    var scope1Result = response.Payload.GetType().GetProperty("Result")?.GetValue(response.Payload);
                    var scope1Lifetime = scope1Result?.GetType().GetProperty("Lifetime")?.GetValue(scope1Result) as string;
                    Assert.Equal("Scoped", scope1Lifetime);
                }

                // Assert: Across different scopes, scoped services should return different instances
                using (var scope2 = provider.CreateScope())
                {
                    var plugin2 = scope2.ServiceProvider.GetRequiredService<ScopedLifetimePlugin>();
                    var scopedService2 = scope2.ServiceProvider.GetRequiredService<ScopedTestService>();

                    scopedInstanceIds.Add(scopedService2.InstanceId);

                    var response = plugin2.Handle(SyncRequest.ForStandardPath(new OperationName("Lifetime.Scoped.PrintId"), correlationId: new CorrelationId("scope-2")));
                    Assert.True(response.Success);
                    Assert.NotNull(response.Payload);
                    var scope2Result = response.Payload.GetType().GetProperty("Result")?.GetValue(response.Payload);
                    var scope2Lifetime = scope2Result?.GetType().GetProperty("Lifetime")?.GetValue(scope2Result) as string;
                    Assert.Equal("Scoped", scope2Lifetime);
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
                {
                    using var scope = provider.CreateScope();
                    return scope.ServiceProvider
                        .GetRequiredService<TransientRuntimePlugin>()
                        .InstanceId;
                }))
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

    [Fact]
    [Trait("ChecklistItem", "di-registration-lambda")]
    public async Task HostRunnerStartAsync_GivenDiscoveredScheduledPluginFromDi_ExpectedScheduledExecutionResolvesThroughLifecycleHostProvider()
    {
        var tempDir = CopyPluginsToTemporaryDirectory();

        try
        {
            var services = new ServiceCollection();
            var pluginsPath = Path.Combine(tempDir, "plugins");

            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            using var provider = services.BuildServiceProvider(validateScopes: true);
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(CancellationToken.None);

            Assert.Contains(
                result.Diagnostics,
                d => d.Contains("stage=lifecycle plugin=Plugin.Host.Telemetry outcome=started", StringComparison.Ordinal));
            Assert.Contains(
                result.Diagnostics,
                d => d.Contains("stage=operation plugin=Plugin.Host.Telemetry", StringComparison.Ordinal)
                    && d.Contains("operation=Telemetry.Host.CollectSnapshot", StringComparison.Ordinal)
                    && d.Contains("outcome=success", StringComparison.Ordinal));
            Assert.DoesNotContain(
                result.Diagnostics,
                d => d.Contains("stage=operation plugin=Plugin.Host.Telemetry", StringComparison.Ordinal)
                    && d.Contains("reason=unresolvable-via-di", StringComparison.Ordinal));
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _ = TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "non-breaking-scheduling-contract")]
    public void StartActivatedPlugins_GivenDiResolvedScheduledPluginWithoutParameterlessConstructor_ExpectedSchedulingDiagnosticsStayStableWhileExecutionUsesDiResolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton(
            new ConstructorInjectedScheduleDefinition(
            [
                new ConstructorInjectedScheduleRegistration("Telemetry.Host.CollectSnapshot.EveryFiveMinutes", TimeSpan.FromMinutes(5), "Telemetry.Host.CollectSnapshot", "payload-five"),
                new ConstructorInjectedScheduleRegistration("Telemetry.Host.CollectSnapshot.EveryMinute", TimeSpan.FromMinutes(1), "Telemetry.Host.CollectSnapshot.Fast", "payload-one")
            ]));
        services.AddTransient<ConstructorInjectedScheduledPlugin>();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(ConstructorInjectedScheduledPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ConstructorInjectedScheduled"),
            "Plugin.Tests.ConstructorInjectedScheduled",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.ScheduledContract")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ConstructorInjectedScheduled"]);

        Assert.Contains(
            "stage=lifecycle plugin=Plugin.Tests.ConstructorInjectedScheduled outcome=started source=Plugin.Tests.ConstructorInjectedScheduled",
            diagnostics,
            StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                "stage=scheduling plugin=Plugin.Tests.ConstructorInjectedScheduled job=Telemetry.Host.CollectSnapshot.EveryFiveMinutes intervalMs=300000 operation=Telemetry.Host.CollectSnapshot outcome=registered",
                "stage=scheduling plugin=Plugin.Tests.ConstructorInjectedScheduled job=Telemetry.Host.CollectSnapshot.EveryMinute intervalMs=60000 operation=Telemetry.Host.CollectSnapshot.Fast outcome=registered"
            }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            diagnostics
                .Where(d => d.StartsWith("stage=scheduling plugin=Plugin.Tests.ConstructorInjectedScheduled", StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(
            new[]
            {
                "stage=operation plugin=Plugin.Tests.ConstructorInjectedScheduled operation=Telemetry.Host.CollectSnapshot source=scheduled job=Telemetry.Host.CollectSnapshot.EveryFiveMinutes outcome=success payload=payload-five",
                "stage=operation plugin=Plugin.Tests.ConstructorInjectedScheduled operation=Telemetry.Host.CollectSnapshot.Fast source=scheduled job=Telemetry.Host.CollectSnapshot.EveryMinute outcome=success payload=payload-one"
            }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            diagnostics
                .Where(d => d.StartsWith("stage=operation plugin=Plugin.Tests.ConstructorInjectedScheduled", StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    [Trait("ChecklistItem", "remove-manual-lifetime-control")]
    public void StartActivatedPlugins_GivenServiceProviderWithoutPluginTypeRegistration_ExpectedActivationDoesNotManuallyInstantiatePlugin()
    {
        ScheduledInstanceTrackingPlugin.Reset();
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(ScheduledInstanceTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ScheduledInstanceTracking"),
            "Plugin.Tests.ScheduledInstanceTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.InstanceTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ScheduledInstanceTracking"]);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Contains("stage=lifecycle plugin=Plugin.Tests.ScheduledInstanceTracking", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            d => d.Contains("stage=scheduling plugin=Plugin.Tests.ScheduledInstanceTracking", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "deterministic-fallback")]
    public void StartActivatedPlugins_GivenServiceProviderWithoutPluginTypeRegistration_ExpectedScheduledOperationProducesDeterministicFallbackDiagnostic()
    {
        // Arrange: activation uses the provider-less fallback to register schedules, but
        // scheduled execution still must not construct the runtime instance outside DI.
        ScheduledInstanceTrackingPlugin.Reset();

        var host = new AssemblyLifecycleHost();
        var assemblyPath = typeof(ScheduledInstanceTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ScheduledInstanceTracking"),
            "Plugin.Tests.ScheduledInstanceTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.InstanceTracking")],
            [],
            AssemblyPath: assemblyPath);

        // Act
        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ScheduledInstanceTracking"]);

        // Assert: schedule registration still happens from the activated instance, but execution
        // is ignored because the runtime plugin type is not resolved through DI.
        Assert.Contains(
            diagnostics,
            d => d.Contains("stage=scheduling plugin=Plugin.Tests.ScheduledInstanceTracking", StringComparison.Ordinal)
                && d.Contains("operation=InstanceTracking.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=registered", StringComparison.Ordinal));
        var ignoredDiagnostic = Assert.Single(
            diagnostics,
            d => d.Contains("operation=InstanceTracking.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=ignored", StringComparison.Ordinal));
        Assert.Contains("reason=unresolvable-via-di", ignoredDiagnostic, StringComparison.Ordinal);
        Assert.Contains(
            $"lifecycleType={typeof(ScheduledInstanceTrackingPlugin).FullName}",
            ignoredDiagnostic,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "remove-manual-lifetime-control")]
    public void StartActivatedPlugins_GivenNoServiceProvider_ExpectedActivationFallsBackToParameterlessConstruction()
    {
        ScheduledInstanceTrackingPlugin.Reset();

        var host = new AssemblyLifecycleHost();
        var assemblyPath = typeof(ScheduledInstanceTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ScheduledInstanceTracking"),
            "Plugin.Tests.ScheduledInstanceTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.InstanceTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ScheduledInstanceTracking"]);

        Assert.Contains(
            diagnostics,
            d => d.Contains("stage=lifecycle plugin=Plugin.Tests.ScheduledInstanceTracking outcome=started", StringComparison.Ordinal));
        Assert.Contains(
            diagnostics,
            d => d.Contains("stage=scheduling plugin=Plugin.Tests.ScheduledInstanceTracking", StringComparison.Ordinal)
                && d.Contains("outcome=registered", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "scope-per-tick")]
    public void StartActivatedPlugins_GivenMultipleScheduledExecutions_ExpectedNewScopeCreatedPerExecution()
    {
        ScopedExecutionTrackingPlugin.Reset();
        using var scopeFactory = new CountingScopeFactory();
        using var provider = new CountingRootProvider(scopeFactory);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(ScopedExecutionTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ScopedExecutionTracking"),
            "Plugin.Tests.ScopedExecutionTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.ScopeTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ScopedExecutionTracking"]);

        var executionDiagnostics = diagnostics
            .Where(d => d.Contains("operation=ScopedExecution.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();
        var executionScopes = executionDiagnostics
            .Select(diagnostic =>
            {
                var scopeId = ExtractTokenValue(diagnostic, "scopeId");
                var activationScopeId = ExtractTokenValue(diagnostic, "activationScopeId");
                return (scopeId, activationScopeId);
            })
            .ToArray();

        Assert.Equal(2, executionDiagnostics.Length);
        Assert.Equal(
            executionScopes.Length,
            executionScopes
                .Select(x => x.scopeId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            executionScopes,
            execution => Assert.NotEqual(execution.activationScopeId, execution.scopeId));
        Assert.True(scopeFactory.CreateScopeCallCount >= 3);
    }

    [Fact]
    [Trait("ChecklistItem", "singleton-container-owned")]
    public void StartActivatedPlugins_GivenSingletonScheduledPluginResolvedFromScope_ExpectedContainerOwnedSingletonReusedAcrossExecutions()
    {
        SingletonExecutionTrackingPlugin.Reset();
        using var scopeFactory = new SingletonTrackingScopeFactory();
        using var provider = new SingletonTrackingRootProvider(scopeFactory);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(SingletonExecutionTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.SingletonExecutionTracking"),
            "Plugin.Tests.SingletonExecutionTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.SingletonTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.SingletonExecutionTracking"]);

        var executionDiagnostics = diagnostics
            .Where(d => d.Contains("operation=SingletonExecution.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();
        var executionInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "instanceId"))
            .ToArray();
        var activationInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "activationInstanceId"))
            .ToArray();

        Assert.Equal(2, executionDiagnostics.Length);
        Assert.Single(executionInstanceIds.Distinct(StringComparer.Ordinal));
        Assert.All(
            activationInstanceIds,
            activationInstanceId => Assert.Equal(executionInstanceIds[0], activationInstanceId));
        Assert.Equal(0, provider.PluginResolutionCount);
        Assert.True(scopeFactory.CreateScopeCallCount >= 3);
    }

    [Fact]
    [Trait("ChecklistItem", "integration-transient-fresh")]
    public void StartActivatedPlugins_GivenTransientScheduledPluginResolvedFromDi_ExpectedFreshInstanceEachRun()
    {
        TransientExecutionTrackingPlugin.Reset();

        var services = new ServiceCollection();
        services.AddTransient<TransientExecutionTrackingPlugin>();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(TransientExecutionTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.TransientExecutionTracking"),
            "Plugin.Tests.TransientExecutionTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.TransientTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.TransientExecutionTracking"]);

        var executionDiagnostics = diagnostics
            .Where(d => d.Contains("operation=TransientExecution.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();
        var executionInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "instanceId"))
            .ToArray();
        var activationInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "activationInstanceId"))
            .ToArray();

        Assert.Equal(2, executionDiagnostics.Length);
        Assert.Equal(
            executionInstanceIds.Length,
            executionInstanceIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(activationInstanceIds.Distinct(StringComparer.Ordinal));
        Assert.DoesNotContain(activationInstanceIds[0], executionInstanceIds, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "integration-singleton-stable")]
    public void StartActivatedPlugins_GivenSingletonScheduledPluginResolvedFromDi_ExpectedSameInstanceAcrossRuns()
    {
        SingletonExecutionTrackingPlugin.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<SingletonExecutionTrackingPlugin>();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(SingletonExecutionTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.SingletonExecutionTracking"),
            "Plugin.Tests.SingletonExecutionTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.SingletonTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.SingletonExecutionTracking"]);

        var executionDiagnostics = diagnostics
            .Where(d => d.Contains("operation=SingletonExecution.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();
        var executionInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "instanceId"))
            .ToArray();
        var activationInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "activationInstanceId"))
            .ToArray();

        Assert.Equal(2, executionDiagnostics.Length);
        Assert.Single(executionInstanceIds.Distinct(StringComparer.Ordinal));
        Assert.Single(activationInstanceIds.Distinct(StringComparer.Ordinal));
        Assert.Equal(executionInstanceIds[0], activationInstanceIds[0]);
    }

    [Fact]
    [Trait("ChecklistItem", "integration-scoped-per-scope")]
    public void StartActivatedPlugins_GivenScopedScheduledPluginResolvedFromDi_ExpectedDifferentInstancePerSchedulerScope()
    {
        ScopedExecutionTrackingPlugin.Reset();

        var services = new ServiceCollection();
        services.AddScoped<ScopedExecutionTrackingPlugin>();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(ScopedExecutionTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ScopedExecutionTracking"),
            "Plugin.Tests.ScopedExecutionTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.ScopeTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ScopedExecutionTracking"]);

        var executionDiagnostics = diagnostics
            .Where(d => d.Contains("operation=ScopedExecution.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();
        var executionInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "instanceId"))
            .ToArray();
        var activationInstanceIds = executionDiagnostics
            .Select(diagnostic => ExtractTokenValue(diagnostic, "activationInstanceId"))
            .ToArray();

        Assert.Equal(2, executionDiagnostics.Length);
        Assert.Equal(
            executionInstanceIds.Length,
            executionInstanceIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Single(activationInstanceIds.Distinct(StringComparer.Ordinal));
        Assert.DoesNotContain(activationInstanceIds[0], executionInstanceIds, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "diagnostics-determinism")]
    public void ScheduledExecutionDiagnostics_GivenResolutionFailure_ExpectedSingleDeterministicFailureMessage()
    {
        ScheduledInstanceTrackingPlugin.Reset();

        var host = new AssemblyLifecycleHost();
        var assemblyPath = typeof(ScheduledInstanceTrackingPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ScheduledInstanceTracking"),
            "Plugin.Tests.ScheduledInstanceTracking",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.InstanceTracking")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ScheduledInstanceTracking"]);

        var failureDiagnostics = diagnostics
            .Where(d => d.Contains("operation=InstanceTracking.Execute", StringComparison.Ordinal)
                && d.Contains("outcome=ignored", StringComparison.Ordinal))
            .ToArray();

        var failure = Assert.Single(failureDiagnostics);
        Assert.Equal("operation", ExtractTokenValue(failure, "stage"));
        Assert.Equal("Plugin.Tests.ScheduledInstanceTracking", ExtractTokenValue(failure, "plugin"));
        Assert.Equal("InstanceTracking.Execute", ExtractTokenValue(failure, "operation"));
        Assert.Equal("scheduled", ExtractTokenValue(failure, "source"));
        Assert.Equal("InstanceTracking.Once", ExtractTokenValue(failure, "job"));
        Assert.Equal("ignored", ExtractTokenValue(failure, "outcome"));
        Assert.Equal("unresolvable-via-di", ExtractTokenValue(failure, "reason"));
        Assert.Equal(typeof(ScheduledInstanceTrackingPlugin).FullName, ExtractTokenValue(failure, "lifecycleType"));
    }

    [Fact]
    [Trait("ChecklistItem", "diagnostics-determinism")]
    public void ScheduledExecutionDiagnostics_GivenSuccess_ExpectedIncludesJobAndOperationMetadata()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ConstructorInjectedScheduleDefinition(
        [
            new ConstructorInjectedScheduleRegistration("Payments.SyncLedger.EveryHour", TimeSpan.FromHours(1), "Payments.SyncLedger", "sync-ok")
        ]));
        services.AddTransient<ConstructorInjectedScheduledPlugin>();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var host = new AssemblyLifecycleHost(provider);
        var assemblyPath = typeof(ConstructorInjectedScheduledPlugin).Assembly.Location;
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.Tests.ConstructorInjectedScheduled"),
            "Plugin.Tests.ConstructorInjectedScheduled",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.ScheduledContract")],
            [],
            AssemblyPath: assemblyPath);

        var diagnostics = host.StartActivatedPlugins([descriptor], ["Plugin.Tests.ConstructorInjectedScheduled"]);

        var successDiagnostics = diagnostics
            .Where(d => d.Contains("operation=Payments.SyncLedger", StringComparison.Ordinal)
                && d.Contains("outcome=success", StringComparison.Ordinal))
            .ToArray();

        var success = Assert.Single(successDiagnostics);
        Assert.Equal("operation", ExtractTokenValue(success, "stage"));
        Assert.Equal("Plugin.Tests.ConstructorInjectedScheduled", ExtractTokenValue(success, "plugin"));
        Assert.Equal("Payments.SyncLedger", ExtractTokenValue(success, "operation"));
        Assert.Equal("scheduled", ExtractTokenValue(success, "source"));
        Assert.Equal("Payments.SyncLedger.EveryHour", ExtractTokenValue(success, "job"));
        Assert.Equal("success", ExtractTokenValue(success, "outcome"));
        Assert.Equal("sync-ok", ExtractTokenValue(success, "payload"));
    }

    private static string ExtractTokenValue(string diagnostic, string tokenName)
    {
        var marker = $"{tokenName}=";
        var markerIndex = diagnostic.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Diagnostic did not contain '{marker}': {diagnostic}");

        markerIndex += marker.Length;
        var endIndex = diagnostic.IndexOf(' ', markerIndex);
        return endIndex >= 0
            ? diagnostic[markerIndex..endIndex]
            : diagnostic[markerIndex..];
    }

    public sealed class ScheduledInstanceTrackingPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginScheduledEvents,
        ISyncResponder
    {
        private static int s_instanceCounter;
        private static volatile int s_activationInstanceId;
        private readonly int _instanceId = Interlocked.Increment(ref s_instanceCounter);

        public static void Reset()
        {
            s_instanceCounter = 0;
            s_activationInstanceId = 0;
        }

        public PluginId PluginId => new("Plugin.Tests.ScheduledInstanceTracking");
        public ContractName ContractName => new("Modus.PluginContract");
        public Version ContractVersion => new(1, 0, 0);

        public void Load(PluginLoadContext context) { }
        public void Start(PluginStartContext context) { }
        public void Stop(PluginStopContext context) { }
        public void Unload(PluginUnloadContext context) { }

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            s_activationInstanceId = _instanceId;
            scheduler.ScheduleAt(
                new JobName("InstanceTracking.Once"),
                DateTimeOffset.UtcNow,
                new OperationName("InstanceTracking.Execute"));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            var isActivated = _instanceId == s_activationInstanceId;
            return new SyncResponse(
                Success: true,
                Payload: $"instanceId={_instanceId} isActivated={isActivated}",
                CorrelationId: request.CorrelationId);
        }
    }

    public sealed record ConstructorInjectedScheduleRegistration(string JobName, TimeSpan Interval, string Operation, string Payload);

    public sealed class ConstructorInjectedScheduleDefinition(IReadOnlyList<ConstructorInjectedScheduleRegistration> schedules)
    {
        public IReadOnlyList<ConstructorInjectedScheduleRegistration> Schedules { get; } = schedules;
    }

    public sealed class ConstructorInjectedScheduledPlugin(ConstructorInjectedScheduleDefinition definition) :
        IPluginContract,
        IPluginLifecycle,
        IPluginScheduledEvents,
        ISyncResponder
    {
        public PluginId PluginId => new("Plugin.Tests.ConstructorInjectedScheduled");

        public ContractName ContractName => new("Modus.PluginContract");

        public Version ContractVersion => new(1, 0, 0);

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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            foreach (var schedule in definition.Schedules)
            {
                scheduler.ScheduleRecurring(new JobName(schedule.JobName), schedule.Interval, new OperationName(schedule.Operation));
            }
        }

        public SyncResponse Handle(SyncRequest request)
        {
            var match = definition.Schedules.Single(x => x.Operation == request.Operation.Value);
            return new SyncResponse(Success: true, Payload: match.Payload, CorrelationId: request.CorrelationId);
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

    public sealed class ScopedExecutionTrackingPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginScheduledEvents,
        ISyncResponder
    {
        private static int s_activationScopeId;
        private static string s_activationInstanceId = string.Empty;
        private readonly int _scopeId;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public ScopedExecutionTrackingPlugin()
            : this(-1)
        {
        }

        public ScopedExecutionTrackingPlugin(int scopeId)
        {
            _scopeId = scopeId;
        }

        public static void Reset()
        {
            s_activationScopeId = 0;
            s_activationInstanceId = string.Empty;
        }

        public PluginId PluginId => new("Plugin.Tests.ScopedExecutionTracking");

        public ContractName ContractName => new("Modus.PluginContract");

        public Version ContractVersion => new(1, 0, 0);

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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            s_activationScopeId = _scopeId;
            s_activationInstanceId = _instanceId;
            scheduler.ScheduleAt(
                new JobName("ScopedExecution.Once.1"),
                DateTimeOffset.UtcNow,
                new OperationName("ScopedExecution.Execute"));
            scheduler.ScheduleAt(
                new JobName("ScopedExecution.Once.2"),
                DateTimeOffset.UtcNow.AddMilliseconds(1),
                new OperationName("ScopedExecution.Execute"));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: $"instanceId={_instanceId} activationInstanceId={s_activationInstanceId} scopeId={_scopeId} activationScopeId={s_activationScopeId}",
                CorrelationId: request.CorrelationId);
        }
    }

    public sealed class SingletonExecutionTrackingPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginScheduledEvents,
        ISyncResponder
    {
        private static string s_activationInstanceId = string.Empty;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public static void Reset()
        {
            s_activationInstanceId = string.Empty;
        }

        public PluginId PluginId => new("Plugin.Tests.SingletonExecutionTracking");

        public ContractName ContractName => new("Modus.PluginContract");

        public Version ContractVersion => new(1, 0, 0);

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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            s_activationInstanceId = _instanceId;
            scheduler.ScheduleAt(
                new JobName("SingletonExecution.Once.1"),
                DateTimeOffset.UtcNow,
                new OperationName("SingletonExecution.Execute"));
            scheduler.ScheduleAt(
                new JobName("SingletonExecution.Once.2"),
                DateTimeOffset.UtcNow.AddMilliseconds(1),
                new OperationName("SingletonExecution.Execute"));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: $"instanceId={_instanceId} activationInstanceId={s_activationInstanceId}",
                CorrelationId: request.CorrelationId);
        }
    }

    public sealed class TransientExecutionTrackingPlugin :
        IPluginContract,
        IPluginLifecycle,
        IPluginScheduledEvents,
        ISyncResponder
    {
        private static string s_activationInstanceId = string.Empty;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public static void Reset()
        {
            s_activationInstanceId = string.Empty;
        }

        public PluginId PluginId => new("Plugin.Tests.TransientExecutionTracking");

        public ContractName ContractName => new("Modus.PluginContract");

        public Version ContractVersion => new(1, 0, 0);

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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
            s_activationInstanceId = _instanceId;
            scheduler.ScheduleAt(
                new JobName("TransientExecution.Once.1"),
                DateTimeOffset.UtcNow,
                new OperationName("TransientExecution.Execute"));
            scheduler.ScheduleAt(
                new JobName("TransientExecution.Once.2"),
                DateTimeOffset.UtcNow.AddMilliseconds(1),
                new OperationName("TransientExecution.Execute"));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: $"instanceId={_instanceId} activationInstanceId={s_activationInstanceId}",
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class CountingRootProvider(CountingScopeFactory scopeFactory) : IServiceProvider, IDisposable
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return scopeFactory;
            }

            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory, IDisposable
    {
        private int _nextScopeId;

        public int CreateScopeCallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCallCount++;
            return new TrackingScope(Interlocked.Increment(ref _nextScopeId));
        }

        public void Dispose()
        {
        }

        private sealed class TrackingScope(int scopeId) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new ScopeServiceProvider(scopeId);

            public void Dispose()
            {
            }
        }

        private sealed class ScopeServiceProvider(int scopeId) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(ScopedExecutionTrackingPlugin))
                {
                    return new ScopedExecutionTrackingPlugin(scopeId);
                }

                return null;
            }
        }
    }

    private sealed class SingletonTrackingRootProvider(SingletonTrackingScopeFactory scopeFactory) : IServiceProvider, IDisposable
    {
        public int PluginResolutionCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return scopeFactory;
            }

            if (serviceType == typeof(SingletonExecutionTrackingPlugin))
            {
                PluginResolutionCount++;
                return scopeFactory.SingletonInstance;
            }

            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class SingletonTrackingScopeFactory : IServiceScopeFactory, IDisposable
    {
        public SingletonExecutionTrackingPlugin SingletonInstance { get; } = new();

        public int CreateScopeCallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCallCount++;
            return new TrackingScope(SingletonInstance);
        }

        public void Dispose()
        {
        }

        private sealed class TrackingScope(SingletonExecutionTrackingPlugin singletonInstance) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new ScopeServiceProvider(singletonInstance);

            public void Dispose()
            {
            }
        }

        private sealed class ScopeServiceProvider(SingletonExecutionTrackingPlugin singletonInstance) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(SingletonExecutionTrackingPlugin))
                {
                    return singletonInstance;
                }

                return null;
            }
        }
    }
}

