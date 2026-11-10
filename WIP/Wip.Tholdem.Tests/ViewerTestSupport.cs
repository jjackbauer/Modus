using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Wip.Tholdem.Web;
using Xunit;
using Xunit.Sdk;

namespace Wip.Tholdem.Tests;

/// <summary>
/// An in-process Kestrel server bound to an ephemeral loopback port. The real HTTP surface of
/// <see cref="TholdemWebHost"/> is exercised; no TestServer shortcut is used.
/// </summary>
internal sealed class ViewerTestServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private ViewerTestServer(WebApplication application, HttpClient client, string baseAddress, string dataDirectory)
    {
        _application = application;
        Client = client;
        BaseAddress = baseAddress;
        DataDirectory = dataDirectory;
    }

    public HttpClient Client { get; }

    public string BaseAddress { get; }

    public string DataDirectory { get; }

    public static async Task<ViewerTestServer> StartAsync(string dataDirectory)
    {
        var application = TholdemWebHost.Create(
            new[] { "--urls", "http://127.0.0.1:0", "--data-dir", dataDirectory });
        await application.StartAsync();

        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        var baseAddress = addresses?.FirstOrDefault()
            ?? application.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report a bound address.");

        var client = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(30) };
        return new ViewerTestServer(application, client, baseAddress, dataDirectory);
    }

    public HttpClient CreateClient(bool allowAutoRedirect)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect };
        return new HttpClient(handler) { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            await _application.StopAsync();
        }
        finally
        {
            await _application.DisposeAsync();
        }
    }
}

internal static class ViewerTestSupport
{
    public static string CreateDataDirectory(string prefix)
    {
        // Scratch lives beside the compiled test assembly (the workspace test output), never %TEMP%.
        var testDirectory = Path.GetDirectoryName(typeof(TholdemWebHost).Assembly.Location)
            ?? throw new InvalidOperationException("Could not locate the compiled test assembly directory.");
        var path = Path.Combine(testDirectory, "viewer-test-temp", prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
    }

    public static async Task<JsonElement> GetJsonAsync(
        HttpClient client,
        string path,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    public static async Task<JsonElement> PostJsonAsync(
        HttpClient client,
        string path,
        object body,
        HttpStatusCode expected)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.Equal(expected, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Starts a live match with a small deterministic seed, which every viewer fixture uses on
    /// purpose. The helper opts in explicitly; the production guard is exercised by tests that post
    /// <c>/api/live</c> directly.
    /// </summary>
    public static Task<JsonElement> StartLiveAsync(
        HttpClient client,
        ulong seed,
        int hands,
        int delayMs,
        string seat0Agent,
        string seat1Agent) =>
        PostJsonAsync(
            client,
            "/api/live",
            new { seed, hands, delayMs, seat0Agent, seat1Agent, allowWeakSeed = true },
            HttpStatusCode.Created);

    public static async Task<JsonElement> WaitForSnapshotAsync(
        HttpClient client,
        string id,
        Func<JsonElement, bool> predicate,
        string description,
        int timeoutMilliseconds = 60000)
    {
        var stopwatch = Stopwatch.StartNew();
        JsonElement last = default;
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            using var response = await client.GetAsync($"/api/live/{id}");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new XunitException(
                    $"GET /api/live/{id} returned {(int)response.StatusCode} while waiting for {description}.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            last = document.RootElement.Clone();
            if (predicate(last))
            {
                return last;
            }

            await Task.Delay(5);
        }

        throw new XunitException($"Timed out waiting for {description}. Last snapshot status: {last}");
    }

    public static string[] StringArray(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(static item => item.GetString()!).ToArray();

    public static long[] LongArray(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(static item => item.GetInt64()).ToArray();

    public static int[] IntArray(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(static item => item.GetInt32()).ToArray();

    public static async Task WithServerAsync(string prefix, Func<ViewerTestServer, Task> body)
    {
        var dataDirectory = CreateDataDirectory(prefix);
        var server = await ViewerTestServer.StartAsync(dataDirectory);
        try
        {
            await body(server);
        }
        finally
        {
            await server.DisposeAsync();
            DeleteDirectoryQuietly(dataDirectory);
        }
    }
}