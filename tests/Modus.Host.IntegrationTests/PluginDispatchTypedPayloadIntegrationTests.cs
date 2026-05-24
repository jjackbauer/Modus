using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginDispatchTypedPayloadIntegrationTests
{
    private const string ChecklistItem = "Enforce runtime owner resolution, DI lifetime path, and isolation guarantees with typed payload assertions in integration dispatch tests [depends on mapper and migration behavior]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task Dispatch_GivenUniqueOwnerAndTypedResponder_ExpectedOwnerResolvedAndTypedPayloadReturned()
    {
        var ownerCatalog = new CatalogOnlyPlugin("Plugin.Dispatch.Owner", ["Orders.Dispatch"]);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([ownerCatalog], [ownerCatalog]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => new OwnerProofResponder());

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Dispatch.Owner/Orders.Dispatch",
            new PluginOperationHttpRequest { CorrelationId = "dispatch-owner-typed", Payload = "payload" });
        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("dispatch-owner-typed", response.CorrelationId);

        var payload = PluginOperationPayload.AsJsonElement(response.Payload);
        Assert.Equal("Plugin.Dispatch.Owner", payload.GetProperty("ownerPluginId").GetString());
        Assert.Equal("Orders.Dispatch", payload.GetProperty("operation").GetString());
        Assert.Equal("accepted", payload.GetProperty("result").GetString());
    }

    [Theory]
    [InlineData(PluginServiceLifetime.Singleton, true)]
    [InlineData(PluginServiceLifetime.Scoped, false)]
    [InlineData(PluginServiceLifetime.Transient, false)]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task Dispatch_GivenRuntimeLifetimePath_ExpectedTypedPayloadTracksInstanceIdentity(
        PluginServiceLifetime serviceLifetime,
        bool expectSameInstance)
    {
        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Dispatch.Lifetime",
            operationName: "Lifetime.Typed.Verify",
            pluginTypeFullName: typeof(RuntimeLifetimeTypedResponder).FullName!,
            serviceLifetime: serviceLifetime);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([projection], [projection]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        RegisterByLifetime(builder.Services, serviceLifetime);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var first = await client.PostAsJsonAsync(
            "/api/Plugin.Dispatch.Lifetime/Lifetime.Typed.Verify",
            new PluginOperationHttpRequest { CorrelationId = "lifetime-typed-1", Payload = "payload" });
        var second = await client.PostAsJsonAsync(
            "/api/Plugin.Dispatch.Lifetime/Lifetime.Typed.Verify",
            new PluginOperationHttpRequest { CorrelationId = "lifetime-typed-2", Payload = "payload" });

        var firstBody = await first.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);

        var firstPayload = PluginOperationPayload.AsJsonElement(firstBody!.Payload);
        var secondPayload = PluginOperationPayload.AsJsonElement(secondBody!.Payload);
        Assert.Equal("typed-runtime", firstPayload.GetProperty("lifetimePath").GetString());
        Assert.Equal("typed-runtime", secondPayload.GetProperty("lifetimePath").GetString());

        var firstInstance = firstPayload.GetProperty("instanceId").GetString();
        var secondInstance = secondPayload.GetProperty("instanceId").GetString();
        if (expectSameInstance)
        {
            Assert.Equal(firstInstance, secondInstance);
        }
        else
        {
            Assert.NotEqual(firstInstance, secondInstance);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task Dispatch_GivenNoOwnerMatch_ExpectedTypedFailurePayloadAndNoSideEffectExecution()
    {
        IsolationGuardResponder.Reset();

        var ownerCatalog = new CatalogOnlyPlugin("Plugin.Dispatch.Owner", ["Orders.Dispatch"]);
        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry([ownerCatalog], [ownerCatalog]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => new IsolationGuardResponder());

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Dispatch.Mismatch/Orders.Dispatch",
            new PluginOperationHttpRequest { CorrelationId = "dispatch-isolation", Payload = "payload" });
        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.False(response!.Success);
        Assert.Equal(SyncResponseStatus.Failed, response.Status);
        Assert.Equal("dispatch-isolation", response.CorrelationId);

        var payload = PluginOperationPayload.AsJsonElement(response.Payload);
        Assert.Equal("dispatch-failure", payload.GetProperty("code").GetString());
        Assert.Contains("No runtime plugin operation owner found", payload.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, IsolationGuardResponder.InvocationCount);
    }

    private static void RegisterByLifetime(IServiceCollection services, PluginServiceLifetime lifetime)
    {
        switch (lifetime)
        {
            case PluginServiceLifetime.Singleton:
                services.AddSingleton<RuntimeLifetimeTypedResponder>();
                break;
            case PluginServiceLifetime.Scoped:
                services.AddScoped<RuntimeLifetimeTypedResponder>();
                break;
            case PluginServiceLifetime.Transient:
                services.AddTransient<RuntimeLifetimeTypedResponder>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported test lifetime.");
        }
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

        public ContractName ContractName => new("Test.CatalogOnly");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;
    }

    private sealed class RuntimeDispatchProjection : IRuntimePluginDispatchTarget
    {
        public RuntimeDispatchProjection(
            string pluginId,
            string operationName,
            string pluginTypeFullName,
            PluginServiceLifetime serviceLifetime)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName("Test.RuntimeProjection");
            ContractVersion = new Version(1, 0, 0);
            SupportedOperations = [new OperationName(operationName)];
            PluginTypeFullName = pluginTypeFullName;
            ServiceLifetime = serviceLifetime;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public string? PluginTypeFullName { get; }

        public PluginServiceLifetime? ServiceLifetime { get; }
    }

    private sealed class OwnerProofResponder : IPluginContract, ISyncResponder
    {
        public PluginId PluginId => new("Plugin.Dispatch.Owner");

        public ContractName ContractName => new("Test.OwnerProofResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: new
                {
                    ownerPluginId = PluginId.Value,
                    operation = request.Operation.Value,
                    result = "accepted"
                },
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class RuntimeLifetimeTypedResponder : IPluginContract, ISyncResponder
    {
        private readonly string _instanceId = Guid.NewGuid().ToString("N");

        public PluginId PluginId => new("Plugin.Dispatch.Lifetime");

        public ContractName ContractName => new("Test.RuntimeLifetimeTypedResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            return new SyncResponse(
                Success: true,
                Payload: new
                {
                    lifetimePath = "typed-runtime",
                    instanceId = _instanceId,
                    operation = request.Operation.Value
                },
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class IsolationGuardResponder : IPluginContract, ISyncResponder
    {
        public static int InvocationCount { get; private set; }

        public static void Reset()
        {
            InvocationCount = 0;
        }

        public PluginId PluginId => new("Plugin.Dispatch.Owner");

        public ContractName ContractName => new("Test.IsolationGuardResponder");

        public Version ContractVersion => new(1, 0, 0);

        public SyncResponse Handle(SyncRequest request)
        {
            InvocationCount++;
            return new SyncResponse(
                Success: true,
                Payload: new { shouldNotExecute = true },
                CorrelationId: request.CorrelationId);
        }
    }
}