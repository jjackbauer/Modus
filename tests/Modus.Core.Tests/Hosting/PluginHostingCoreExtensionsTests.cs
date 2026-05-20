using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Events;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Hosting;

public sealed class PluginHostingCoreExtensionsTests
{
    [Fact]
    public void AddDiscoveredPlugins_GivenNullServices_ExpectedArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PluginHostingCoreExtensions.AddDiscoveredPlugins(null!, [typeof(DiscoverablePluginRegister).Assembly]));
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginsRefactor.ExtensionsPartition.MoveDIRegistrationHelpers")]
    public void AddDiscoveredPlugins_GivenRealAssemblyWithRegistrar_ExpectedDescriptorsRegistered()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExamplePluginService));

        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.Equal(typeof(ExamplePluginService), registration.ImplementationType);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.SingletonSupport.RootProviderResolution")]
    public void SingletonRegistration_GivenRepeatedResolutionsFromRootProvider_ExpectedSameInstanceReturned()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var first = provider.GetRequiredService<IExamplePluginService>();
        var second = provider.GetRequiredService<IExamplePluginService>();

        Assert.Same(first, second);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.IdempotentReRegistration.SameAssemblies")]
    public void Idempotency_GivenAddDiscoveredPluginsCalledTwice_ExpectedNoDuplicateDescriptors()
    {
        var services = new ServiceCollection();
        var pluginAssembly = typeof(DiscoverablePluginRegister).Assembly;

        services.AddDiscoveredPlugins([pluginAssembly]);

        var firstSnapshot = services.Select(DescribeDescriptor).ToArray();

        services.AddDiscoveredPlugins([pluginAssembly]);

        var secondSnapshot = services.Select(DescribeDescriptor).ToArray();

        Assert.Equal(firstSnapshot, secondSnapshot);
    }

    [Fact]
    public void PluginTypeDiscovery_GivenAbstractAndOpenGenericTypes_ExpectedTypesIgnored()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAbstractIgnoredService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IOpenGenericIgnoredService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConcreteDiscoveredService));
    }

    [Fact]
    public void PluginTypeDiscovery_GivenTypeWithoutIPluginContract_ExpectedTypeExcluded()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(INonContractRegisterService));
    }

    [Fact]
    public void PluginTypeDiscovery_GivenMixedAssemblies_ExpectedDeterministicOrderedResult()
    {
        var first = new ServiceCollection();
        var second = new ServiceCollection();
        var testAssembly = typeof(DiscoverablePluginRegister).Assembly;
        var coreAssembly = typeof(IPluginContract).Assembly;

        first.AddDiscoveredPlugins([coreAssembly, testAssembly]);
        second.AddDiscoveredPlugins([testAssembly, coreAssembly]);

        var firstOrder = first
            .Where(descriptor => descriptor.ServiceType == typeof(IDeterministicOrderService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        var secondOrder = second
            .Where(descriptor => descriptor.ServiceType == typeof(IDeterministicOrderService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        var expectedOrder = new[]
        {
            typeof(DeterministicOrderAlpha),
            typeof(DeterministicOrderBeta),
            typeof(DeterministicOrderGamma)
        };

        Assert.Equal(expectedOrder, firstOrder);
        Assert.Equal(expectedOrder, secondOrder);
    }

    [Fact]
    public void ConflictResolution_GivenDuplicatePluginIds_ExpectedDeterministicSingleWinner()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDuplicatePluginIdentityWinnerService))
            .ToArray();

        var registration = Assert.Single(registrations);
        Assert.Equal(typeof(DuplicatePluginIdentityWinnerAlpha), registration.ImplementationType);
    }

    [Fact]
    public void ConflictResolution_GivenDuplicateCapabilities_ExpectedDeterministicSingleWinner()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDuplicateCapabilityService)
                && descriptor.ImplementationType == typeof(DuplicateCapabilityImplementation))
            .ToArray();

        var registration = Assert.Single(registrations);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.DeterministicMapping.EquivalentInputs")]
    public void ConflictResolution_GivenDifferentAssemblyInputOrder_ExpectedDeterministicWinners()
    {
        var first = new ServiceCollection();
        var second = new ServiceCollection();
        var testAssembly = typeof(DiscoverablePluginRegister).Assembly;
        var coreAssembly = typeof(IPluginContract).Assembly;

        first.AddDiscoveredPlugins([coreAssembly, testAssembly]);
        second.AddDiscoveredPlugins([testAssembly, coreAssembly]);

        var firstIdentityWinner = Assert.Single(first, descriptor => descriptor.ServiceType == typeof(IDuplicatePluginIdentityWinnerService));
        var secondIdentityWinner = Assert.Single(second, descriptor => descriptor.ServiceType == typeof(IDuplicatePluginIdentityWinnerService));
        Assert.Equal(typeof(DuplicatePluginIdentityWinnerAlpha), firstIdentityWinner.ImplementationType);
        Assert.Equal(firstIdentityWinner.ImplementationType, secondIdentityWinner.ImplementationType);

        var firstCapabilityWinner = Assert.Single(first, descriptor => descriptor.ServiceType == typeof(IDuplicateCapabilityService)
            && descriptor.ImplementationType == typeof(DuplicateCapabilityImplementation));
        var secondCapabilityWinner = Assert.Single(second, descriptor => descriptor.ServiceType == typeof(IDuplicateCapabilityService)
            && descriptor.ImplementationType == typeof(DuplicateCapabilityImplementation));
        Assert.Equal(ServiceLifetime.Singleton, firstCapabilityWinner.Lifetime);
        Assert.Equal(firstCapabilityWinner.Lifetime, secondCapabilityWinner.Lifetime);
    }

    [Fact]
    public void DescriptorPolicy_GivenPluginCapabilities_ExpectedConfiguredLifetimePerCapability()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var plugin = scope.ServiceProvider.GetRequiredService<CapabilityMappedPluginRegister>();
        var contract = scope.ServiceProvider.GetServices<IPluginContract>().OfType<CapabilityMappedPluginRegister>().Single();
        var registrar = scope.ServiceProvider.GetServices<IPluginDependencyRegister>().OfType<CapabilityMappedPluginRegister>().Single();
        var lifecycle = scope.ServiceProvider.GetServices<IPluginLifecycle>().OfType<CapabilityMappedPluginRegister>().Single();
        var operations = scope.ServiceProvider.GetServices<IPluginOperationCatalog>().OfType<CapabilityMappedPluginRegister>().Single();
        var schedules = scope.ServiceProvider.GetServices<IPluginScheduledEvents>().OfType<CapabilityMappedPluginRegister>().Single();
        var policy = scope.ServiceProvider.GetServices<IPluginRegistrationPolicy>().OfType<CapabilityMappedPluginRegister>().Single();
        var subscriber = scope.ServiceProvider.GetServices<IEventSubscriber>().OfType<CapabilityMappedPluginRegister>().Single();
        var responder = scope.ServiceProvider.GetServices<ISyncResponder>().OfType<CapabilityMappedPluginRegister>().Single();

        Assert.Same(plugin, contract);
        Assert.Same(plugin, registrar);
        Assert.Same(plugin, lifecycle);
        Assert.Same(plugin, operations);
        Assert.Same(plugin, schedules);
        Assert.Same(plugin, policy);
        Assert.Same(plugin, subscriber);
        Assert.Same(plugin, responder);
    }

    [Fact]
    public void DescriptorPolicy_GivenRepeatedPolicyUse_ExpectedNoDuplicateAdded()
    {
        var services = new ServiceCollection();
        var pluginAssembly = typeof(DiscoverablePluginRegister).Assembly;

        services.AddDiscoveredPlugins([pluginAssembly]);
        services.AddDiscoveredPlugins([pluginAssembly]);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginContract)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginLifecycle)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginOperationCatalog)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginScheduledEvents)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginRegistrationPolicy)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEventSubscriber)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISyncResponder)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && DescriptorTargetsPlugin(descriptor, typeof(CapabilityMappedPluginRegister)));
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.ScopedSupport.HostCreatedScopeBoundaries")]
    public void ScopedRegistration_GivenTwoDifferentServiceScopes_ExpectedDifferentInstancesAcrossScopes()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ScopedCapabilityMappedPluginRegister>();
        var second = secondScope.ServiceProvider.GetRequiredService<ScopedCapabilityMappedPluginRegister>();

        Assert.NotSame(first, second);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.ScopedSupport.HostCreatedScopeBoundaries")]
    public void ScopedRegistration_GivenRepeatedResolutionWithinSameScope_ExpectedSameInstanceReturned()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<ScopedCapabilityMappedPluginRegister>();
        var second = scope.ServiceProvider.GetRequiredService<ScopedCapabilityMappedPluginRegister>();

        Assert.Same(first, second);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.ScopedSupport.HostCreatedScopeBoundaries")]
    public void HostExecution_GivenScopedPluginCapability_ExpectedScopeCreatedAndDisposedPerExecution()
    {
        ScopedCapabilityMappedPluginRegister.ResetTracking();
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        ExecuteScopedPluginBoundary(provider);
        ExecuteScopedPluginBoundary(provider);

        Assert.Equal(2, ScopedCapabilityMappedPluginRegister.DisposedInstanceIds.Count);
        Assert.Equal(2, ScopedCapabilityMappedPluginRegister.DisposedInstanceIds.Distinct().Count());

        static void ExecuteScopedPluginBoundary(ServiceProvider root)
        {
            using var scope = root.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ScopedCapabilityMappedPluginRegister>();
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.TransientSupport.NewInstancePerResolution")]
    public void TransientRegistration_GivenRepeatedResolutions_ExpectedNewInstanceEachTime()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var firstConcrete = scope.ServiceProvider.GetRequiredService<TransientCapabilityMappedPluginRegister>();
        var secondConcrete = scope.ServiceProvider.GetRequiredService<TransientCapabilityMappedPluginRegister>();
        var firstCapability = scope.ServiceProvider.GetServices<IPluginLifecycle>()
            .OfType<TransientCapabilityMappedPluginRegister>()
            .Single();
        var secondCapability = scope.ServiceProvider.GetServices<IPluginLifecycle>()
            .OfType<TransientCapabilityMappedPluginRegister>()
            .Single();

        Assert.NotSame(firstConcrete, secondConcrete);
        Assert.NotEqual(firstConcrete.InstanceId, secondConcrete.InstanceId);
        Assert.NotSame(firstCapability, secondCapability);
        Assert.NotEqual(firstCapability.InstanceId, secondCapability.InstanceId);
    }

    [Fact]
    public void RegistrationDiagnostics_GivenSuccessfulPluginRegistration_ExpectedSuccessDiagnosticIncludesPluginIdentity()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        var diagnostics = ExtractDiagnostics(services);

        var entry = Assert.Single(diagnostics.Entries, e =>
            e.Outcome == PluginRegistrationOutcome.Success
            && e.PluginId == "Core.Tests.DiscoveredPlugin");

        Assert.Equal(typeof(DiscoverablePluginRegister).FullName, entry.RegisterTypeName);
        Assert.Equal(ServiceLifetime.Singleton, entry.SelectedLifetime);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void RegistrationDiagnostics_GivenDuplicatePluginIdentity_ExpectedSkippedDiagnosticWithConflictReason()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(DiscoverablePluginRegister).Assembly]);

        var diagnostics = ExtractDiagnostics(services);

        // DuplicatePluginIdentityRegisterAlpha wins (alphabetically first by type name);
        // DuplicatePluginIdentityRegisterBeta is skipped with a conflict reason.
        var skippedEntry = Assert.Single(diagnostics.Entries, e =>
            e.Outcome == PluginRegistrationOutcome.Skipped
            && e.RegisterTypeName == typeof(DuplicatePluginIdentityRegisterBeta).FullName);

        Assert.Equal("Core.Tests.DuplicateIdentity", skippedEntry.PluginId);
        Assert.Null(skippedEntry.SelectedLifetime);
        Assert.NotNull(skippedEntry.Reason);
        Assert.Contains("Core.Tests.DuplicateIdentity", skippedEntry.Reason);
    }

    [Fact]
    public void RegistrationDiagnostics_GivenRegistrarInstantiationFailure_ExpectedFailureDiagnosticWithReason()
    {
        var services = new ServiceCollection();

        services.AddDiscoveredPlugins([typeof(FailingConstructorPluginRegister).Assembly]);

        var diagnostics = ExtractDiagnostics(services);

        var failureEntry = Assert.Single(diagnostics.Entries, e =>
            e.Outcome == PluginRegistrationOutcome.Failure
            && e.RegisterTypeName == typeof(FailingConstructorPluginRegister).FullName);

        Assert.NotNull(failureEntry.Reason);
    }

    private static PluginDiRegistrationDiagnostics ExtractDiagnostics(IServiceCollection services)
        => services
            .Where(d => d.ServiceType == typeof(PluginDiRegistrationDiagnostics)
                        && d.ImplementationInstance is PluginDiRegistrationDiagnostics)
            .Select(d => (PluginDiRegistrationDiagnostics)d.ImplementationInstance!)
            .Single();

    private static bool DescriptorTargetsPlugin(ServiceDescriptor descriptor, Type pluginType)
    {
        if (descriptor.ImplementationType == pluginType)
        {
            return true;
        }

        return descriptor.ImplementationFactory?.Target?
            .GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(static field => field.FieldType == typeof(Type))
            .Select(field => field.GetValue(descriptor.ImplementationFactory.Target))
            .OfType<Type>()
            .Contains(pluginType) == true;
    }

    private static string DescribeDescriptor(ServiceDescriptor descriptor)
        => string.Join("|",
            descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name,
            descriptor.Lifetime.ToString(),
            descriptor.ImplementationType?.FullName
                ?? descriptor.ImplementationInstance?.GetType().FullName
                ?? descriptor.ImplementationFactory?.Target?.GetType().FullName
                ?? "<factory>");

    private interface IExamplePluginService
    {
    }

    private sealed class ExamplePluginService : IExamplePluginService
    {
    }

    private interface IAbstractIgnoredService
    {
    }

    private sealed class AbstractIgnoredService : IAbstractIgnoredService
    {
    }

    private interface IOpenGenericIgnoredService
    {
    }

    private sealed class OpenGenericIgnoredService : IOpenGenericIgnoredService
    {
    }

    private interface IConcreteDiscoveredService
    {
    }

    private sealed class ConcreteDiscoveredService : IConcreteDiscoveredService
    {
    }

    private interface INonContractRegisterService
    {
    }

    private sealed class NonContractRegisterService : INonContractRegisterService
    {
    }

    private interface IDeterministicOrderService
    {
    }

    private sealed class DeterministicOrderAlpha : IDeterministicOrderService
    {
    }

    private sealed class DeterministicOrderBeta : IDeterministicOrderService
    {
    }

    private sealed class DeterministicOrderGamma : IDeterministicOrderService
    {
    }

    private interface IDuplicatePluginIdentityWinnerService
    {
    }

    private sealed class DuplicatePluginIdentityWinnerAlpha : IDuplicatePluginIdentityWinnerService
    {
    }

    private sealed class DuplicatePluginIdentityWinnerBeta : IDuplicatePluginIdentityWinnerService
    {
    }

    private interface IDuplicateCapabilityService
    {
    }

    private sealed class DuplicateCapabilityImplementation : IDuplicateCapabilityService
    {
    }

    public sealed class DiscoverablePluginRegister : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DiscoveredPlugin");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IExamplePluginService, ExamplePluginService>();
        }
    }

    public abstract class AbstractPluginRegister : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.AbstractPluginRegister");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IAbstractIgnoredService, AbstractIgnoredService>();
        }
    }

    public sealed class OpenGenericPluginRegister<T> : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.OpenGenericPluginRegister");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IOpenGenericIgnoredService, OpenGenericIgnoredService>();
        }
    }

    public sealed class ConcretePluginRegister : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.ConcretePluginRegister");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IConcreteDiscoveredService, ConcreteDiscoveredService>();
        }
    }

    public sealed class RegisterWithoutPluginContract : IPluginDependencyRegister
    {
        public void Register(IServiceCollection services)
        {
            services.AddSingleton<INonContractRegisterService, NonContractRegisterService>();
        }
    }

    public sealed class DeterministicOrderRegisterAlpha : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DeterministicOrderRegisterAlpha");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IDeterministicOrderService, DeterministicOrderAlpha>();
        }
    }

    public sealed class DeterministicOrderRegisterBeta : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DeterministicOrderRegisterBeta");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IDeterministicOrderService, DeterministicOrderBeta>();
        }
    }

    public sealed class DeterministicOrderRegisterGamma : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DeterministicOrderRegisterGamma");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IDeterministicOrderService, DeterministicOrderGamma>();
        }
    }

    public sealed class DuplicatePluginIdentityRegisterAlpha : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DuplicateIdentity");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IDuplicatePluginIdentityWinnerService, DuplicatePluginIdentityWinnerAlpha>();
        }
    }

    public sealed class DuplicatePluginIdentityRegisterBeta : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DuplicateIdentity");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IDuplicatePluginIdentityWinnerService, DuplicatePluginIdentityWinnerBeta>();
        }
    }

    public sealed class DuplicateCapabilityRegisterAlpha : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DuplicateCapability.Alpha");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IDuplicateCapabilityService, DuplicateCapabilityImplementation>();
        }
    }

    public sealed class DuplicateCapabilityRegisterBeta : IPluginDependencyRegister, IPluginContract
    {
        public PluginId PluginId => new PluginId("Core.Tests.DuplicateCapability.Beta");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddScoped<IDuplicateCapabilityService, DuplicateCapabilityImplementation>();
        }
    }

    public sealed class CapabilityMappedPluginRegister :
        IPluginDependencyRegister,
        IPluginContract,
        IPluginLifecycle,
        IPluginOperationCatalog,
        IPluginScheduledEvents,
        IPluginRegistrationPolicy,
        IEventSubscriber,
        ISyncResponder
    {
        public PluginId PluginId => new PluginId("Core.Tests.CapabilityMappedPlugin");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("capability-mapped-op")];

        public void Register(IServiceCollection services)
        {
            services.AddPluginService<CapabilityMappedPluginRegister, CapabilityMappedPluginRegister>(PluginServiceLifetime.Singleton);
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

        public void RegisterSchedules(IPluginScheduler scheduler)
        {
        }

        public IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin)
            => [];

        public void Subscribe(IEventPublisher publisher)
        {
        }

        public SyncResponse Handle(SyncRequest request)
            => new(true, "ok");
    }

    public sealed class ScopedCapabilityMappedPluginRegister :
        IPluginDependencyRegister,
        IPluginContract,
        IPluginLifecycle,
        IDisposable
    {
        private static readonly List<Guid> s_disposedInstanceIds = [];

        public static IReadOnlyList<Guid> DisposedInstanceIds => s_disposedInstanceIds;

        public static void ResetTracking()
            => s_disposedInstanceIds.Clear();

        public ScopedCapabilityMappedPluginRegister()
        {
            InstanceId = Guid.NewGuid();
        }

        public Guid InstanceId { get; }

        public PluginId PluginId => new PluginId("Core.Tests.ScopedCapabilityMappedPlugin");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddPluginService<ScopedCapabilityMappedPluginRegister, ScopedCapabilityMappedPluginRegister>(PluginServiceLifetime.Scoped);
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

        public void Dispose()
        {
            s_disposedInstanceIds.Add(InstanceId);
        }
    }

    public sealed class TransientCapabilityMappedPluginRegister :
        IPluginDependencyRegister,
        IPluginContract,
        IPluginLifecycle
    {
        public TransientCapabilityMappedPluginRegister()
        {
            InstanceId = Guid.NewGuid();
        }

        public Guid InstanceId { get; }

        public PluginId PluginId => new PluginId("Core.Tests.TransientCapabilityMappedPlugin");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
            services.AddPluginService<TransientCapabilityMappedPluginRegister, TransientCapabilityMappedPluginRegister>(PluginServiceLifetime.Transient);
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

    public sealed class FailingConstructorPluginRegister : IPluginDependencyRegister, IPluginContract
    {
        public FailingConstructorPluginRegister()
            => throw new InvalidOperationException("Construction failed for test.");

        public PluginId PluginId => new PluginId("Core.Tests.FailingPlugin");

        public ContractName ContractName => new ContractName("Modus.PluginContract");

        public Version ContractVersion => new(1, 0);

        public void Register(IServiceCollection services)
        {
        }
    }
}
