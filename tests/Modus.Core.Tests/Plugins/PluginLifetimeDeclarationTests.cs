using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginLifetimeDeclarationTests
{
    [Theory]
    [InlineData(PluginServiceLifetime.Singleton, ServiceLifetime.Singleton)]
    [InlineData(PluginServiceLifetime.Scoped, ServiceLifetime.Scoped)]
    [InlineData(PluginServiceLifetime.Transient, ServiceLifetime.Transient)]
    [Trait("ChecklistItem", "Core.PluginLifetimes.DeterministicMapping.EquivalentInputs")]
    public void LifetimePolicy_GivenDeclaredLifetime_ExpectedDeterministicServiceLifetime(
        PluginServiceLifetime declaredLifetime,
        ServiceLifetime expectedLifetime)
    {
        var actualLifetime = PluginServiceLifetimeMapping.ToServiceLifetime(declaredLifetime);

        Assert.Equal(expectedLifetime, actualLifetime);
    }

    [Fact]
    public void LifetimeDeclaration_GivenSingletonSelection_ExpectedDescriptorIntentMarkedSingleton()
    {
        var services = new ServiceCollection();

        services.AddPluginService<ILifetimeProbeService, LifetimeProbeService>(PluginServiceLifetime.Singleton);

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(ILifetimeProbeService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(LifetimeProbeService), descriptor.ImplementationType);
    }

    [Fact]
    public void LifetimeDeclaration_GivenScopedSelection_ExpectedDescriptorIntentMarkedScoped()
    {
        var services = new ServiceCollection();

        services.AddPluginService<ILifetimeProbeService, LifetimeProbeService>(PluginServiceLifetime.Scoped);

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(ILifetimeProbeService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(LifetimeProbeService), descriptor.ImplementationType);
    }

    [Fact]
    public void LifetimeDeclaration_GivenTransientSelection_ExpectedDescriptorIntentMarkedTransient()
    {
        var services = new ServiceCollection();

        services.AddPluginService<ILifetimeProbeService, LifetimeProbeService>(PluginServiceLifetime.Transient);

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(ILifetimeProbeService));
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        Assert.Equal(typeof(LifetimeProbeService), descriptor.ImplementationType);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.LifetimeValidation.InvalidAndConflictingDeclarations")]
    public void LifetimeValidation_GivenUnsupportedLifetimeValue_ExpectedArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginServiceLifetimeMapping.ToServiceLifetime((PluginServiceLifetime)1234));
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.LifetimeValidation.InvalidAndConflictingDeclarations")]
    public void LifetimeValidation_GivenConflictingDeclarationsForSameServiceContract_ExpectedInvalidOperationException()
    {
        var services = new ServiceCollection();

        services.AddPluginService<ILifetimeProbeService, LifetimeProbeService>(PluginServiceLifetime.Singleton);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddPluginService<ILifetimeProbeService, LifetimeProbeService>(PluginServiceLifetime.Scoped));

        Assert.Contains(nameof(ILifetimeProbeService), exception.Message);
    }

    [Fact]
    [Trait("ChecklistItem", "Core.PluginLifetimes.DeterministicMapping.EquivalentInputs")]
    public void LifetimePolicy_GivenEquivalentPluginInputs_ExpectedDeterministicDescriptorLifetimes()
    {
        var first = new ServiceDescriptor[]
        {
            ServiceDescriptor.Describe(typeof(LifetimeProbeService), static _ => new LifetimeProbeService(), ServiceLifetime.Singleton),
            ServiceDescriptor.Singleton(typeof(LifetimeProbeService), new LifetimeProbeService())
        };
        var second = new ServiceDescriptor[]
        {
            ServiceDescriptor.Singleton(typeof(LifetimeProbeService), new LifetimeProbeService()),
            ServiceDescriptor.Describe(typeof(LifetimeProbeService), static _ => new LifetimeProbeService(), ServiceLifetime.Singleton)
        };

        var firstResolved = PluginServiceLifetimeMapping.TryResolveLifetime(typeof(LifetimeProbeService), first, out var firstLifetime);
        var secondResolved = PluginServiceLifetimeMapping.TryResolveLifetime(typeof(LifetimeProbeService), second, out var secondLifetime);

        Assert.True(firstResolved);
        Assert.True(secondResolved);
        Assert.Equal(ServiceLifetime.Singleton, firstLifetime);
        Assert.Equal(firstLifetime, secondLifetime);
    }

    private interface ILifetimeProbeService
    {
    }

    private sealed class LifetimeProbeService : ILifetimeProbeService
    {
    }
}