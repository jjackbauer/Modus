using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.Telemetry;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Modus.SamplePlugins.Telemetry;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginWebApiEndpointTests
{
    [Fact]
    public void Map_GivenValidPluginAndCatalog_ExpectedEndpointMapperRegistersRoutes()
    {
        // Arrange: Create a mock plugin with operations
        var testPlugin = new TestPlugin(
            pluginId: "Test.Plugin",
            supportedOps: new[] { "Op.One", "Op.Two" });

        var contracts = new List<IPluginContract> { testPlugin };
        var catalogs = new List<IPluginOperationCatalog> { testPlugin };

        var mapper = new PluginEndpointMapper(contracts, catalogs);
        
        // Create a real WebApplication
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddModusPluginHosting(opts =>
        {
            opts.PluginsPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}");
        });
        
        using var app = builder.Build();

        // Act: Map should complete without throwing
        var result = mapper.Map(app);

        // Assert: Map should return the app for chaining
        Assert.Same(app, result);
    }

    [Fact]
    public void Map_GivenEmptyPlugins_ExpectedNoRoutesRegisteredSuccessfully()
    {
        // Arrange: Empty collections
        var mapper = new PluginEndpointMapper(
            new List<IPluginContract>(),
            new List<IPluginOperationCatalog>());

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddModusPluginHosting(opts =>
        {
            opts.PluginsPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}");
        });
        
        using var app = builder.Build();

        // Act: Map with empty collections should succeed
        var result = mapper.Map(app);

        // Assert: Should return the app
        Assert.Same(app, result);
    }

    [Fact]
    public void Map_GivenNullApplication_ExpectedArgumentNullException()
    {
        // Arrange
        var mapper = new PluginEndpointMapper(
            new List<IPluginContract>(),
            new List<IPluginOperationCatalog>());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => mapper.Map(null!));
    }

    [Fact]
    public void PluginEndpointMapper_GivenNullContracts_ExpectedArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PluginEndpointMapper(
            null!,
            new List<IPluginOperationCatalog>()));
    }

    [Fact]
    public void PluginEndpointMapper_GivenNullCatalogs_ExpectedArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PluginEndpointMapper(
            new List<IPluginContract>(),
            null!));
    }

    [Fact]
    public void PluginOperationSyncResponderDispatcher_GivenMultipleResponders_ExpectedFirstResponderHandles()
    {
        // Arrange: Create test plugins/responders
        var responder1 = new TestPlugin("Plugin.One", new[] { "Op.One" });
        var responder2 = new TestPlugin("Plugin.Two", new[] { "Op.Two" });
        var dispatcher = new PluginOperationSyncResponderDispatcher(
            new List<ISyncResponder> { responder1, responder2 });

        var request = new SyncRequest(
            Operation: new OperationName("Op.One"),
            IsFallbackExplicit: false,
            FallbackReason: SyncFallbackReason.None,
            FallbackReasonCode: null,
            CorrelationId: new CorrelationId("test-123"));

        // Act: Dispatch to the first responder
        var response = dispatcher.Handle(request);

        // Assert: Response should be successful
        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal(new CorrelationId("test-123"), response.CorrelationId);
    }

    [Fact]
    public void PluginOperationSyncResponderDispatcher_GivenUnhandledOperation_ExpectedOperationNotFoundResponse()
    {
        // Arrange: Create responders that don't handle the operation
        var responder = new TestPlugin("Plugin.One", new[] { "Known.Op" });
        var dispatcher = new PluginOperationSyncResponderDispatcher(
            new List<ISyncResponder> { responder });

        var request = new SyncRequest(
            Operation: new OperationName("Unknown.Op"),
            IsFallbackExplicit: false,
            FallbackReason: SyncFallbackReason.None,
            FallbackReasonCode: null,
            CorrelationId: new CorrelationId("test-456"));

        // Act: Try to handle unknown operation
        var response = dispatcher.Handle(request);

        // Assert: Should return operation-not-found
        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.Contains("operation-not-found", response.Payload);
        Assert.Equal(new CorrelationId("test-456"), response.CorrelationId);
    }

    [Fact]
    public void PluginOperationSyncResponderDispatcher_GivenNullRequest_ExpectedArgumentNullException()
    {
        // Arrange
        var dispatcher = new PluginOperationSyncResponderDispatcher(new List<ISyncResponder>());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => dispatcher.Handle(null!));
    }

    [Fact]
    public void PluginOperationSyncResponderDispatcher_GivenNullResponders_ExpectedArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PluginOperationSyncResponderDispatcher(null!));
    }

    [Theory]
    [InlineData(SyncResponseStatus.Success)]
    [InlineData(SyncResponseStatus.Rejected)]
    [InlineData(SyncResponseStatus.Failed)]
    public void PluginOperationSyncResponderDispatcher_GivenMultipleStatusCodes_ExpectedProperHandling(
        SyncResponseStatus status)
    {
        // Arrange: Create a responder that returns a specific status
        var responder = new TestPluginWithStatus(
            "Plugin.Test",
            new[] { "Op.Test" },
            status);
        
        var dispatcher = new PluginOperationSyncResponderDispatcher(
            new List<ISyncResponder> { responder });

        var request = new SyncRequest(
            Operation: new OperationName("Op.Test"),
            IsFallbackExplicit: false,
            FallbackReason: SyncFallbackReason.None,
            FallbackReasonCode: null,
            CorrelationId: null);

        // Act
        var response = dispatcher.Handle(request);

        // Assert: Status should match the responder's response
        Assert.Equal(status, response.Status);
    }

    [Fact]
    public void PluginOperationHttpResponse_Status_GivenSyncResponseStatusAssigned_PropertyTypeIsNullableSyncResponseStatus()
    {
        var response = new PluginOperationHttpResponse
        {
            Success = true,
            Payload = "ok",
            PayloadObject = new { message = "ok" },
            Status = SyncResponseStatus.Success,
        };

        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.IsType<SyncResponseStatus>(response.Status!.Value);
        Assert.NotNull(response.PayloadObject);
    }

    [Fact]
    [Trait("ChecklistItem", "Reuse existing telemetry plugin abstractions and evolve handler return payloads to typed object responses")]
    public async Task HandlePluginOperation_GivenTypedSyncPayload_ExpectedHttpResponseIncludesPayloadObject()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Typed", ["Typed.Op"]);
        var mapper = new PluginEndpointMapper([catalog], [catalog]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => new TypedPayloadResponder("Plugin.Typed", "Typed.Op"));

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Typed/Typed.Op",
            new PluginOperationHttpRequest { CorrelationId = "typed-corr" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.NotNull(response.PayloadObject);
        var payloadObject = Assert.IsType<JsonElement>(response.PayloadObject);
        Assert.Equal(JsonValueKind.Object, payloadObject.ValueKind);
        Assert.Equal("typed", payloadObject.GetProperty("mode").GetString());
        Assert.Equal(2, payloadObject.GetProperty("count").GetInt32());
        Assert.Contains("\"mode\":\"typed\"", response.Payload, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Refactor machine and host telemetry plugin handlers to return typed measurements and structured metadata instead of log-only outputs")]
    public async Task HandlePluginOperation_GivenHostTelemetryPlugin_ExpectedHttpResponseIncludesTelemetryMeasurementsAndMetadata()
    {
        var plugin = new HostTelemetryPlugin();
        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));
        var mapper = new PluginEndpointMapper([plugin], [plugin]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISyncResponder>(plugin);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Host.Telemetry/Telemetry.Host.CollectSnapshot",
            new PluginOperationHttpRequest { CorrelationId = "telemetry-http" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.NotNull(response.PayloadObject);
        var payloadObject = Assert.IsType<JsonElement>(response.PayloadObject);
        Assert.Equal("Plugin.Host.Telemetry", payloadObject.GetProperty("pluginId").GetString());
        Assert.Equal("Telemetry.Host.CollectSnapshot", payloadObject.GetProperty("operation").GetString());
        Assert.Equal("host", payloadObject.GetProperty("source").GetString());
        Assert.Equal("runtime", payloadObject.GetProperty("category").GetString());

        var measurements = payloadObject.GetProperty("measurements").EnumerateArray().ToArray();
        Assert.Contains(measurements, static measurement =>
            measurement.GetProperty("name").GetString() == "cpu.percent"
            && measurement.GetProperty("unit").GetString() == "percent"
            && measurement.GetProperty("kind").GetString() == "gauge");
        Assert.Contains(measurements, static measurement =>
            measurement.GetProperty("name").GetString() == "memory.workingSet.bytes"
            && measurement.GetProperty("unit").GetString() == "bytes");

        var metadata = payloadObject.GetProperty("metadata");
        Assert.True(metadata.TryGetProperty("processId", out var processId));
        Assert.False(string.IsNullOrWhiteSpace(processId.GetString()));
        Assert.True(metadata.TryGetProperty("processorCount", out var processorCount));
        Assert.True(processorCount.GetString() is { Length: > 0 });
    }

    [Fact]
    [Trait("ChecklistItem", "Standardize telemetry endpoint envelope with source, collectedAt, and measurement list metadata [depends on machine and host telemetry endpoints]")]
    [Trait("ChecklistItem", "Prove management API runtime contracts remain stable after OpenAPI mapping refactor [depends on management endpoint integration behavior proof]")]
    public async Task BuildTelemetryEnvelope_GivenTelemetryResult_IncludesSourceTimestampAndMeasurements()
    {
        var plugin = new HostTelemetryPlugin();
        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IHostTelemetryPluginContract>(plugin);
        builder.Services.AddSingleton<TelemetryAggregationService>();
        builder.Services.AddSingleton<ManagementTelemetryEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementTelemetryEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/telemetry/host");
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        var payload = Assert.Single(response!);
        Assert.Equal("Plugin.Host.Telemetry", payload.GetProperty("pluginId").GetString());
        Assert.Equal("Telemetry.Host.CollectSnapshot", payload.GetProperty("operation").GetString());
        Assert.Equal("host", payload.GetProperty("source").GetString());
        Assert.Equal("runtime", payload.GetProperty("category").GetString());
        Assert.True(payload.TryGetProperty("collectedAt", out var collectedAt));
        Assert.Equal(JsonValueKind.String, collectedAt.ValueKind);

        var measurements = payload.GetProperty("measurements");
        Assert.Equal(7, measurements.GetProperty("count").GetInt32());
        var items = measurements.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(measurements.GetProperty("count").GetInt32(), items.Length);
        Assert.Contains(items, static measurement =>
            measurement.GetProperty("name").GetString() == "cpu.percent"
            && measurement.GetProperty("unit").GetString() == "percent"
            && measurement.GetProperty("kind").GetString() == "gauge"
            && measurement.GetProperty("value").GetDouble() >= 0d);
        Assert.Contains(items, static measurement =>
            measurement.GetProperty("name").GetString() == "memory.workingSet.bytes"
            && measurement.GetProperty("unit").GetString() == "bytes"
            && measurement.GetProperty("kind").GetString() == "gauge"
            && measurement.GetProperty("value").GetDouble() >= 0d);

        var metadata = payload.GetProperty("metadata");
        Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("processId").GetString()));
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/telemetry/host endpoint returning typed host measurements [depends on telemetry aggregation service]")]
    public async Task GetHostTelemetry_GivenNoMeasurements_ReturnsOkWithEmptyCollection()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TelemetryAggregationService>();
        builder.Services.AddSingleton<ManagementTelemetryEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementTelemetryEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/telemetry/host");
        var response = await httpResponse.Content.ReadFromJsonAsync<TelemetryResult[]>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.Empty(response!);
    }

    [Fact]
    [Trait("ChecklistItem", "Standardize telemetry endpoint envelope with source, collectedAt, and measurement list metadata [depends on machine and host telemetry endpoints]")]
    public async Task BuildTelemetryEnvelope_GivenMixedMeasurementUnits_PreservesUnitPerMeasurement()
    {
        var plugin = new MachineTelemetryPlugin();
        plugin.Start(new PluginStartContext(plugin.PluginId, CancellationToken.None));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IMachineTelemetryPluginContract>(plugin);
        builder.Services.AddSingleton<TelemetryAggregationService>();
        builder.Services.AddSingleton<ManagementTelemetryEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementTelemetryEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/telemetry/machine");
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        var payload = Assert.Single(response!);
        Assert.Equal("Plugin.Machine.Telemetry", payload.GetProperty("pluginId").GetString());
        Assert.Equal("Telemetry.Machine.CollectSnapshot", payload.GetProperty("operation").GetString());
        Assert.Equal("machine", payload.GetProperty("source").GetString());
        Assert.Equal("system", payload.GetProperty("category").GetString());
        Assert.True(payload.TryGetProperty("collectedAt", out var collectedAt));
        Assert.Equal(JsonValueKind.String, collectedAt.ValueKind);

        var measurements = payload.GetProperty("measurements");
        Assert.Equal(4, measurements.GetProperty("count").GetInt32());
        var items = measurements.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(measurements.GetProperty("count").GetInt32(), items.Length);
        Assert.Contains(items, static measurement =>
            measurement.GetProperty("name").GetString() == "cpu.percent"
            && measurement.GetProperty("unit").GetString() == "percent"
            && measurement.GetProperty("kind").GetString() == "gauge");
        Assert.Contains(items, static measurement =>
            measurement.GetProperty("name").GetString() == "memory.totalPhysical.bytes"
            && measurement.GetProperty("unit").GetString() == "bytes"
            && measurement.GetProperty("kind").GetString() == "gauge");
        Assert.Contains(items, static measurement =>
            measurement.GetProperty("name").GetString() == "memory.load.percent"
            && measurement.GetProperty("unit").GetString() == "percent"
            && measurement.GetProperty("kind").GetString() == "gauge");

        var metadata = payload.GetProperty("metadata");
        Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("osDescription").GetString()));
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/status endpoint exposing loaded plugins, capabilities, versions, and lifecycle state [depends on host status snapshot contract]")]
    [Trait("ChecklistItem", "Prove management API runtime contracts remain stable after OpenAPI mapping refactor [depends on management endpoint integration behavior proof]")]
    public async Task GetHostStatus_GivenRunningHost_ReturnsOkWithPluginInventory()
    {
        var registry = new HostStatusRegistry();
        registry.Update(
            new HostStatusSnapshot(
                State: HostRuntimeState.Running,
                LoadedPlugins:
                [
                    new LoadedPluginMetadata(
                        PluginId: new PluginId("Plugin.Inventory"),
                        AssemblyName: "Plugin.Inventory",
                        Version: new Version(2, 1, 0),
                        LifecycleState: PluginRuntimeState.Active,
                        Capabilities: [new CapabilityName("Cap.Inventory"), new CapabilityName("Cap.Shared")]),
                    new LoadedPluginMetadata(
                        PluginId: new PluginId("Plugin.Orders"),
                        AssemblyName: "Plugin.Orders",
                        Version: new Version(1, 5, 0),
                        LifecycleState: PluginRuntimeState.Active,
                        Capabilities: [new CapabilityName("Cap.Orders")]),
                ],
                CapabilityOwnership:
                [
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Inventory"),
                        OwnerPluginId: new PluginId("Plugin.Inventory")),
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Orders"),
                        OwnerPluginId: new PluginId("Plugin.Orders")),
                ]),
            ["stage=startup outcome=success watcher=registered path=C:/temp/plugins"]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton<ManagementStatusEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementStatusEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/status");
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal("Running", response.GetProperty("state").GetString());

        var loadedPlugins = response.GetProperty("loadedPlugins").EnumerateArray().ToArray();
        Assert.Equal(2, loadedPlugins.Length);
        Assert.Collection(
            loadedPlugins,
            inventory =>
            {
                Assert.Equal("Plugin.Inventory", inventory.GetProperty("pluginId").GetString());
                Assert.Equal("Plugin.Inventory", inventory.GetProperty("assemblyName").GetString());
                Assert.Equal("2.1.0", inventory.GetProperty("version").GetString());
                Assert.Equal("Active", inventory.GetProperty("lifecycleState").GetString());
                Assert.Equal(
                    ["Cap.Inventory", "Cap.Shared"],
                    inventory.GetProperty("capabilities").EnumerateArray().Select(static item => item.GetString()!).ToArray());
            },
            orders =>
            {
                Assert.Equal("Plugin.Orders", orders.GetProperty("pluginId").GetString());
                Assert.Equal("1.5.0", orders.GetProperty("version").GetString());
                Assert.Equal("Active", orders.GetProperty("lifecycleState").GetString());
                Assert.Equal(
                    ["Cap.Orders"],
                    orders.GetProperty("capabilities").EnumerateArray().Select(static item => item.GetString()!).ToArray());
            });

        var capabilityOwnership = response.GetProperty("capabilityOwnership").EnumerateArray().ToArray();
        Assert.Equal(2, capabilityOwnership.Length);
        Assert.Collection(
            capabilityOwnership,
            inventory =>
            {
                Assert.Equal("Cap.Inventory", inventory.GetProperty("capability").GetString());
                Assert.Equal("Plugin.Inventory", inventory.GetProperty("ownerPluginId").GetString());
            },
            orders =>
            {
                Assert.Equal("Cap.Orders", orders.GetProperty("capability").GetString());
                Assert.Equal("Plugin.Orders", orders.GetProperty("ownerPluginId").GetString());
            });

        var diagnostics = response.GetProperty("diagnostics").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        Assert.Equal(["stage=startup outcome=success watcher=registered path=C:/temp/plugins"], diagnostics);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/status endpoint exposing loaded plugins, capabilities, versions, and lifecycle state [depends on host status snapshot contract]")]
    public async Task GetHostStatus_GivenPluginLoadErrors_ExposesFailedPluginDiagnostics()
    {
        var registry = new HostStatusRegistry();
        registry.Update(
            new HostStatusSnapshot(
                State: HostRuntimeState.Degraded,
                LoadedPlugins:
                [
                    new LoadedPluginMetadata(
                        PluginId: new PluginId("Plugin.Inventory"),
                        AssemblyName: "Plugin.Inventory",
                        Version: new Version(2, 1, 0),
                        LifecycleState: PluginRuntimeState.Active,
                        Capabilities: [new CapabilityName("Cap.Inventory")]),
                ],
                CapabilityOwnership:
                [
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Inventory"),
                        OwnerPluginId: new PluginId("Plugin.Inventory")),
                ]),
            [
                "stage=activation plugin=Plugin.Legacy outcome=failure reason=boom",
                "stage=isolation plugin=Plugin.Legacy failed-stage=activation outcome=isolated",
            ]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton<ManagementStatusEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementStatusEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/status");
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal("Degraded", response.GetProperty("state").GetString());

        var loadedPlugin = Assert.Single(response.GetProperty("loadedPlugins").EnumerateArray().ToArray());
        Assert.Equal("Plugin.Inventory", loadedPlugin.GetProperty("pluginId").GetString());

        var diagnostics = response.GetProperty("diagnostics").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.Contains("stage=activation plugin=Plugin.Legacy outcome=failure reason=boom", diagnostics, StringComparer.Ordinal);
        Assert.Contains("stage=isolation plugin=Plugin.Legacy failed-stage=activation outcome=isolated", diagnostics, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/plugins/capabilities endpoint for runtime capability catalog and ownership mapping [proposed - operational discoverability]")]
    [Trait("ChecklistItem", "Prove management API runtime contracts remain stable after OpenAPI mapping refactor [depends on management endpoint integration behavior proof]")]
    public async Task GetPluginCapabilitiesCatalog_GivenLoadedPlugins_ReturnsCapabilityOwnershipMatrix()
    {
        var registry = new HostStatusRegistry();
        registry.Update(
            new HostStatusSnapshot(
                State: HostRuntimeState.Running,
                LoadedPlugins:
                [
                    new LoadedPluginMetadata(
                        PluginId: new PluginId("Plugin.Orders"),
                        AssemblyName: "Plugin.Orders",
                        Version: new Version(1, 5, 0),
                        LifecycleState: PluginRuntimeState.Active,
                        Capabilities: [new CapabilityName("Cap.Orders")]),
                    new LoadedPluginMetadata(
                        PluginId: new PluginId("Plugin.Inventory"),
                        AssemblyName: "Plugin.Inventory",
                        Version: new Version(2, 1, 0),
                        LifecycleState: PluginRuntimeState.Active,
                        Capabilities: [new CapabilityName("Cap.Shared"), new CapabilityName("Cap.Inventory")]),
                ],
                CapabilityOwnership:
                [
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Shared"),
                        OwnerPluginId: new PluginId("Plugin.Inventory")),
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Inventory"),
                        OwnerPluginId: new PluginId("Plugin.Inventory")),
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Orders"),
                        OwnerPluginId: new PluginId("Plugin.Orders")),
                ]),
            []);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(new RuntimePluginRegistry([], []));
        builder.Services.AddSingleton<ManagementPluginCapabilitiesEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginCapabilitiesEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/plugins/capabilities");
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        var capabilities = response.GetProperty("capabilities").EnumerateArray().ToArray();
        Assert.Equal(3, capabilities.Length);
        Assert.Collection(
            capabilities,
            inventory =>
            {
                Assert.Equal("Cap.Inventory", inventory.GetProperty("capability").GetString());
                Assert.Equal("Plugin.Inventory", inventory.GetProperty("ownerPluginId").GetString());
            },
            orders =>
            {
                Assert.Equal("Cap.Orders", orders.GetProperty("capability").GetString());
                Assert.Equal("Plugin.Orders", orders.GetProperty("ownerPluginId").GetString());
            },
            shared =>
            {
                Assert.Equal("Cap.Shared", shared.GetProperty("capability").GetString());
                Assert.Equal("Plugin.Inventory", shared.GetProperty("ownerPluginId").GetString());
            });

        var plugins = response.GetProperty("plugins").EnumerateArray().ToArray();
        Assert.Equal(2, plugins.Length);
        Assert.Collection(
            plugins,
            inventory =>
            {
                Assert.Equal("Plugin.Inventory", inventory.GetProperty("pluginId").GetString());
                Assert.Equal(
                    ["Cap.Inventory", "Cap.Shared"],
                    inventory.GetProperty("capabilities").EnumerateArray().Select(static item => item.GetString()!).ToArray());
            },
            orders =>
            {
                Assert.Equal("Plugin.Orders", orders.GetProperty("pluginId").GetString());
                Assert.Equal(
                    ["Cap.Orders"],
                    orders.GetProperty("capabilities").EnumerateArray().Select(static item => item.GetString()!).ToArray());
            });
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/plugins/capabilities endpoint for runtime capability catalog and ownership mapping [proposed - operational discoverability]")]
    public async Task GetPluginCapabilitiesCatalog_GivenNoPluginsLoaded_ReturnsOkWithEmptyCatalog()
    {
        var registry = new HostStatusRegistry();
        registry.Update(
            new HostStatusSnapshot(
                State: HostRuntimeState.Running,
                LoadedPlugins: [],
                CapabilityOwnership: []),
            []);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(new RuntimePluginRegistry([], []));
        builder.Services.AddSingleton<ManagementPluginCapabilitiesEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginCapabilitiesEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/plugins/capabilities");
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Empty(response.GetProperty("capabilities").EnumerateArray());
        Assert.Empty(response.GetProperty("plugins").EnumerateArray());
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/telemetry/machine endpoint returning typed machine measurements [depends on telemetry aggregation service]")]
    public async Task GetMachineTelemetry_GivenProviderFailure_ReturnsProblemDetailsWithCorrelationId()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IMachineTelemetryPluginContract>(new FailingMachineTelemetryProvider());
        builder.Services.AddSingleton<TelemetryAggregationService>();
        builder.Services.AddSingleton<ManagementTelemetryEndpointMapper>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementTelemetryEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync("/management/telemetry/machine");
        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
        Assert.Equal("application/problem+json", httpResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal("Machine telemetry collection failed.", problem!.Title);
        Assert.Contains("Plugin.Machine.Telemetry.Failing", problem.Detail, StringComparison.Ordinal);
        Assert.True(problem.Extensions.TryGetValue("correlationId", out var correlationId));
        Assert.NotNull(correlationId);
        Assert.Contains("management-machine:", correlationId!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Resolve HTTP operation handlers through request-scoped DI only")]
    public async Task HandlePluginOperation_GivenOperationRequest_ExpectedResponderResolvedFromRequestScope()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Catalog", ["Catalog.Op"]);
        var mapper = new PluginEndpointMapper(
            [catalog],
            [catalog]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ =>
            new ScopeResolvedResponder("Plugin.Catalog", "Catalog.Op", "resolved-from-scope"));

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Catalog/Catalog.Op",
            new PluginOperationHttpRequest { CorrelationId = "corr-1", Payload = "payload" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("resolved-from-scope", response.Payload);
        Assert.Equal("corr-1", response.CorrelationId);
    }

    [Fact]
    [Trait("ChecklistItem", "Resolve HTTP operation handlers through request-scoped DI only")]
    public async Task HandlePluginOperation_GivenOnlyUnrelatedResponder_ExpectedNoFallbackSelection()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Catalog", ["Catalog.Op"]);
        var unrelatedResponder = new ScopeResolvedResponder("Plugin.Unrelated", "Other.Op", "unexpected");
        var mapper = new PluginEndpointMapper(
            [catalog],
            [catalog]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => unrelatedResponder);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Catalog/Catalog.Op",
            new PluginOperationHttpRequest { CorrelationId = "corr-2" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.False(response!.Success);
        Assert.Contains("No ISyncResponder registered in request scope for plugin 'Plugin.Catalog'.", response.Payload);
    }

    [Fact]
    [Trait("ChecklistItem", "Refactor dispatcher path to resolve plugin operation owners from RuntimePluginRegistry snapshot per request [mandatory - live DI resolve]")]
    public async Task HandlePluginOperation_GivenRuntimeRegistryNoLongerOwnsOperation_ExpectedDispatchRejectedFromCurrentSnapshot()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Catalog", ["Catalog.Op"]);
        var registry = new RuntimePluginRegistry([catalog], [catalog]);
        var mapper = new PluginEndpointMapper(registry);
        var statusRegistry = new HostStatusRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(statusRegistry);
        builder.Services.AddScoped<ISyncResponder>(_ =>
            new ScopeResolvedResponder("Plugin.Catalog", "Catalog.Op", "resolved-from-scope"));

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        registry.Update([], []);

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Catalog/Catalog.Op",
            new PluginOperationHttpRequest { CorrelationId = "corr-live-registry", Payload = "payload" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.False(response!.Success);
        Assert.Contains("No runtime plugin operation owner found for plugin 'Plugin.Catalog' and operation 'Catalog.Op'.", response.Payload);

        var diagnostics = statusRegistry.GetCurrent().Diagnostics;
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Contains("stage=dispatch outcome=miss", StringComparison.Ordinal)
                && diagnostic.Contains("plugin=Plugin.Catalog", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Catalog.Op", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Ensure dynamic endpoint pipeline can execute newly onboarded plugin operations without host restart [mandatory - live endpoint resolve]")]
    public async Task HandlePluginOperation_GivenRuntimeOnboardedPlugin_ExpectedOperationExecutesWithoutHostRestart()
    {
        var existing = new TestPlugin("Plugin.Existing", ["Existing.Op"]);
        var runtimeOnboarded = new TestPlugin("Plugin.Onboarded", ["Onboarded.Op"]);
        var registry = new RuntimePluginRegistry([existing], [existing]);
        var mapper = new PluginEndpointMapper(registry);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var beforeOnboarding = await client.PostAsJsonAsync(
            "/api/Plugin.Onboarded/Onboarded.Op",
            new PluginOperationHttpRequest { CorrelationId = "runtime-before", Payload = "payload" });
        var beforeBody = await beforeOnboarding.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, beforeOnboarding.StatusCode);
        Assert.NotNull(beforeBody);
        Assert.False(beforeBody!.Success);
        Assert.Contains("No runtime plugin operation owner found", beforeBody.Payload, StringComparison.Ordinal);

        registry.Update([existing, runtimeOnboarded], [existing, runtimeOnboarded]);

        var afterOnboarding = await client.PostAsJsonAsync(
            "/api/Plugin.Onboarded/Onboarded.Op",
            new PluginOperationHttpRequest { CorrelationId = "runtime-after", Payload = "payload" });
        var afterBody = await afterOnboarding.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, afterOnboarding.StatusCode);
        Assert.NotNull(afterBody);
        Assert.True(afterBody!.Success);
        Assert.Equal(SyncResponseStatus.Success, afterBody.Status);
        Assert.Equal("runtime-after", afterBody.CorrelationId);
        Assert.Contains("Onboarded.Op", afterBody.Payload, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Add thread-safety guards for registry mutation and snapshot reads during concurrent requests and onboarding [depends on runtime registry abstraction]")]
    public async Task HandlePluginOperation_GivenRegistryMutatedMidRequest_ExpectedDispatchUsesConsistentSnapshot()
    {
        var runtimePlugin = new TestPlugin("Plugin.Concurrent", ["Concurrent.Op"]);
        var registry = new RuntimePluginRegistry([runtimePlugin], [runtimePlugin]);
        var mapper = new PluginEndpointMapper(registry);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTransient<ISyncResponder>(_ =>
        {
            registry.Update(Array.Empty<IPluginContract>(), Array.Empty<IPluginOperationCatalog>());
            return new ScopeResolvedResponder("Plugin.Unrelated", "Other.Op", "ignored");
        });

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Concurrent/Concurrent.Op",
            new PluginOperationHttpRequest { CorrelationId = "corr-concurrent", Payload = "payload" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("corr-concurrent", response.CorrelationId);
        Assert.Contains("Concurrent.Op", response.Payload, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Prove plugin operation endpoint runtime dispatch remains correct after OpenAPI mapping refactor [depends on endpoint integration behavior proof]")]
    [Trait("AuditArtifact", "iterative-implementation-openapi-dispatch-behavior-proof-2026-05-23")]
    public async Task MapPluginOperationEndpoint_GivenSupportedOpenApiMapping_ExpectedOperationDispatchBusinessSemanticsPreserved()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.OpenApi.Dispatch", ["OpenApi.Dispatch"]);
        var mapper = new PluginEndpointMapper([catalog], [catalog]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi();
        builder.Services.AddScoped<ISyncResponder>(_ =>
            new ScopeResolvedResponder("Plugin.OpenApi.Dispatch", "OpenApi.Dispatch", "business-ok"));

        await using var app = builder.Build();
        mapper.Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();

        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var openApiDocument = await openApiResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Contains("/api/{pluginId}/{operation}", openApiDocument, StringComparison.Ordinal);

        var dispatchHttpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.OpenApi.Dispatch/OpenApi.Dispatch",
            new PluginOperationHttpRequest { CorrelationId = "openapi-dispatch-corr", Payload = "payload" });
        var dispatchResponse = await dispatchHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, dispatchHttpResponse.StatusCode);
        Assert.NotNull(dispatchResponse);
        Assert.True(dispatchResponse!.Success);
        Assert.Equal(SyncResponseStatus.Success, dispatchResponse.Status);
        Assert.Equal("business-ok", dispatchResponse.Payload);
        Assert.Equal("openapi-dispatch-corr", dispatchResponse.CorrelationId);
    }

    [Fact]
    [Trait("ChecklistItem", "HandlePluginOperation_GivenScopedPluginAcrossDifferentRequests_ExpectedDifferentInstanceIds")]
    public async Task HandlePluginOperation_GivenScopedPluginAcrossDifferentRequests_ExpectedDifferentInstanceIds()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Scoped", ["Scoped.Op"]);
        var mapper = new PluginEndpointMapper([catalog], [catalog]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => new LifetimeProbeResponder("Plugin.Scoped", "Scoped.Op"));

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var firstResponse = await PostPluginOperationAsync(client, "Plugin.Scoped", "Scoped.Op", "scoped-1");
        var secondResponse = await PostPluginOperationAsync(client, "Plugin.Scoped", "Scoped.Op", "scoped-2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResponse.Body);
        Assert.NotNull(secondResponse.Body);
        Assert.NotEqual(firstResponse.Body!.Payload, secondResponse.Body!.Payload);
    }

    [Fact]
    [Trait("ChecklistItem", "HandlePluginOperation_GivenTransientPluginAcrossRepeatedCalls_ExpectedNewInstancePerCall")]
    public async Task HandlePluginOperation_GivenTransientPluginAcrossRepeatedCalls_ExpectedNewInstancePerCall()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Transient", ["Transient.Op"]);
        var mapper = new PluginEndpointMapper([catalog], [catalog]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTransient<ISyncResponder>(_ => new LifetimeProbeResponder("Plugin.Transient", "Transient.Op"));

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var firstResponse = await PostPluginOperationAsync(client, "Plugin.Transient", "Transient.Op", "transient-1");
        var secondResponse = await PostPluginOperationAsync(client, "Plugin.Transient", "Transient.Op", "transient-2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResponse.Body);
        Assert.NotNull(secondResponse.Body);
        Assert.NotEqual(firstResponse.Body!.Payload, secondResponse.Body!.Payload);
    }

    [Fact]
    [Trait("ChecklistItem", "HandlePluginOperation_GivenSingletonPluginAcrossRepeatedCalls_ExpectedSameInstance")]
    public async Task HandlePluginOperation_GivenSingletonPluginAcrossRepeatedCalls_ExpectedSameInstance()
    {
        var catalog = new CatalogOnlyPlugin("Plugin.Singleton", ["Singleton.Op"]);
        var mapper = new PluginEndpointMapper([catalog], [catalog]);
        var singletonResponder = new LifetimeProbeResponder("Plugin.Singleton", "Singleton.Op");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISyncResponder>(singletonResponder);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var firstResponse = await PostPluginOperationAsync(client, "Plugin.Singleton", "Singleton.Op", "singleton-1");
        var secondResponse = await PostPluginOperationAsync(client, "Plugin.Singleton", "Singleton.Op", "singleton-2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResponse.Body);
        Assert.NotNull(secondResponse.Body);
        Assert.Equal(firstResponse.Body!.Payload, secondResponse.Body!.Payload);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement explicit lifetime policy in dispatcher: singleton cache, scoped-per-request, transient-per-call using DI scopes [depends on runtime DI resolve]")]
    public async Task HandlePluginOperation_GivenRuntimeSingletonDispatchTarget_ExpectedSameInstanceAcrossRequests()
    {
        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Singleton",
            supportedOperation: "Singleton.Op",
            pluginTypeFullName: typeof(RuntimeSingletonResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Singleton);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([projection], [projection]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<RuntimeSingletonResponder>();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var firstResponse = await PostPluginOperationAsync(client, "Plugin.Runtime.Singleton", "Singleton.Op", "runtime-singleton-1");
        var secondResponse = await PostPluginOperationAsync(client, "Plugin.Runtime.Singleton", "Singleton.Op", "runtime-singleton-2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResponse.Body);
        Assert.NotNull(secondResponse.Body);
        Assert.Equal(firstResponse.Body!.Payload, secondResponse.Body!.Payload);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement plugin unload path with deterministic disposal and registry eviction [depends on runtime registry abstraction]")]
    public async Task HandlePluginOperation_GivenRuntimeSingletonPluginUnloaded_ExpectedCachedResponderDisposedAndRegistryEvicted()
    {
        DisposableRuntimeSingletonResponder.Reset();

        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Unload",
            supportedOperation: "Unload.Op",
            pluginTypeFullName: typeof(DisposableRuntimeSingletonResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Singleton);
        var registry = new RuntimePluginRegistry([projection], [projection]);
        var mapper = new PluginEndpointMapper(registry);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<DisposableRuntimeSingletonResponder>();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var beforeUnload = await PostPluginOperationAsync(client, "Plugin.Runtime.Unload", "Unload.Op", "runtime-unload-1");

        Assert.Equal(HttpStatusCode.OK, beforeUnload.StatusCode);
        Assert.NotNull(beforeUnload.Body);
        Assert.Equal(0, DisposableRuntimeSingletonResponder.DisposeCount);

        registry.Update(Array.Empty<IPluginContract>(), Array.Empty<IPluginOperationCatalog>());
        var snapshot = registry.GetSnapshot();

        Assert.Equal(1, DisposableRuntimeSingletonResponder.DisposeCount);
        Assert.DoesNotContain(snapshot.Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Unload", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Catalogs, x => string.Equals((x as IPluginContract)?.PluginId.Value, "Plugin.Runtime.Unload", StringComparison.Ordinal));

        var afterUnload = await PostPluginOperationAsync(client, "Plugin.Runtime.Unload", "Unload.Op", "runtime-unload-2");

        Assert.Equal(HttpStatusCode.InternalServerError, afterUnload.StatusCode);
        Assert.NotNull(afterUnload.Body);
        Assert.False(afterUnload.Body!.Success);
        Assert.Equal(SyncResponseStatus.Failed, afterUnload.Body.Status);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement explicit lifetime policy in dispatcher: singleton cache, scoped-per-request, transient-per-call using DI scopes [depends on runtime DI resolve]")]
    public async Task HandlePluginOperation_GivenRuntimeTransientDispatchTarget_ExpectedNewInstancePerCall()
    {
        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Transient",
            supportedOperation: "Transient.Op",
            pluginTypeFullName: typeof(RuntimeTransientResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Transient);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([projection], [projection]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTransient<RuntimeTransientResponder>();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var firstResponse = await PostPluginOperationAsync(client, "Plugin.Runtime.Transient", "Transient.Op", "runtime-transient-1");
        var secondResponse = await PostPluginOperationAsync(client, "Plugin.Runtime.Transient", "Transient.Op", "runtime-transient-2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResponse.Body);
        Assert.NotNull(secondResponse.Body);
        Assert.NotEqual(firstResponse.Body!.Payload, secondResponse.Body!.Payload);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement explicit lifetime policy in dispatcher: singleton cache, scoped-per-request, transient-per-call using DI scopes [depends on runtime DI resolve]")]
    public async Task HandlePluginOperation_GivenRuntimeScopedDispatchTarget_ExpectedResolvedWithinRequestBoundary()
    {
        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Scoped",
            supportedOperation: "Scoped.Op",
            pluginTypeFullName: typeof(RuntimeScopedResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Scoped);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([projection], [projection]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<RequestBoundaryMarker>();
        builder.Services.AddScoped<RuntimeScopedResponder>();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var firstResponse = await PostPluginOperationAsync(client, "Plugin.Runtime.Scoped", "Scoped.Op", "runtime-scoped-1");
        var secondResponse = await PostPluginOperationAsync(client, "Plugin.Runtime.Scoped", "Scoped.Op", "runtime-scoped-2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstResponse.Body);
        Assert.NotNull(secondResponse.Body);
        Assert.NotEqual(firstResponse.Body!.Payload, secondResponse.Body!.Payload);
    }

    [Fact]
    public async Task HandlePluginOperation_GivenRuntimeDispatchTargetWithoutLifetime_ExpectedResolvedFromRequestScope()
    {
        var projection = new RuntimeDispatchProjection(
            pluginId: "Modus.Core",
            supportedOperation: "Op.Modus.Core.HealthCheck",
            pluginTypeFullName: typeof(RuntimeNoLifetimeResponder).FullName!,
            serviceLifetime: null);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([projection], [projection]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTransient<RuntimeNoLifetimeResponder>();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await PostPluginOperationAsync(client, "Modus.Core", "Op.Modus.Core.HealthCheck", "runtime-no-lifetime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Body);
        Assert.True(response.Body!.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Body.Status);
        Assert.Equal("runtime-no-lifetime", response.Body.CorrelationId);
        Assert.Contains("health-ok", response.Body.Payload, StringComparison.Ordinal);
    }

    private sealed class FailingMachineTelemetryProvider : IMachineTelemetryPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        public PluginId PluginId => new("Plugin.Machine.Telemetry.Failing");

        public ContractName ContractName => new("Modus.PluginContract");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("Telemetry.Machine.CollectSnapshot")];

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: false,
                Payload: "machine-provider-failure",
                Status: SyncResponseStatus.Failed,
                CorrelationId: request.CorrelationId);
        }
    }

    // Test helper classes
    private sealed class TestPlugin : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOps;

        public TestPlugin(string pluginId, string[] supportedOps)
        {
            PluginId = new PluginId(pluginId);
            _supportedOps = supportedOps.Select(op => new OperationName(op)).ToArray();
        }

        public PluginId PluginId { get; }
        public ContractName ContractName => new ContractName("Test.Contract");
        public Version ContractVersion => new(1, 0, 0);
        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOps;

        public SyncResponse Handle(SyncRequest request)
        {
            if (!_supportedOps.Any(op => op == request.Operation))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: true,
                Payload: $"{{\"operation\":\"{request.Operation}\"}}",
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class TestPluginWithStatus : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOps;
        private readonly SyncResponseStatus _status;

        public TestPluginWithStatus(string pluginId, string[] supportedOps, SyncResponseStatus status)
        {
            PluginId = new PluginId(pluginId);
            _supportedOps = supportedOps.Select(op => new OperationName(op)).ToArray();
            _status = status;
        }

        public PluginId PluginId { get; }
        public ContractName ContractName => new ContractName("Test.Contract");
        public Version ContractVersion => new(1, 0, 0);
        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOps;

        public SyncResponse Handle(SyncRequest request)
        {
            if (!_supportedOps.Any(op => op == request.Operation))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: _status == SyncResponseStatus.Success,
                Payload: $"{{\"status\":\"{_status}\"}}",
                Status: _status,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class LifetimeProbeResponder : IPluginContract, ISyncResponder
    {
        private readonly string _expectedOperation;

        public LifetimeProbeResponder(string pluginId, string expectedOperation)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
            InstanceId = Guid.NewGuid().ToString("N");
        }

        public PluginId PluginId { get; }

        public string InstanceId { get; }

        public ContractName ContractName => new("Test.LifetimeProbe");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            if (!string.Equals(request.Operation.Value, _expectedOperation, StringComparison.Ordinal))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: true,
                Payload: InstanceId,
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private static async Task<(HttpStatusCode StatusCode, PluginOperationHttpResponse? Body)> PostPluginOperationAsync(
        HttpClient client,
        string pluginId,
        string operation,
        string correlationId)
    {
        var httpResponse = await client.PostAsJsonAsync(
            $"/api/{pluginId}/{operation}",
            new PluginOperationHttpRequest { CorrelationId = correlationId, Payload = "payload" });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        return (httpResponse.StatusCode, response);
    }

    private sealed class CatalogOnlyPlugin : IPluginContract, IPluginOperationCatalog
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOps;

        public CatalogOnlyPlugin(string pluginId, IEnumerable<string> supportedOps)
        {
            PluginId = new PluginId(pluginId);
            _supportedOps = supportedOps.Select(static op => new OperationName(op)).ToArray();
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Test.CatalogOnly");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOps;
    }

    private sealed class ScopeResolvedResponder : IPluginContract, ISyncResponder
    {
        private readonly string _expectedOperation;
        private readonly string _payload;

        public ScopeResolvedResponder(string pluginId, string expectedOperation, string payload)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
            _payload = payload;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Test.ScopeResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            if (!string.Equals(request.Operation.Value, _expectedOperation, StringComparison.Ordinal))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: true,
                Payload: _payload,
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RuntimeDispatchProjection : IRuntimePluginDispatchTarget
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOperations;

        public RuntimeDispatchProjection(
            string pluginId,
            string supportedOperation,
            string pluginTypeFullName,
            PluginServiceLifetime? serviceLifetime)
        {
            PluginId = new PluginId(pluginId);
            _supportedOperations = [new OperationName(supportedOperation)];
            PluginTypeFullName = pluginTypeFullName;
            ServiceLifetime = serviceLifetime;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Test.RuntimeDispatchProjection");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;

        public string? PluginTypeFullName { get; }

        public PluginServiceLifetime? ServiceLifetime { get; }
    }

    private sealed class RuntimeSingletonResponder : IPluginContract, ISyncResponder
    {
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public PluginId PluginId => new("Plugin.Runtime.Singleton");

        public ContractName ContractName => new("Test.RuntimeSingletonResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: _instanceId,
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RuntimeNoLifetimeResponder : IPluginContract, ISyncResponder
    {
        public PluginId PluginId => new("Modus.Core");

        public ContractName ContractName => new("Test.RuntimeNoLifetimeResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: "health-ok",
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class DisposableRuntimeSingletonResponder : IPluginContract, ISyncResponder, IDisposable
    {
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public static int DisposeCount { get; private set; }

        public static void Reset()
        {
            DisposeCount = 0;
        }

        public PluginId PluginId => new("Plugin.Runtime.Unload");

        public ContractName ContractName => new("Test.DisposableRuntimeSingletonResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: _instanceId,
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class RuntimeTransientResponder : IPluginContract, ISyncResponder
    {
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public PluginId PluginId => new("Plugin.Runtime.Transient");

        public ContractName ContractName => new("Test.RuntimeTransientResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: _instanceId,
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RuntimeScopedResponder : IPluginContract, ISyncResponder
    {
        private readonly RequestBoundaryMarker _boundaryMarker;

        public RuntimeScopedResponder(RequestBoundaryMarker boundaryMarker)
        {
            _boundaryMarker = boundaryMarker;
        }

        public PluginId PluginId => new("Plugin.Runtime.Scoped");

        public ContractName ContractName => new("Test.RuntimeScopedResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: _boundaryMarker.Id,
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RequestBoundaryMarker
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
    }

    private sealed class TypedPayloadResponder : IPluginContract, ISyncResponder
    {
        private readonly string _expectedOperation;

        public TypedPayloadResponder(string pluginId, string expectedOperation)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Test.TypedPayloadResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            if (!string.Equals(request.Operation.Value, _expectedOperation, StringComparison.Ordinal))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: true,
                PayloadObject: new { mode = "typed", count = 2 },
                CorrelationId: request.CorrelationId);
        }
    }
}
