using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Modus.SamplePlugins.Telemetry;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginHttpEndpointCoverageTests
{
    [Fact]
    [Trait("ChecklistItem", "Map_GivenDiscoveredPluginOperations_ExpectedOnePostRoutePerOperation")]
    public async Task Map_GivenDiscoveredPluginOperations_ExpectedOnePostRoutePerOperation_AndOpenApiContainsAllMappedPaths()
    {
        var hostTelemetry = new HostTelemetryPlugin();
        var machineTelemetry = new MachineTelemetryPlugin();
        var mapper = new PluginEndpointMapper(
            [hostTelemetry, machineTelemetry],
            [hostTelemetry, machineTelemetry]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi();

        await using var app = builder.Build();
        mapper.Map(app);
        app.MapOpenApi();
        await app.StartAsync();

        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .ToArray();

        AssertRouteExists(endpoints, "/api/Plugin.Host.Telemetry/Telemetry.Host.CollectSnapshot");
        AssertRouteExists(endpoints, "/api/Plugin.Machine.Telemetry/Telemetry.Machine.CollectSnapshot");

        var client = app.GetTestClient();
        var openApi = await client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/Plugin.Host.Telemetry/Telemetry.Host.CollectSnapshot", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/Plugin.Machine.Telemetry/Telemetry.Machine.CollectSnapshot", openApi, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "LiveEndpointVerification_GivenRunningHostApi_ExpectedEachPluginEndpointRespondsToCurl")]
    public async Task LiveEndpointVerification_GivenRunningHostApi_ExpectedEachPluginEndpointRespondsToCurl()
    {
        var repoRoot = FindRepositoryRoot();
        var pluginsRoot = CopyPluginsToTemporaryDirectory(repoRoot);
        var port = GetAvailablePort();
        var hostAssemblyPath = typeof(HostRunner).Assembly.Location;
        var hostProcess = StartHostProcess(hostAssemblyPath, pluginsRoot, port, repoRoot);

        try
        {
            await WaitForHostApiAsync(port);

            var endpoints = await GetDiscoveredPluginEndpointsAsync(port);
            Assert.NotEmpty(endpoints);

            foreach (var endpoint in endpoints)
            {
                var correlationId = $"curl-{Guid.NewGuid():N}";
                var response = await CurlPostJsonAsync(
                    port,
                    $"/api/{endpoint.PluginId}/{endpoint.Operation}",
                    $"{{\"correlationId\":\"{correlationId}\",\"payload\":\"payload\"}}");

                Assert.Equal(0, response.ExitCode);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.NotNull(response.Body);
                Assert.True(response.Body!.Success);
                Assert.Equal(SyncResponseStatus.Success, response.Body.Status);
                Assert.Equal(correlationId, response.Body.CorrelationId);
                Assert.False(string.IsNullOrWhiteSpace(response.Body.Payload));

                if (endpoint.PluginId is "Plugin.Host.Telemetry" or "Plugin.Machine.Telemetry")
                {
                    Assert.NotNull(response.Body.PayloadObject);
                    var payloadObject = Assert.IsType<JsonElement>(response.Body.PayloadObject);
                    Assert.Equal(JsonValueKind.Object, payloadObject.ValueKind);
                    Assert.True(payloadObject.TryGetProperty("pluginId", out var pluginId));
                    Assert.Equal(endpoint.PluginId, pluginId.GetString());
                    Assert.True(payloadObject.TryGetProperty("collectedAtUtc", out _));
                    Assert.True(payloadObject.TryGetProperty("measurements", out var measurements));
                    Assert.Equal(JsonValueKind.Array, measurements.ValueKind);
                    Assert.NotEmpty(measurements.EnumerateArray());
                    Assert.True(payloadObject.TryGetProperty("metadata", out var metadata));
                    Assert.Equal(JsonValueKind.Object, metadata.ValueKind);
                }
            }
        }
        finally
        {
            TryTerminate(hostProcess);
            TryDeleteDirectory(pluginsRoot);
        }
    }

    private static void AssertRouteExists(RouteEndpoint[] endpoints, string routePattern)
    {
        Assert.Contains(endpoints, endpoint =>
            string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal)
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase) == true);
    }

    private static async Task WaitForHostApiAsync(int port)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await CurlGetStatusAsync(port, "/openapi/v1.json");
            if (result.ExitCode == 0 && string.Equals(result.StdOut.Trim(), "200", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Host API on port {port} did not become ready in time.");
    }

    private static async Task<IReadOnlyList<(string PluginId, string Operation)>> GetDiscoveredPluginEndpointsAsync(int port)
    {
        var result = await RunCurlAsync(port, "/openapi/v1.json", "GET", jsonBody: null, includeResponseBody: true);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unable to fetch OpenAPI document from live host: {result.StdErr}");
        }

        var lastNewLine = result.StdOut.LastIndexOf('\n');
        if (lastNewLine < 0)
        {
            throw new InvalidOperationException($"Unable to parse OpenAPI response body from output: {result.StdOut}");
        }

        var body = result.StdOut[..lastNewLine];
        using var document = JsonDocument.Parse(body);

        var paths = document.RootElement.GetProperty("paths");
        var endpoints = new List<(string PluginId, string Operation)>();

        foreach (var pathProperty in paths.EnumerateObject())
        {
            if (!pathProperty.Name.StartsWith("/api/", StringComparison.Ordinal))
            {
                continue;
            }

            var segments = pathProperty.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 3)
            {
                continue;
            }

            endpoints.Add((segments[1], segments[2]));
        }

        return endpoints;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr, HttpStatusCode StatusCode, PluginOperationHttpResponse? Body)> CurlPostJsonAsync(
        int port,
        string path,
        string jsonBody)
    {
        var result = await RunCurlAsync(port, path, "POST", jsonBody, includeResponseBody: true);
        var statusCode = ParseHttpStatusCode(result.StdOut);
        var responseBody = ParsePluginOperationResponse(result.StdOut);
        return (result.ExitCode, result.StdOut, result.StdErr, statusCode, responseBody);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> CurlGetStatusAsync(int port, string path)
    {
        var result = await RunCurlAsync(port, path, "GET", jsonBody: null, includeResponseBody: false);
        return (result.ExitCode, result.StdOut, result.StdErr);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCurlAsync(
        int port,
        string path,
        string method,
        string? jsonBody,
        bool includeResponseBody)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("-sS");
        if (!includeResponseBody)
        {
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("NUL");
        }

        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add(includeResponseBody ? "\n%{http_code}" : "%{http_code}");
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add(method);
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Content-Type: application/json");
        if (jsonBody is not null)
        {
            startInfo.ArgumentList.Add("--data-raw");
            startInfo.ArgumentList.Add(jsonBody);
        }
        startInfo.ArgumentList.Add($"http://127.0.0.1:{port}{path}");

        using var process = Process.Start(startInfo)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdOut, stdErr);
    }

    private static HttpStatusCode ParseHttpStatusCode(string curlOutput)
    {
        var lastNewLine = curlOutput.LastIndexOf('\n');
        if (lastNewLine < 0 || !int.TryParse(curlOutput[(lastNewLine + 1)..].Trim(), out var statusCode))
        {
            throw new InvalidOperationException($"Unable to parse curl status code from output: {curlOutput}");
        }

        return (HttpStatusCode)statusCode;
    }

    private static PluginOperationHttpResponse? ParsePluginOperationResponse(string curlOutput)
    {
        var lastNewLine = curlOutput.LastIndexOf('\n');
        if (lastNewLine < 0)
        {
            throw new InvalidOperationException($"Unable to parse curl response body from output: {curlOutput}");
        }

        var body = curlOutput[..lastNewLine];
        return JsonSerializer.Deserialize<PluginOperationHttpResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Modus.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string CopyPluginsToTemporaryDirectory(string repoRoot)
    {
        var pluginSourceDir = Path.Combine(repoRoot, "plugins", "bin", "Debug", "net10.0");
        var tempDir = Path.Combine(Path.GetTempPath(), $"modus-live-curl-{Guid.NewGuid():N}");
        var tempPluginsDir = Path.Combine(tempDir, "plugins");

        Directory.CreateDirectory(tempPluginsDir);

        foreach (var file in Directory.EnumerateFiles(pluginSourceDir, "Plugin*.dll"))
        {
            File.Copy(file, Path.Combine(tempPluginsDir, Path.GetFileName(file)), overwrite: true);
        }

        var coreDll = Path.Combine(pluginSourceDir, "Modus.Core.dll");
        if (File.Exists(coreDll))
        {
            File.Copy(coreDll, Path.Combine(tempPluginsDir, "Modus.Core.dll"), overwrite: true);
        }

        return tempDir;
    }

    private static Process StartHostProcess(string hostAssemblyPath, string pluginsRoot, int port, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.ArgumentList.Add(hostAssemblyPath);
        startInfo.ArgumentList.Add(Path.Combine(pluginsRoot, "plugins"));

        return Process.Start(startInfo)!;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
