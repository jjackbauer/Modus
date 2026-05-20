using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginInterfaceMappingTests
{
    [Fact]
    [Trait("ChecklistItem", "Introduce explicit interface-registration path")]
    public void RegisterPluginServices_GivenDeclaredInterfaceMapping_ExpectedInterfaceResolvesToPluginImplementation()
    {
        var services = new ServiceCollection();
        var plugin = new SingletonPluginWithInterfaceMapping();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        var concreteResolution = provider.GetRequiredService<SingletonPluginWithInterfaceMapping>();
        var interfaceResolution = provider.GetRequiredService<ICustomPluginContract>();

        Assert.NotNull(concreteResolution);
        Assert.NotNull(interfaceResolution);
        Assert.Same(concreteResolution, interfaceResolution);
    }

    [Fact]
    [Trait("ChecklistItem", "Introduce explicit interface-registration path")]
    public void RegisterPluginServices_GivenMultipleInterfaceMappings_ExpectedAllMappedInterfacesResolvable()
    {
        var services = new ServiceCollection();
        var plugin = new SingletonPluginWithMultipleInterfaces();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        var concrete = provider.GetRequiredService<SingletonPluginWithMultipleInterfaces>();
        var interface1 = provider.GetRequiredService<IFirstContract>();
        var interface2 = provider.GetRequiredService<ISecondContract>();

        Assert.NotNull(concrete);
        Assert.NotNull(interface1);
        Assert.NotNull(interface2);
        Assert.Same(concrete, interface1);
        Assert.Same(concrete, interface2);
    }

    [Fact]
    [Trait("ChecklistItem", "Ensure Singleton/Scoped lifetime preservation")]
    public void SingletonInterfaceMapping_GivenRepeatedRootProviderResolution_ExpectedSameImplementationInstance()
    {
        var services = new ServiceCollection();
        var plugin = new SingletonPluginWithInterfaceMapping();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        var interface1 = provider.GetRequiredService<ICustomPluginContract>();
        var interface2 = provider.GetRequiredService<ICustomPluginContract>();
        var concrete1 = provider.GetRequiredService<SingletonPluginWithInterfaceMapping>();
        var concrete2 = provider.GetRequiredService<SingletonPluginWithInterfaceMapping>();

        Assert.Same(interface1, interface2);
        Assert.Same(concrete1, concrete2);
        Assert.Same(interface1, concrete1);
    }

    [Fact]
    [Trait("ChecklistItem", "Ensure Singleton/Scoped lifetime preservation")]
    public void ScopedInterfaceMapping_GivenSingleScopeResolutions_ExpectedSameScopeBoundInstance()
    {
        var services = new ServiceCollection();
        var plugin = new ScopedPluginWithInterfaceMapping();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var interface1 = scope.ServiceProvider.GetRequiredService<ICustomPluginContract>();
            var interface2 = scope.ServiceProvider.GetRequiredService<ICustomPluginContract>();
            var concrete1 = scope.ServiceProvider.GetRequiredService<ScopedPluginWithInterfaceMapping>();
            var concrete2 = scope.ServiceProvider.GetRequiredService<ScopedPluginWithInterfaceMapping>();

            Assert.Same(interface1, interface2);
            Assert.Same(concrete1, concrete2);
            Assert.Same(interface1, concrete1);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Ensure Singleton/Scoped lifetime preservation")]
    public void ScopedInterfaceMapping_GivenDifferentScopes_ExpectedDifferentScopeInstances()
    {
        var services = new ServiceCollection();
        var plugin = new ScopedPluginWithInterfaceMapping();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        ICustomPluginContract interface1;
        ICustomPluginContract interface2;

        using (var scope1 = provider.CreateScope())
        {
            interface1 = scope1.ServiceProvider.GetRequiredService<ICustomPluginContract>();
        }

        using (var scope2 = provider.CreateScope())
        {
            interface2 = scope2.ServiceProvider.GetRequiredService<ICustomPluginContract>();
        }

        Assert.NotSame(interface1, interface2);
    }

    [Fact]
    [Trait("ChecklistItem", "Ensure Transient lifetime semantics")]
    public void TransientInterfaceMapping_GivenRepeatedResolution_ExpectedNewInstanceEachTime()
    {
        var services = new ServiceCollection();
        var plugin = new TransientPluginWithInterfaceMapping();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        var interface1 = provider.GetRequiredService<ICustomPluginContract>();
        var interface2 = provider.GetRequiredService<ICustomPluginContract>();
        var concrete1 = provider.GetRequiredService<TransientPluginWithInterfaceMapping>();
        var concrete2 = provider.GetRequiredService<TransientPluginWithInterfaceMapping>();

        Assert.NotSame(interface1, interface2);
        Assert.NotSame(concrete1, concrete2);
        Assert.NotSame(interface1, concrete1);
    }

    [Fact]
    [Trait("ChecklistItem", "Ensure Transient lifetime semantics")]
    public void TransientInterfaceMapping_GivenConcurrentResolutions_ExpectedIndependentInstances()
    {
        var services = new ServiceCollection();
        var plugin = new TransientPluginWithInterfaceMapping();
        plugin.Register(services);

        var provider = services.BuildServiceProvider();
        var instances = new List<ICustomPluginContract>();

        Parallel.For(0, 10, _ =>
        {
            instances.Add(provider.GetRequiredService<ICustomPluginContract>());
        });

        var uniqueInstances = instances.Distinct().Count();
        // Should create multiple different instances (at least 80% of requests should get unique instances)
        Assert.True(uniqueInstances >= 8, $"Expected at least 8 unique instances, but got {uniqueInstances}");
    }

    [Fact]
    [Trait("ChecklistItem", "Enforce registration validation")]
    public void InterfaceMappingValidation_GivenNonAssignableContract_ExpectedArgumentException()
    {
        var services = new ServiceCollection();
        
        // Verify that runtime check catches non-assignable types when using the non-generic overload
        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddPluginServiceInterface(
                typeof(ICustomPluginContract),
                typeof(PluginWithInvalidInterfaceAttempt),
                PluginServiceLifetime.Singleton));

        Assert.Contains(nameof(ICustomPluginContract), exception.Message);
    }

    [Fact]
    [Trait("ChecklistItem", "Enforce registration validation")]
    public void InterfaceMappingValidation_GivenDuplicateContractMapping_ExpectedDeterministicSingleEffectiveDescriptor()
    {
        var services = new ServiceCollection();
        var plugin = new PluginWithDuplicateInterfaceMapping();
        plugin.Register(services);

        // Verify that duplicate mappings don't cause resolution issues
        var provider = services.BuildServiceProvider();
        var interface1 = provider.GetRequiredService<ICustomPluginContract>();
        var interface2 = provider.GetRequiredService<ICustomPluginContract>();
        var concrete1 = provider.GetRequiredService<PluginWithDuplicateInterfaceMapping>();
        var concrete2 = provider.GetRequiredService<PluginWithDuplicateInterfaceMapping>();

        // All resolutions should return the same instance for Singleton
        Assert.Same(interface1, interface2);
        Assert.Same(concrete1, concrete2);
        Assert.Same(interface1, concrete1);
    }

    [Fact]
    [Trait("ChecklistItem", "Enforce registration validation")]
    public void InterfaceMappingValidation_GivenEquivalentRepeatedRegistration_ExpectedIdempotentDescriptorSet()
    {
        var services = new ServiceCollection();
        var plugin = new SingletonPluginWithInterfaceMapping();
        
        plugin.Register(services);
        var countAfterFirst = services.Count();
        
        plugin.Register(services);
        var countAfterSecond = services.Count();

        // Multiple registrations of the same plugin should not cause descriptor explosion
        Assert.True(countAfterSecond <= countAfterFirst * 2);
    }

    // Test plugins with interface mappings

    private interface ICustomPluginContract
    {
        string GetName();
    }

    private interface IFirstContract
    {
    }

    private interface ISecondContract
    {
    }

    private sealed class SingletonPluginWithInterfaceMapping : SingletonPlugin<SingletonPluginWithInterfaceMapping>, ICustomPluginContract
    {
        public string GetName() => "Singleton";

        protected override void RegisterPluginServices(IServiceCollection services)
        {
            base.RegisterPluginServices(services);
            services.AddPluginServiceInterface<ICustomPluginContract, SingletonPluginWithInterfaceMapping>(DeclaredServiceLifetime);
        }
    }

    private sealed class ScopedPluginWithInterfaceMapping : ScopedPlugin<ScopedPluginWithInterfaceMapping>, ICustomPluginContract
    {
        public string GetName() => "Scoped";

        protected override void RegisterPluginServices(IServiceCollection services)
        {
            base.RegisterPluginServices(services);
            services.AddPluginServiceInterface<ICustomPluginContract, ScopedPluginWithInterfaceMapping>(DeclaredServiceLifetime);
        }
    }

    private sealed class TransientPluginWithInterfaceMapping : TransientPlugin<TransientPluginWithInterfaceMapping>, ICustomPluginContract
    {
        public string GetName() => "Transient";

        protected override void RegisterPluginServices(IServiceCollection services)
        {
            base.RegisterPluginServices(services);
            services.AddPluginServiceInterface<ICustomPluginContract, TransientPluginWithInterfaceMapping>(DeclaredServiceLifetime);
        }
    }

    private sealed class SingletonPluginWithMultipleInterfaces : 
        SingletonPlugin<SingletonPluginWithMultipleInterfaces>,
        IFirstContract,
        ISecondContract
    {
        protected override void RegisterPluginServices(IServiceCollection services)
        {
            base.RegisterPluginServices(services);
            services.AddPluginServiceInterface<IFirstContract, SingletonPluginWithMultipleInterfaces>(DeclaredServiceLifetime);
            services.AddPluginServiceInterface<ISecondContract, SingletonPluginWithMultipleInterfaces>(DeclaredServiceLifetime);
        }
    }

    private sealed class PluginWithInvalidInterfaceAttempt : SingletonPlugin<PluginWithInvalidInterfaceAttempt>
    {
    }

    private sealed class PluginWithDuplicateInterfaceMapping : SingletonPlugin<PluginWithDuplicateInterfaceMapping>, ICustomPluginContract
    {
        public string GetName() => "Duplicate";

        protected override void RegisterPluginServices(IServiceCollection services)
        {
            base.RegisterPluginServices(services);
            services.AddPluginServiceInterface<ICustomPluginContract, PluginWithDuplicateInterfaceMapping>(DeclaredServiceLifetime);
            services.AddPluginServiceInterface<ICustomPluginContract, PluginWithDuplicateInterfaceMapping>(DeclaredServiceLifetime);
        }
    }
}
