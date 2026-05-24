using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class RuntimePluginRegistryTests
{
    [Fact]
    [Trait("ChecklistItem", "runtime-plugin-registry-di")]
    public void AddModusPluginHostingRuntime_GivenProviderBuild_ExpectedRuntimePluginRegistryResolvableAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PluginHostingOptions());

        var plugin = new RegistryProbePlugin();
        services.AddSingleton<IPluginContract>(plugin);
        services.AddSingleton<IPluginOperationCatalog>(plugin);

        services.AddModusPluginHostingRuntime();

        using var provider = services.BuildServiceProvider();

        var registry1 = provider.GetRequiredService<RuntimePluginRegistry>();
        var registry2 = provider.GetRequiredService<RuntimePluginRegistry>();
        var snapshot = registry1.GetSnapshot();

        Assert.Same(registry1, registry2);
        Assert.Contains(snapshot.Contracts, candidate => string.Equals(candidate.PluginId.Value, plugin.PluginId.Value, StringComparison.Ordinal));
        Assert.Contains(snapshot.Catalogs, candidate => ReferenceEquals(candidate, plugin));
    }

    [Fact]
    [Trait("ChecklistItem", "Replace per-plugin startup route expansion with one stable dynamic POST /api/{pluginId}/{operation} endpoint [mandatory - live endpoint resolve]")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-live-endpoint-resolve-transition-proof-2026-05-21")]
    public async Task PluginEndpointMapper_GivenRuntimeRegistry_ExpectedStableCatchAllRouteMappedFromRegistrySnapshot()
    {
        var plugin = new RegistryProbePlugin();
        var registry = new RuntimePluginRegistry([plugin], [plugin]);
        var mapper = new PluginEndpointMapper(registry);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi();
        await using var app = builder.Build();

        mapper.Map(app);
        await app.StartAsync();

        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Single(endpoints);
        Assert.Equal("/api/{pluginId}/{operation}", endpoints[0].RoutePattern.RawText);
        Assert.True(
            endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase) == true,
            "Expected the runtime-registry-backed plugin endpoint to accept POST requests.");
    }

    [Fact]
    [Trait("ChecklistItem", "Add thread-safety guards for registry mutation and snapshot reads during concurrent requests and onboarding [depends on runtime registry abstraction]")]
    public async Task RuntimePluginRegistry_GivenConcurrentReadAndWrite_ExpectedConsistentSnapshotSemantics()
    {
        var first = new RegistryProbePlugin("Plugin.Registry.First", "Registry.First.Op");
        var second = new RegistryProbePlugin("Plugin.Registry.Second", "Registry.Second.Op");
        var registry = new RuntimePluginRegistry([first], [first]);

        var writer = Task.Run(async () =>
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var plugin = iteration % 2 == 0 ? first : second;
                registry.Update([plugin], [plugin]);
                await Task.Yield();
            }
        });

        for (var iteration = 0; iteration < 200; iteration++)
        {
            var snapshot = registry.GetSnapshot();
            var contractPluginIds = snapshot.Contracts
                .Select(static contract => contract.PluginId.Value)
                .OrderBy(static pluginId => pluginId, StringComparer.Ordinal)
                .ToArray();
            var catalogPluginIds = snapshot.Catalogs
                .OfType<IPluginContract>()
                .Select(static contract => contract.PluginId.Value)
                .OrderBy(static pluginId => pluginId, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(contractPluginIds, catalogPluginIds);
            Assert.Single(contractPluginIds);
            Assert.Contains(contractPluginIds[0], new[] { "Plugin.Registry.First", "Plugin.Registry.Second" });

            await Task.Yield();
        }

        await writer;
    }

    private sealed class RegistryProbePlugin : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        public RegistryProbePlugin(string pluginId = "Plugin.Registry", string operationName = "Registry.Probe")
        {
            PluginId = new PluginId(pluginId);
            SupportedOperations = [new OperationName(operationName)];
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Registry Probe");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: "ok",
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }
}