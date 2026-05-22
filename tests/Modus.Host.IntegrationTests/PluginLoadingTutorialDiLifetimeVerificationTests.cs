using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginLoadingTutorialDiLifetimeVerificationTests
{
    private const string ChecklistItem = "Document DI lifetime verification walkthrough for singleton, scoped, and transient plugin responders under repeated live API requests [depends on runtime operation invocation]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LifetimeVerification_GivenSingletonPluginAcrossRequests_ExpectedSameResponderIdentity()
    {
        var registry = new RuntimePluginRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(static services => new PluginEndpointMapper(services.GetRequiredService<RuntimePluginRegistry>()));
        builder.Services.AddSingleton<LifetimeSingletonResponder>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Tutorial.Lifetime.Singleton",
            operationName: "Tutorial.Lifetime.Singleton.Verify",
            pluginTypeFullName: typeof(LifetimeSingletonResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Singleton);
        registry.Update([projection], [projection]);

        var client = app.GetTestClient();
        var first = await DispatchAndReadLifetimePayloadAsync(
            client,
            "Plugin.Tutorial.Lifetime.Singleton",
            "Tutorial.Lifetime.Singleton.Verify",
            "tutorial-lifetime-singleton-a");
        var second = await DispatchAndReadLifetimePayloadAsync(
            client,
            "Plugin.Tutorial.Lifetime.Singleton",
            "Tutorial.Lifetime.Singleton.Verify",
            "tutorial-lifetime-singleton-b");

        Assert.Equal(first.InstanceId, second.InstanceId);
        Assert.Equal(1, first.InvocationCount);
        Assert.Equal(2, second.InvocationCount);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LifetimeVerification_GivenScopedPluginAcrossRequests_ExpectedDifferentScopeBoundResponderIdentities()
    {
        var registry = new RuntimePluginRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(static services => new PluginEndpointMapper(services.GetRequiredService<RuntimePluginRegistry>()));
        builder.Services.AddScoped<LifetimeScopedResponder>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Tutorial.Lifetime.Scoped",
            operationName: "Tutorial.Lifetime.Scoped.Verify",
            pluginTypeFullName: typeof(LifetimeScopedResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Scoped);
        registry.Update([projection], [projection]);

        var client = app.GetTestClient();
        var first = await DispatchAndReadLifetimePayloadAsync(
            client,
            "Plugin.Tutorial.Lifetime.Scoped",
            "Tutorial.Lifetime.Scoped.Verify",
            "tutorial-lifetime-scoped-a");
        var second = await DispatchAndReadLifetimePayloadAsync(
            client,
            "Plugin.Tutorial.Lifetime.Scoped",
            "Tutorial.Lifetime.Scoped.Verify",
            "tutorial-lifetime-scoped-b");

        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.Equal(1, first.InvocationCount);
        Assert.Equal(1, second.InvocationCount);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task LifetimeVerification_GivenTransientPluginRepeatedCalls_ExpectedNewResponderIdentityPerCall()
    {
        var registry = new RuntimePluginRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(static services => new PluginEndpointMapper(services.GetRequiredService<RuntimePluginRegistry>()));
        builder.Services.AddTransient<LifetimeTransientResponder>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var projection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Tutorial.Lifetime.Transient",
            operationName: "Tutorial.Lifetime.Transient.Verify",
            pluginTypeFullName: typeof(LifetimeTransientResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Transient);
        registry.Update([projection], [projection]);

        var client = app.GetTestClient();
        var first = await DispatchAndReadLifetimePayloadAsync(
            client,
            "Plugin.Tutorial.Lifetime.Transient",
            "Tutorial.Lifetime.Transient.Verify",
            "tutorial-lifetime-transient-a");
        var second = await DispatchAndReadLifetimePayloadAsync(
            client,
            "Plugin.Tutorial.Lifetime.Transient",
            "Tutorial.Lifetime.Transient.Verify",
            "tutorial-lifetime-transient-b");

        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.Equal(1, first.InvocationCount);
        Assert.Equal(1, second.InvocationCount);
    }

    private static async Task<LifetimePayload> DispatchAndReadLifetimePayloadAsync(
        HttpClient client,
        string pluginId,
        string operation,
        string correlationId)
    {
        var dispatchResponse = await client.PostAsJsonAsync(
            $"/api/{pluginId}/{operation}",
            new PluginOperationHttpRequest
            {
                CorrelationId = correlationId,
                Payload = "{}"
            });

        var body = await dispatchResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.Equal(SyncResponseStatus.Success, body.Status);
        Assert.Equal(correlationId, body.CorrelationId);

        var payloadObject = Assert.IsType<JsonElement>(body.PayloadObject);
        Assert.Equal(JsonValueKind.Object, payloadObject.ValueKind);

        var instanceId = payloadObject.GetProperty("instanceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(instanceId));

        var invocationCount = payloadObject.GetProperty("invocationCount").GetInt32();
        Assert.True(invocationCount > 0);

        return new LifetimePayload(instanceId!, invocationCount);
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
            ContractName = new ContractName($"Contract.{pluginId}");
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

    private sealed class LifetimeSingletonResponder : LifetimeResponderBase
    {
        public override PluginId PluginId => new("Plugin.Tutorial.Lifetime.Singleton");

        protected override string OperationName => "Tutorial.Lifetime.Singleton.Verify";
    }

    private sealed class LifetimeScopedResponder : LifetimeResponderBase
    {
        public override PluginId PluginId => new("Plugin.Tutorial.Lifetime.Scoped");

        protected override string OperationName => "Tutorial.Lifetime.Scoped.Verify";
    }

    private sealed class LifetimeTransientResponder : LifetimeResponderBase
    {
        public override PluginId PluginId => new("Plugin.Tutorial.Lifetime.Transient");

        protected override string OperationName => "Tutorial.Lifetime.Transient.Verify";
    }

    private abstract class LifetimeResponderBase : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly Guid _instanceId = Guid.NewGuid();
        private int _invocationCount;

        public ContractName ContractName => new("Modus.Tutorial.Lifetime");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(OperationName)];

        public abstract PluginId PluginId { get; }

        protected abstract string OperationName { get; }

        public SyncResponse Handle(SyncRequest request)
        {
            var payload = new
            {
                pluginId = PluginId.Value,
                operation = request.Operation.Value,
                instanceId = _instanceId.ToString("N"),
                invocationCount = Interlocked.Increment(ref _invocationCount)
            };

            return new SyncResponse(
                Success: true,
                PayloadObject: payload,
                CorrelationId: request.CorrelationId);
        }
    }

    private readonly record struct LifetimePayload(string InstanceId, int InvocationCount);
}