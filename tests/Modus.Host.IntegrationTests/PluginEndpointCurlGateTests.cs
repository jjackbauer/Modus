using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Xunit;

namespace Modus.Host.IntegrationTests;

[Trait("MigrationRegression", "true")]
public sealed class PluginEndpointCurlGateTests
{
    [Fact]
    [Trait("ChecklistItem", "Add hosted endpoint probe gate that executes curl-style API calls and fails on runtime dispatch-failure contracts")]
    public async Task CurlGate_GivenHostRuntimeStarted_ExpectedNoDispatchFailureAcrossPluginOperationEndpoints()
    {
        var pluginsPath = ResolvePluginsPath();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

        await using var app = builder.Build();
        var provider = app.Services;

        var runner = provider.GetRequiredService<HostRunner>();
        var start = await runner.StartAsync(pluginsPath, CancellationToken.None);
        Assert.True(start.HostHealthy);

        provider.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var endpoints = BuildEndpointList(provider.GetRequiredService<RuntimePluginRegistry>());

        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var response = await client.PostAsJsonAsync(
                endpoint.Path,
                new PluginOperationHttpRequest { CorrelationId = endpoint.CorrelationId });

            var body = await response.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
            var payload = body is null ? default : PluginOperationPayload.AsJsonElement(body.Payload);
            var isDispatchFailure = payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("code", out var code)
                && string.Equals(code.GetString(), "dispatch-failure", StringComparison.Ordinal);

            if (!response.IsSuccessStatusCode || body is null || isDispatchFailure)
            {
                failures.Add($"{endpoint.Path} -> http={(int)response.StatusCode}, success={body?.Success.ToString() ?? "null"}, status={body?.Status.ToString() ?? "null"}, payload={payload}");
            }

            AssertTimerEndpointPayloadShape(endpoint.Path, body, payload);
        }

        Assert.True(
            failures.Count == 0,
            "Curl-style endpoint gate detected runtime dispatch failures:\n" + string.Join("\n", failures));
    }

    [Fact]
    [Trait("ChecklistItem", "Add hosted endpoint probe gate that executes curl-style API calls and fails on runtime dispatch-failure contracts")]
    public async Task CurlGate_GivenRuntimeCatalogProbe_ExpectedAllOperationEndpointsReturnNon5xxBehaviorContracts()
    {
        var pluginsPath = ResolvePluginsPath();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

        await using var app = builder.Build();
        var provider = app.Services;

        var runner = provider.GetRequiredService<HostRunner>();
        var start = await runner.StartAsync(pluginsPath, CancellationToken.None);
        Assert.True(start.HostHealthy);

        provider.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var endpoints = BuildEndpointList(provider.GetRequiredService<RuntimePluginRegistry>());

        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var response = await client.PostAsJsonAsync(
                endpoint.Path,
                new PluginOperationHttpRequest { CorrelationId = endpoint.CorrelationId });

            var body = await response.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
            var payload = body is null ? default : PluginOperationPayload.AsJsonElement(body.Payload);
            var isDispatchFailure = payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("code", out var code)
                && string.Equals(code.GetString(), "dispatch-failure", StringComparison.Ordinal);

            var is5xx = (int)response.StatusCode >= 500;
            if (is5xx || body is null || isDispatchFailure)
            {
                failures.Add($"{endpoint.Path} -> http={(int)response.StatusCode}, success={body?.Success.ToString() ?? "null"}, status={body?.Status.ToString() ?? "null"}, payload={payload}");
            }

            AssertTimerEndpointPayloadShape(endpoint.Path, body, payload);
        }

        Assert.True(
            failures.Count == 0,
            "Curl-style runtime catalog probe detected non-compliant endpoint contracts:\n" + string.Join("\n", failures));
    }

    private static IReadOnlyList<ProbeEndpoint> BuildEndpointList(RuntimePluginRegistry registry)
    {
        var snapshot = registry.GetSnapshot();
        var endpoints = snapshot
            .Catalogs
            .OfType<Modus.Core.Plugins.IPluginOperationCatalog>()
            .SelectMany(catalog =>
            {
                var contract = catalog as Modus.Core.Plugins.IPluginContract;
                if (contract is null)
                {
                    return [];
                }

                return catalog.SupportedOperations.Select(operation => new ProbeEndpoint(
                    Path: $"/api/{contract.PluginId.Value}/{operation.Value}",
                    CorrelationId: $"curl-gate:{contract.PluginId.Value}:{operation.Value}"));
            })
            .Distinct()
            .OrderBy(static endpoint => endpoint.Path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(endpoints);
        return endpoints;
    }

    private static string ResolvePluginsPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var pluginsPath = Path.Combine(root, "plugins");
        Assert.True(Directory.Exists(pluginsPath), $"Plugins path not found: {pluginsPath}");
        return pluginsPath;
    }

    private static void AssertTimerEndpointPayloadShape(string endpointPath, PluginOperationHttpResponse? body, JsonElement payload)
    {
        if (!endpointPath.EndsWith("/Timer.WriteCurrentTime", StringComparison.Ordinal))
        {
            return;
        }

        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.True(
            payload.TryGetProperty("timestampUtcIso8601", out var timestamp)
            && !string.IsNullOrWhiteSpace(timestamp.GetString()),
            $"Timer endpoint '{endpointPath}' must return consolidated payload.timestampUtcIso8601, but payload was: {payload}");
    }

    private sealed record ProbeEndpoint(string Path, string CorrelationId);
}