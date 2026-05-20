using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
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
            Status = SyncResponseStatus.Success,
        };

        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.IsType<SyncResponseStatus>(response.Status!.Value);
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
}
