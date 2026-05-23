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
    [Trait("ChecklistItem", "Replace per-plugin startup route expansion with one stable dynamic POST /api/{pluginId}/{operation} endpoint [mandatory - live endpoint resolve]")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-live-endpoint-resolve-transition-proof-2026-05-21")]
    public async Task PluginEndpointMapper_GivenHostStartup_ExpectedCatchAllPluginRouteMappedOnce()
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
            .Where(static endpoint => string.Equals(endpoint.RoutePattern.RawText, "/api/{pluginId}/{operation}", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(endpoints);
        Assert.True(
            endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase) == true,
            "Expected the stable plugin endpoint to accept POST requests.");

        var client = app.GetTestClient();
        var openApi = await client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/{pluginId}/{operation}", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/Plugin.Host.Telemetry/Telemetry.Host.CollectSnapshot", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/Plugin.Machine.Telemetry/Telemetry.Machine.CollectSnapshot", openApi, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Replace per-plugin startup route expansion with one stable dynamic POST /api/{pluginId}/{operation} endpoint [mandatory - live endpoint resolve]")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-live-endpoint-resolve-transition-proof-2026-05-21")]
    public async Task PluginEndpointMapper_GivenMultiplePluginOperations_ExpectedNoRouteTableRemapRequired()
    {
        var hostTelemetry = new HostTelemetryPlugin();
        var machineTelemetry = new MachineTelemetryPlugin();
        var mapper = new PluginEndpointMapper(
            [hostTelemetry, machineTelemetry],
            [hostTelemetry, machineTelemetry]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Single(endpoints);
        Assert.Equal("/api/{pluginId}/{operation}", endpoints[0].RoutePattern.RawText);
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

            var endpoints = GetKnownPluginEndpoints();

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

    [Fact]
    [Trait("ChecklistItem", "Replace deprecated WithOpenApi endpoint decoration usage with supported OpenAPI mapping primitives for all affected endpoint mappers [depends on API endpoint mapping refactor]")]
    public async Task OpenApiDocument_GivenMappedHostEndpoints_ExpectedManagementAndPluginRoutesPublished()
    {
        var repoRoot = FindRepositoryRoot();
        var pluginsRoot = CopyPluginsToTemporaryDirectory(repoRoot);
        var port = GetAvailablePort();
        var hostAssemblyPath = typeof(HostRunner).Assembly.Location;
        var hostProcess = StartHostProcess(hostAssemblyPath, pluginsRoot, port, repoRoot);

        try
        {
            await WaitForHostApiAsync(port);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var openApi = await client.GetStringAsync("/openapi/v1.json");

            Assert.Contains("/api/{pluginId}/{operation}", openApi, StringComparison.Ordinal);
            Assert.Contains("/management/status", openApi, StringComparison.Ordinal);
            Assert.Contains("/management/plugins/capabilities", openApi, StringComparison.Ordinal);
            Assert.Contains("/management/telemetry/host", openApi, StringComparison.Ordinal);
            Assert.Contains("/management/telemetry/machine", openApi, StringComparison.Ordinal);
            Assert.Contains("/management/plugins/uploads", openApi, StringComparison.Ordinal);
            Assert.Contains("/management/plugins/uploads/{operationId}", openApi, StringComparison.Ordinal);
        }
        finally
        {
            TryTerminate(hostProcess);
            TryDeleteDirectory(pluginsRoot);
        }
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

    private static IReadOnlyList<(string PluginId, string Operation)> GetKnownPluginEndpoints()
    {
        return
        [
            ("Plugin.Host.Telemetry", "Telemetry.Host.CollectSnapshot"),
            ("Plugin.Machine.Telemetry", "Telemetry.Machine.CollectSnapshot"),
            ("Plugin.Lifetime.Scoped", "Lifetime.Scoped.PrintId"),
            ("Plugin.Lifetime.Singleton", "Lifetime.Singleton.PrintId"),
            ("Plugin.Lifetime.Transient", "Lifetime.Transient.PrintId")
        ];
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
            FileName = OperatingSystem.IsWindows() ? "curl.exe" : "curl",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("-sS");
        if (!includeResponseBody)
        {
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
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
        var pluginSourceDir = ResolvePluginOutputDirectory(repoRoot);
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

    private static string ResolvePluginOutputDirectory(string repoRoot)
    {
        var debugPath = Path.Combine(repoRoot, "plugins", "bin", "Debug", "net10.0");
        if (Directory.Exists(debugPath))
        {
            return debugPath;
        }

        var releasePath = Path.Combine(repoRoot, "plugins", "bin", "Release", "net10.0");
        if (Directory.Exists(releasePath))
        {
            return releasePath;
        }

        throw new DirectoryNotFoundException(
            $"Could not find plugin binaries. Checked '{debugPath}' and '{releasePath}'.");
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
