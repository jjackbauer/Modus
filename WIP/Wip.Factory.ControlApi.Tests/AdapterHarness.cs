using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Wip.Factory.ControlApi;
using Wip.Factory.ControlApi.Services;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.ControlApi.Tests;

/// <summary>
/// A real Kestrel adapter process for one temporary factory control root and target repository. Tests use the
/// production composition root, not a mocked handler, so a passing test exercises the same adapter the Rust
/// control plane calls.
/// </summary>
public sealed class AdapterHarness : IAsyncDisposable
{
    public const string Token = "adapter-test-token-0123456789abcdef";
    public const string Operator = "integration-operator";
    public const string Viewer = "integration-viewer";
    public const string Administrator = "integration-admin";

    private readonly WebApplication _app;
    private readonly string _workspace;

    private AdapterHarness(WebApplication app, HttpClient client, string workspace, string repository, string controlRoot)
    {
        _app = app;
        Client = client;
        _workspace = workspace;
        Repository = repository;
        ControlRoot = controlRoot;
    }

    public HttpClient Client { get; }
    public string Repository { get; }
    public string ControlRoot { get; }

    public static async Task<AdapterHarness> StartAsync(
        ImplementationMode mode = ImplementationMode.None,
        bool executionEnabled = false)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "wip-adapter-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(workspace, "target");
        var controlRoot = Path.Combine(workspace, "control");
        Directory.CreateDirectory(repository);

        await RunAsync(repository, "git", "init", "-b", "main");
        await RunAsync(repository, "git", "config", "user.email", "factory@example.test");
        await RunAsync(repository, "git", "config", "user.name", "Factory");
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# adapter integration target\n");
        await File.WriteAllTextAsync(Path.Combine(repository, ".gitignore"), "bin/\nobj/\n");
        await RunAsync(repository, "git", "add", ".");
        await RunAsync(repository, "git", "commit", "-m", "chore: baseline");

        var (root, error) = FactoryControlRoot.Resolve(repository, controlRoot);
        if (root is null)
        {
            throw new InvalidOperationException(error);
        }

        var options = new ControlApiOptions
        {
            Factory = root,
            Token = Token,
            Implementation = mode,
            ExecutionEnabled = executionEnabled,
            AdapterVersion = "1.0.0-test",
            BindAddress = "127.0.0.1:0",
            MaxServeSeconds = 60,
            MaxDiffBytes = 512 * 1024,
            MaxFileBytes = 256 * 1024,
            MaxEventDataLength = 4000,
            AuditDirectory = Path.Combine(root.Root, "control-api"),
            ModelBaseUrl = null,
            ModelApiKey = null
        };

        var app = ControlApiApp.Build(options);
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.FirstOrDefault()
                      ?? throw new InvalidOperationException("adapter did not report a listening address");
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(3) };
        return new AdapterHarness(app, client, workspace, repository, root.Root);
    }

    public async Task<HttpResponseMessage> GetAsync(string path, string role = "operator", string actor = Operator)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeaders(request, role, actor);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> GetStreamAsync(string path, string role = "operator", string actor = Operator)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeaders(request, role, actor);
        return await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        string idempotencyKey,
        string role = "operator",
        string actor = Operator)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddHeaders(request, role, actor);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path, string idempotencyKey, string role = "operator", string actor = Operator)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        AddHeaders(request, role, actor);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await Client.SendAsync(request);
    }

    public static void AddHeaders(HttpRequestMessage request, string role, string actor)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Add("X-Control-Plane-Role", role);
        request.Headers.Add("X-Control-Plane-Actor", Uri.EscapeDataString(actor));
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    public static async Task<string> TextAsync(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync();

    public async Task<JsonDocument> SubmitAsync(string requirement, string contract, string key = "submit-0001")
    {
        var response = await SubmitRequestAsync(requirement, contract, key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    public async Task<HttpResponseMessage> SubmitRequestAsync(string requirement, string contract, string key = "submit-0001")
    {
        var body = new
        {
            requirement,
            acceptanceContractJson = contract,
            planning = new { priority = "normal", tags = new[] { "integration" }, links = Array.Empty<string>(), notes = "adapter integration" },
            explicitlyConfirmed = true
        };
        return await PostAsync("/v1/work-items", body, key);
    }

    public static string Requirement => """
        ---
        title: Adapter lifecycle health endpoint
        supportedClass: aspnetcore-http-net10
        ---

        Create a small service with an observable health endpoint.

        ## Acceptance expectations
        - GET /health returns HTTP 200 with status ok.
        """;

    public static string Contract => """
        {
          "schemaVersion": 1,
          "supportedClass": "aspnetcore-http-net10",
          "scenarios": [
            { "id": "AC-1", "method": "GET", "path": "/health",
              "expect": { "status": 200, "jsonSubset": { "status": "ok" } } }
          ]
        }
        """;

    public async Task<JsonDocument> DispatchAndWaitAsync(string key, int timeoutSeconds = 120)
    {
        var response = await PostAsync("/v1/dispatch", new { maxPasses = 16 }, key);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = await JsonAsync(response);
        var jobId = body.RootElement.GetProperty("job").GetProperty("jobId").GetString()!;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(500);
            using var statusResponse = await GetAsync("/v1/jobs/" + jobId);
            using var job = await JsonAsync(statusResponse);
            var state = job.RootElement.GetProperty("state").GetString();
            if (state is not "running")
            {
                return JsonDocument.Parse(job.RootElement.GetRawText());
            }
        }

        throw new TimeoutException("dispatch job did not finish");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            await _app.StopAsync();
        }
        finally
        {
            await _app.DisposeAsync();
        }

        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task RunAsync(string workingDirectory, string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("could not start " + fileName);
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} failed: {stdout}\n{stderr}");
        }
    }
}
