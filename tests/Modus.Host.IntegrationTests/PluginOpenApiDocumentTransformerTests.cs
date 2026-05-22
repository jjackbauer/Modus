using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginOpenApiDocumentTransformerTests
{
    [Fact]
    [Trait("ChecklistItem", "Register OpenAPI document transformer that builds plugin operation paths from RuntimePluginRegistry at document request time [mandatory - live swagger update]")]
    public async Task PluginOpenApiDocumentTransformer_GivenRuntimeRegistryMutation_ExpectedOpenApiProjectsCurrentPluginOperationsPerRequest()
    {
        var first = new OpenApiProjectionPlugin("Plugin.OpenApi.First", "OpenApi.First.Operation");
        var second = new OpenApiProjectionPlugin("Plugin.OpenApi.Second", "OpenApi.Second.Operation");
        var registry = new RuntimePluginRegistry([first], [first]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<PluginOpenApiDocumentTransformer>());

        await using var app = builder.Build();
        new PluginEndpointMapper(registry).Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();

        var firstDocument = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/Plugin.OpenApi.First/OpenApi.First.Operation", firstDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/Plugin.OpenApi.Second/OpenApi.Second.Operation", firstDocument, StringComparison.Ordinal);

        registry.Update([first, second], [first, second]);

        var secondDocument = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/Plugin.OpenApi.First/OpenApi.First.Operation", secondDocument, StringComparison.Ordinal);
        Assert.Contains("/api/Plugin.OpenApi.Second/OpenApi.Second.Operation", secondDocument, StringComparison.Ordinal);

        registry.Update([second], [second]);

        var thirdDocument = await client.GetStringAsync("/openapi/v1.json");
        Assert.DoesNotContain("/api/Plugin.OpenApi.First/OpenApi.First.Operation", thirdDocument, StringComparison.Ordinal);
        Assert.Contains("/api/Plugin.OpenApi.Second/OpenApi.Second.Operation", thirdDocument, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Expose Swagger UI against live OpenAPI document and verify newly onboarded plugins appear after refresh [mandatory - live swagger update]")]
    public async Task SwaggerLiveProjection_GivenSwaggerUiRefresh_ExpectedNewPluginOperationsVisible()
    {
        var first = new OpenApiProjectionPlugin("Plugin.OpenApi.First", "OpenApi.First.Operation");
        var second = new OpenApiProjectionPlugin("Plugin.OpenApi.Second", "OpenApi.Second.Operation");
        var registry = new RuntimePluginRegistry([first], [first]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<PluginOpenApiDocumentTransformer>());

        await using var app = builder.Build();
        new PluginEndpointMapper(registry).Map(app);
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Modus Host API v1");
            options.RoutePrefix = "swagger";
        });
        await app.StartAsync();

        var client = app.GetTestClient();

        var initialSwaggerUiResponse = await client.GetAsync("/swagger/index.html");
        Assert.True(initialSwaggerUiResponse.IsSuccessStatusCode);

        var initialDocument = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/Plugin.OpenApi.First/OpenApi.First.Operation", initialDocument, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/Plugin.OpenApi.Second/OpenApi.Second.Operation", initialDocument, StringComparison.Ordinal);

        registry.Update([first, second], [first, second]);

        var refreshedSwaggerUiResponse = await client.GetAsync("/swagger/index.html");
        Assert.True(refreshedSwaggerUiResponse.IsSuccessStatusCode);

        var refreshedDocument = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/Plugin.OpenApi.First/OpenApi.First.Operation", refreshedDocument, StringComparison.Ordinal);
        Assert.Contains("/api/Plugin.OpenApi.Second/OpenApi.Second.Operation", refreshedDocument, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginOpenApiDocumentTransformer_GivenProjectedConcretePath_ExpectedNoDynamicPathParameters()
    {
        var first = new OpenApiProjectionPlugin("Plugin.OpenApi.First", "OpenApi.First.Operation");
        var registry = new RuntimePluginRegistry([first], [first]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<PluginOpenApiDocumentTransformer>());

        await using var app = builder.Build();
        new PluginEndpointMapper(registry).Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();
        var documentJson = await client.GetStringAsync("/openapi/v1.json");

        using var document = JsonDocument.Parse(documentJson);
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths
            .GetProperty("/api/Plugin.OpenApi.First/OpenApi.First.Operation")
            .GetProperty("post");

        if (operation.TryGetProperty("parameters", out var parameters))
        {
            foreach (var parameter in parameters.EnumerateArray())
            {
                var name = parameter.GetProperty("name").GetString();
                var location = parameter.GetProperty("in").GetString();

                Assert.False(
                    string.Equals(location, "path", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(name, "pluginId", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "operation", StringComparison.OrdinalIgnoreCase)),
                    "Concrete projected OpenAPI paths should not require pluginId/operation path parameters.");
            }
        }
    }

    [Fact]
    public async Task PluginOpenApiDocumentTransformer_GivenProjectedConcretePaths_ExpectedUniqueOperationIds()
    {
        var first = new OpenApiProjectionPlugin("Plugin.OpenApi.First", "OpenApi.First.Operation");
        var second = new OpenApiProjectionPlugin("Plugin.OpenApi.Second", "OpenApi.Second.Operation");
        var registry = new RuntimePluginRegistry([first, second], [first, second]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<PluginOpenApiDocumentTransformer>());

        await using var app = builder.Build();
        new PluginEndpointMapper(registry).Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();
        var documentJson = await client.GetStringAsync("/openapi/v1.json");

        using var document = JsonDocument.Parse(documentJson);
        var paths = document.RootElement.GetProperty("paths");

        var concreteOperationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths.EnumerateObject())
        {
            if (!path.Name.StartsWith("/api/", StringComparison.Ordinal)
                || string.Equals(path.Name, "/api/{pluginId}/{operation}", StringComparison.Ordinal)
                || !path.Value.TryGetProperty("post", out var post))
            {
                continue;
            }

            var operationId = post.GetProperty("operationId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(operationId));
            Assert.True(concreteOperationIds.Add(operationId!), $"Duplicate concrete operationId found: {operationId}");
        }

        Assert.True(concreteOperationIds.Count >= 2, "Expected multiple concrete operationIds to validate uniqueness.");
    }

    [Fact]
    [Trait("ChecklistItem", "Add thread-safety guards for registry mutation and snapshot reads during concurrent requests and onboarding [depends on runtime registry abstraction]")]
    public async Task PluginOpenApiDocumentTransformer_GivenConcurrentOnboarding_ExpectedDocumentGenerationCompletesWithConsistentSnapshot()
    {
        var first = new OpenApiProjectionPlugin("Plugin.OpenApi.First", "OpenApi.First.Operation");
        var second = new OpenApiProjectionPlugin("Plugin.OpenApi.Second", "OpenApi.Second.Operation");
        var registry = new RuntimePluginRegistry([first], [first]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<PluginOpenApiDocumentTransformer>());

        await using var app = builder.Build();
        new PluginEndpointMapper(registry).Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();
        var writer = Task.Run(async () =>
        {
            for (var iteration = 0; iteration < 50; iteration++)
            {
                var plugin = iteration % 2 == 0 ? first : second;
                registry.Update([plugin], [plugin]);
                await Task.Yield();
            }
        });

        for (var iteration = 0; iteration < 25; iteration++)
        {
            var document = await client.GetStringAsync("/openapi/v1.json");
            var containsFirst = document.Contains("/api/Plugin.OpenApi.First/OpenApi.First.Operation", StringComparison.Ordinal);
            var containsSecond = document.Contains("/api/Plugin.OpenApi.Second/OpenApi.Second.Operation", StringComparison.Ordinal);

            Assert.True(containsFirst ^ containsSecond, "Expected each OpenAPI document read to project exactly one runtime snapshot.");
        }

        await writer;
    }

    [Fact]
    [Trait("ChecklistItem", "Emit structured diagnostics for plugin registry updates, endpoint dispatch misses, and OpenAPI projection failures [depends on runtime endpoint and OpenAPI projection]")]
    public async Task PluginOpenApiDocumentTransformer_GivenFaultingProjectionCatalog_ExpectedProjectionFailureDiagnosticRecorded()
    {
        var healthy = new OpenApiProjectionPlugin("Plugin.OpenApi.Healthy", "OpenApi.Healthy.Operation");
        var faulting = new FaultingOpenApiProjectionPlugin("Plugin.OpenApi.Faulting", "OpenApi.Faulting.Operation");
        var registry = new RuntimePluginRegistry([healthy, faulting], [healthy, faulting]);
        var statusRegistry = new HostStatusRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(statusRegistry);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<PluginOpenApiDocumentTransformer>());

        await using var app = builder.Build();
        new PluginEndpointMapper(registry).Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();
        var document = await client.GetStringAsync("/openapi/v1.json");

        Assert.DoesNotContain("/api/Plugin.OpenApi.Faulting/OpenApi.Faulting.Operation", document, StringComparison.Ordinal);

        var diagnostics = statusRegistry.GetCurrent().Diagnostics;
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Contains("stage=openapi outcome=failure", StringComparison.Ordinal)
                && diagnostic.Contains("reason=boom", StringComparison.Ordinal));
    }

    private sealed class OpenApiProjectionPlugin(string pluginId, string operation)
        : IPluginContract, IPluginOperationCatalog
    {
        public PluginId PluginId { get; } = new(pluginId);

        public ContractName ContractName { get; } = new($"{pluginId} Contract");

        public Version ContractVersion { get; } = new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations { get; } = [new(operation)];
    }

    private sealed class FaultingOpenApiProjectionPlugin(string pluginId, string operation)
        : IPluginContract, IPluginOperationCatalog
    {
        public PluginId PluginId { get; } = new(pluginId);

        public ContractName ContractName { get; } = new($"{pluginId} Contract");

        public Version ContractVersion { get; } = new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => throw new InvalidOperationException("boom");
    }
}