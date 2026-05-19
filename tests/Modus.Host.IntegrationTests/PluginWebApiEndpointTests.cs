using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
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
        var responders = new List<ISyncResponder> { testPlugin };

        var mapper = new PluginEndpointMapper(contracts, catalogs, responders);
        
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
            new List<IPluginOperationCatalog>(),
            new List<ISyncResponder>());

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
            new List<IPluginOperationCatalog>(),
            new List<ISyncResponder>());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => mapper.Map(null!));
    }

    [Fact]
    public void PluginEndpointMapper_GivenNullContracts_ExpectedArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PluginEndpointMapper(
            null!,
            new List<IPluginOperationCatalog>(),
            new List<ISyncResponder>()));
    }

    [Fact]
    public void PluginEndpointMapper_GivenNullCatalogs_ExpectedArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PluginEndpointMapper(
            new List<IPluginContract>(),
            null!,
            new List<ISyncResponder>()));
    }

    [Fact]
    public void PluginEndpointMapper_GivenNullResponders_ExpectedArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PluginEndpointMapper(
            new List<IPluginContract>(),
            new List<IPluginOperationCatalog>(),
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
            Operation: "Op.One",
            IsFallbackExplicit: false,
            FallbackReason: SyncFallbackReason.None,
            FallbackReasonCode: null,
            CorrelationId: "test-123");

        // Act: Dispatch to the first responder
        var response = dispatcher.Handle(request);

        // Assert: Response should be successful
        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("test-123", response.CorrelationId);
    }

    [Fact]
    public void PluginOperationSyncResponderDispatcher_GivenUnhandledOperation_ExpectedOperationNotFoundResponse()
    {
        // Arrange: Create responders that don't handle the operation
        var responder = new TestPlugin("Plugin.One", new[] { "Known.Op" });
        var dispatcher = new PluginOperationSyncResponderDispatcher(
            new List<ISyncResponder> { responder });

        var request = new SyncRequest(
            Operation: "Unknown.Op",
            IsFallbackExplicit: false,
            FallbackReason: SyncFallbackReason.None,
            FallbackReasonCode: null,
            CorrelationId: "test-456");

        // Act: Try to handle unknown operation
        var response = dispatcher.Handle(request);

        // Assert: Should return operation-not-found
        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.Contains("operation-not-found", response.Payload);
        Assert.Equal("test-456", response.CorrelationId);
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
            Operation: "Op.Test",
            IsFallbackExplicit: false,
            FallbackReason: SyncFallbackReason.None,
            FallbackReasonCode: null,
            CorrelationId: null);

        // Act
        var response = dispatcher.Handle(request);

        // Assert: Status should match the responder's response
        Assert.Equal(status, response.Status);
    }

    // Test helper classes
    private sealed class TestPlugin : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly string _pluginId;
        private readonly IReadOnlyCollection<string> _supportedOps;

        public TestPlugin(string pluginId, string[] supportedOps)
        {
            _pluginId = pluginId;
            _supportedOps = supportedOps;
        }

        public string PluginId => _pluginId;
        public string ContractName => "Test.Contract";
        public Version ContractVersion => new(1, 0, 0);
        public IReadOnlyCollection<string> SupportedOperations => _supportedOps;

        public SyncResponse Handle(SyncRequest request)
        {
            if (!_supportedOps.Contains(request.Operation))
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
        private readonly string _pluginId;
        private readonly IReadOnlyCollection<string> _supportedOps;
        private readonly SyncResponseStatus _status;

        public TestPluginWithStatus(string pluginId, string[] supportedOps, SyncResponseStatus status)
        {
            _pluginId = pluginId;
            _supportedOps = supportedOps;
            _status = status;
        }

        public string PluginId => _pluginId;
        public string ContractName => "Test.Contract";
        public Version ContractVersion => new(1, 0, 0);
        public IReadOnlyCollection<string> SupportedOperations => _supportedOps;

        public SyncResponse Handle(SyncRequest request)
        {
            if (!_supportedOps.Contains(request.Operation))
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
}
