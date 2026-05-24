using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Plugins;
using Modus.SamplePlugins.Orders;
using Modus.SamplePlugins.Payments;
using Xunit;

namespace Modus.Host.IntegrationTests;

[Trait("MigrationRegression", "true")]
public sealed class SamplePluginLifetimeBaseClassMigrationTests
{
    [Fact]
    public void ActivateSamplePlugin_GivenMigratedLifetimeBaseClass_ExpectedLifecycleHooksExecuteInDeterministicOrder()
    {
        var plugins = new PluginBase[]
        {
            new PaymentsGatewayPlugin(),
            new OrdersFulfillmentPlugin(),
        };

        foreach (var plugin in plugins)
        {
            Assert.Throws<InvalidOperationException>(() =>
                plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None)));

            plugin.Load(new PluginLoadContext(plugin.PluginId, CancellationToken.None));
            plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));
            plugin.Stop(new PluginStopContext(plugin.PluginId, CancellationToken.None));
            plugin.Unload(
                new PluginUnloadContext(
                    plugin.PluginId,
                    PluginUnloadReason.GracefulShutdown,
                    DateTimeOffset.UtcNow.AddSeconds(30),
                    CancellationToken.None));
        }
    }

    [Fact]
    public void RegisterPluginServices_GivenMigratedSamplePlugin_ExpectedServicesResolvedThroughDeclaredLifetimePath()
    {
        var paymentsPlugin = new PaymentsGatewayPlugin();
        var ordersPlugin = new OrdersFulfillmentPlugin();

        var services = new ServiceCollection();
        ((IPluginDependencyRegister)paymentsPlugin).Register(services);
        ((IPluginDependencyRegister)ordersPlugin).Register(services);

        var paymentsDescriptor = Assert.Single(services, d => d.ServiceType == typeof(PaymentsGatewayPlugin));
        Assert.Equal(ServiceLifetime.Singleton, paymentsDescriptor.Lifetime);

        var ordersDescriptor = Assert.Single(services, d => d.ServiceType == typeof(OrdersFulfillmentPlugin));
        Assert.Equal(ServiceLifetime.Scoped, ordersDescriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var singletonFromRoot = provider.GetRequiredService<PaymentsGatewayPlugin>();
        using var singletonScope = provider.CreateScope();
        var singletonFromScope = singletonScope.ServiceProvider.GetRequiredService<PaymentsGatewayPlugin>();
        Assert.Same(singletonFromRoot, singletonFromScope);

        using var firstScope = provider.CreateScope();
        var firstScopeOrdersA = firstScope.ServiceProvider.GetRequiredService<OrdersFulfillmentPlugin>();
        var firstScopeOrdersB = firstScope.ServiceProvider.GetRequiredService<OrdersFulfillmentPlugin>();
        Assert.Same(firstScopeOrdersA, firstScopeOrdersB);

        using var secondScope = provider.CreateScope();
        var secondScopeOrders = secondScope.ServiceProvider.GetRequiredService<OrdersFulfillmentPlugin>();
        Assert.NotSame(firstScopeOrdersA, secondScopeOrders);
    }
}