using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PackWarningsBehaviorProofPolicyComplianceTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-pack-warnings-behavior-proof-policy-2026-05-23")]
    public async Task IntegrationGate_GivenEveryChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofPath()
    {
        var apiCatalog = new CatalogOnlyPlugin("Plugin.Policy.Business", ["Policy.Business.Op"]);
        var registry = new RuntimePluginRegistry([apiCatalog], [apiCatalog]);

        var statusRegistry = new HostStatusRegistry();
        statusRegistry.Update(
            new HostStatusSnapshot(
                State: HostRuntimeState.Running,
                LoadedPlugins:
                [
                    new LoadedPluginMetadata(
                        PluginId: new PluginId("Plugin.Policy.Business"),
                        AssemblyName: "Plugin.Policy.Business",
                        Version: new Version(1, 0, 0),
                        LifecycleState: PluginRuntimeState.Active,
                        Capabilities: [new CapabilityName("Cap.Policy.Business")]),
                ],
                CapabilityOwnership:
                [
                    new CapabilityOwnershipSnapshot(
                        Capability: new CapabilityName("Cap.Policy.Business"),
                        OwnerPluginId: new PluginId("Plugin.Policy.Business")),
                ]),
            ["stage=startup outcome=success watcher=registered path=C:/policy/plugins"]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi();
        builder.Services.AddModusPluginHostingRuntime();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(statusRegistry);
        builder.Services.AddScoped<ISyncResponder>(_ =>
            new PolicyResponder("Plugin.Policy.Business", "Policy.Business.Op", "business-proof"));

        await using var app = builder.Build();
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementStatusEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementPluginCapabilitiesEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var client = app.GetTestClient();

        var openApiHttpResponse = await client.GetAsync("/openapi/v1.json");
        var openApiDocument = await openApiHttpResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, openApiHttpResponse.StatusCode);
        Assert.Contains("/api/{pluginId}/{operation}", openApiDocument, StringComparison.Ordinal);

        var dispatchHttpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Policy.Business/Policy.Business.Op",
            new PluginOperationHttpRequest
            {
                CorrelationId = "policy-proof-correlation",
                Payload = "payload"
            });
        var dispatchPayload = await dispatchHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, dispatchHttpResponse.StatusCode);
        Assert.NotNull(dispatchPayload);
        Assert.True(dispatchPayload!.Success);
        Assert.Equal(SyncResponseStatus.Success, dispatchPayload.Status);
        Assert.Equal("policy-proof-correlation", dispatchPayload.CorrelationId);
        Assert.Contains("owner=Plugin.Policy.Business", dispatchPayload.Payload, StringComparison.Ordinal);
        Assert.Contains("business=business-proof", dispatchPayload.Payload, StringComparison.Ordinal);

        var statusHttpResponse = await client.GetAsync("/management/status");
        var statusPayload = await statusHttpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, statusHttpResponse.StatusCode);
        Assert.Equal("Running", statusPayload.GetProperty("state").GetString());
        Assert.Contains(
            statusPayload.GetProperty("loadedPlugins").EnumerateArray(),
            plugin => string.Equals(plugin.GetProperty("pluginId").GetString(), "Plugin.Policy.Business", StringComparison.Ordinal));

        var capabilitiesHttpResponse = await client.GetAsync("/management/plugins/capabilities");
        var capabilitiesPayload = await capabilitiesHttpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, capabilitiesHttpResponse.StatusCode);
        Assert.Contains(
            capabilitiesPayload.GetProperty("capabilities").EnumerateArray(),
            capability => string.Equals(capability.GetProperty("ownerPluginId").GetString(), "Plugin.Policy.Business", StringComparison.Ordinal));

        using var unauthorizedUploadRequest = BuildDummyUploadRequest();
        var unauthorizedUploadResponse = await client.PostAsync("/management/plugins/uploads", unauthorizedUploadRequest);
        var unauthorizedErrorPayload = await unauthorizedUploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedUploadResponse.StatusCode);
        Assert.Equal("No trusted plugin author keys are configured.", unauthorizedErrorPayload.GetProperty("error").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-pack-warnings-behavior-proof-policy-2026-05-23")]
    public async Task IntegrationGate_GivenApiFocusedTests_ExpectedOwnerBusinessLifetimeCorrelationIsolationAndNegativeGatesAsserted()
    {
        var ownerCatalog = new CatalogOnlyPlugin("Plugin.Owner.Policy", ["Orders.Submit"]);
        var scopedCatalog = new CatalogOnlyPlugin("Plugin.Lifetime.Policy", ["Lifetime.Verify"]);
        var registry = new RuntimePluginRegistry([ownerCatalog, scopedCatalog], [ownerCatalog, scopedCatalog]);
        var mapper = new PluginEndpointMapper(registry);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddScoped<ISyncResponder>(_ =>
            new PolicyResponder("Plugin.Owner.Policy", "Orders.Submit", "owner-business-ok"));
        builder.Services.AddScoped<ISyncResponder>(_ =>
            new ScopedLifetimeResponder("Plugin.Lifetime.Policy", "Lifetime.Verify"));

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();

        var ownerHttpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Owner.Policy/Orders.Submit",
            new PluginOperationHttpRequest
            {
                CorrelationId = "gate-correlation-owner",
                Payload = "order-payload"
            });
        var ownerPayload = await ownerHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, ownerHttpResponse.StatusCode);
        Assert.NotNull(ownerPayload);
        Assert.True(ownerPayload!.Success);
        Assert.Equal("gate-correlation-owner", ownerPayload.CorrelationId);
        Assert.Contains("owner=Plugin.Owner.Policy", ownerPayload.Payload, StringComparison.Ordinal);
        Assert.Contains("business=owner-business-ok", ownerPayload.Payload, StringComparison.Ordinal);

        var firstLifetimeHttpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Lifetime.Policy/Lifetime.Verify",
            new PluginOperationHttpRequest
            {
                CorrelationId = "gate-correlation-lifetime-1",
                Payload = "payload"
            });
        var firstLifetimePayload = await firstLifetimeHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        var secondLifetimeHttpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Lifetime.Policy/Lifetime.Verify",
            new PluginOperationHttpRequest
            {
                CorrelationId = "gate-correlation-lifetime-2",
                Payload = "payload"
            });
        var secondLifetimePayload = await secondLifetimeHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, firstLifetimeHttpResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLifetimeHttpResponse.StatusCode);
        Assert.NotNull(firstLifetimePayload);
        Assert.NotNull(secondLifetimePayload);
        Assert.Equal("gate-correlation-lifetime-1", firstLifetimePayload!.CorrelationId);
        Assert.Equal("gate-correlation-lifetime-2", secondLifetimePayload!.CorrelationId);
        Assert.NotEqual(firstLifetimePayload.Payload, secondLifetimePayload.Payload);

        registry.Update([], []);

        var isolatedHttpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Owner.Policy/Orders.Submit",
            new PluginOperationHttpRequest
            {
                CorrelationId = "gate-correlation-negative",
                Payload = "order-payload"
            });
        var isolatedPayload = await isolatedHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, isolatedHttpResponse.StatusCode);
        Assert.NotNull(isolatedPayload);
        Assert.False(isolatedPayload!.Success);
        Assert.Equal(SyncResponseStatus.Failed, isolatedPayload.Status);
        Assert.Contains(
            "No runtime plugin operation owner found for plugin 'Plugin.Owner.Policy' and operation 'Orders.Submit'.",
            isolatedPayload.Payload,
            StringComparison.Ordinal);
    }

    private static MultipartFormDataContent BuildDummyUploadRequest()
    {
        var request = new MultipartFormDataContent();
        request.Add(new ByteArrayContent([1, 2, 3]), "package", "plugin.bundle.zip");
        request.Add(new ByteArrayContent([4, 5, 6]), "signature", "plugin.bundle.sig");
        return request;
    }

    private sealed class CatalogOnlyPlugin : IPluginContract, IPluginOperationCatalog
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOperations;

        public CatalogOnlyPlugin(string pluginId, IEnumerable<string> operations)
        {
            PluginId = new PluginId(pluginId);
            _supportedOperations = operations.Select(static operation => new OperationName(operation)).ToArray();
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Modus.Policy.CatalogOnly");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;
    }

    private sealed class PolicyResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly string _expectedOperation;
        private readonly string _businessResult;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public PolicyResponder(string pluginId, string expectedOperation, string businessResult)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
            _businessResult = businessResult;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Modus.Policy.Responder");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(_expectedOperation)];

        public SyncResponse Handle(SyncRequest request)
        {
            if (!string.Equals(request.Operation.Value, _expectedOperation, StringComparison.Ordinal))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unexpected-operation",
                    CorrelationId: request.CorrelationId,
                    Status: SyncResponseStatus.Failed);
            }

            return new SyncResponse(
                Success: true,
                Payload: $"owner={PluginId.Value};business={_businessResult};instance={_instanceId}",
                CorrelationId: request.CorrelationId,
                Status: SyncResponseStatus.Success);
        }
    }

    private sealed class ScopedLifetimeResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly string _expectedOperation;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public ScopedLifetimeResponder(string pluginId, string expectedOperation)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Modus.Policy.ScopedLifetime");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(_expectedOperation)];

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: $"scoped-instance={_instanceId}",
                CorrelationId: request.CorrelationId,
                Status: SyncResponseStatus.Success);
        }
    }
}
