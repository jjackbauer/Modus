using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.SamplePlugins.Lifetime;
using Modus.SamplePlugins.Telemetry;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginResponseTypedOnlyInventoryTests
{
    private const string ChecklistItem = "Update all plugins in Plugin Change Inventory to return typed payload contracts only (no string payload fallback fields) [depends on typed core/host response contracts]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void PluginInventoryContracts_GivenTypedOnlyRequirement_ExpectedNoObjectOrStringResponsePayloadContracts()
    {
        var inventoryTypes = new[]
        {
            typeof(HostTelemetryPlugin),
            typeof(MachineTelemetryPlugin),
            typeof(ScopedLifetimePlugin),
            typeof(SingletonLifetimePlugin),
            typeof(TransientLifetimePlugin),
            typeof(TimerPlugin),
            typeof(FiveSecondIntervalsTimerPrint),
        };

        foreach (var pluginType in inventoryTypes)
        {
            var responderInterfaces = pluginType
                .GetInterfaces()
                .Where(static candidate =>
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(ISyncResponder<,>)
                    && candidate.GenericTypeArguments[0] == typeof(SyncRequest))
                .ToArray();

            Assert.NotEmpty(responderInterfaces);
            Assert.DoesNotContain(
                responderInterfaces,
                static candidate => candidate.GenericTypeArguments[1] == typeof(SyncResponse<object>));

            foreach (var responderInterface in responderInterfaces)
            {
                var responseType = responderInterface.GenericTypeArguments[1];
                Assert.True(responseType.IsGenericType);
                Assert.Equal(typeof(SyncResponse<>), responseType.GetGenericTypeDefinition());

                var payloadType = responseType.GenericTypeArguments[0];
                Assert.NotEqual(typeof(object), payloadType);
                Assert.NotEqual(typeof(string), payloadType);
            }
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task PluginInventoryDispatch_GivenRuntimeEndpointDispatch_ExpectedTypedPayloadObjectsForAllMigratedPlugins()
    {
        var cases = GetInventoryCases();
        var projections = cases
            .Select(static @case =>
                new RuntimeDispatchProjection(
                    pluginId: @case.PluginId,
                    operationName: @case.Operation,
                    pluginTypeFullName: @case.PluginType.FullName!,
                    serviceLifetime: @case.Lifetime))
            .ToArray();

        var mapper = new PluginEndpointMapper(new RuntimePluginRegistry(projections, projections));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        foreach (var @case in cases)
        {
            RegisterPlugin(builder.Services, @case.PluginType, @case.Lifetime);
        }

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();

        foreach (var @case in cases)
        {
            var correlationId = $"typed-only-{@case.PluginId.Replace('.', '-')}-{@case.Operation.Replace('.', '-')}";
            var httpResponse = await client.PostAsJsonAsync(
                $"/api/{@case.PluginId}/{@case.Operation}",
                new PluginOperationHttpRequest
                {
                    CorrelationId = correlationId,
                    Payload = "{}"
                });

            var body = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
            Assert.NotNull(body);
            Assert.True(body!.Success);
            Assert.Equal(SyncResponseStatus.Success, body.Status);
            Assert.Equal(correlationId, body.CorrelationId);

            Assert.False(body.Payload is string);

            var payload = PluginOperationPayload.AsJsonElement(body.Payload);
            Assert.Equal(JsonValueKind.Object, payload.ValueKind);
            Assert.True(
                payload.TryGetProperty(@case.ExpectedTopLevelField, out var typedField),
                $"Expected payload field '{@case.ExpectedTopLevelField}' for plugin '{@case.PluginId}'.");
            Assert.Equal(JsonValueKind.Object, typedField.ValueKind);
        }
    }

    private static IReadOnlyList<InventoryCase> GetInventoryCases()
    {
        return
        [
            new InventoryCase("Plugin.Host.Telemetry", "Telemetry.Host.CollectSnapshot", typeof(HostTelemetryPlugin), PluginServiceLifetime.Singleton, "result"),
            new InventoryCase("Plugin.Machine.Telemetry", "Telemetry.Machine.CollectSnapshot", typeof(MachineTelemetryPlugin), PluginServiceLifetime.Singleton, "result"),
            new InventoryCase("Plugin.Lifetime.Scoped", "Lifetime.Scoped.PrintId", typeof(ScopedLifetimePlugin), PluginServiceLifetime.Scoped, "result"),
            new InventoryCase("Plugin.Lifetime.Singleton", "Lifetime.Singleton.PrintId", typeof(SingletonLifetimePlugin), PluginServiceLifetime.Singleton, "result"),
            new InventoryCase("Plugin.Lifetime.Transient", "Lifetime.Transient.PrintId", typeof(TransientLifetimePlugin), PluginServiceLifetime.Transient, "result")
        ];
    }

    private static void RegisterPlugin(IServiceCollection services, Type pluginType, PluginServiceLifetime lifetime)
    {
        var serviceLifetime = lifetime switch
        {
            PluginServiceLifetime.Singleton => ServiceLifetime.Singleton,
            PluginServiceLifetime.Scoped => ServiceLifetime.Scoped,
            PluginServiceLifetime.Transient => ServiceLifetime.Transient,
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported plugin lifetime.")
        };

        services.Add(new ServiceDescriptor(pluginType, pluginType, serviceLifetime));
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
            ContractName = new ContractName("Test.PluginInventoryProjection");
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

    private sealed record InventoryCase(
        string PluginId,
        string Operation,
        Type PluginType,
        PluginServiceLifetime Lifetime,
        string ExpectedTopLevelField);
}
