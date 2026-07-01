using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Wip.Bones.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wip.Bones.Agents.Workflow;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostPlaywrightApplication : IAsyncLifetime
{
    private WebApplication? _app;

    public Uri ListenUri { get; private set; } = null!;

    public string DataDirectory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ListenUri = BonesHostTestNetwork.AllocateListenUrl();
        DataDirectory = BonesHostTestPaths.CreateTempDataDirectory();

        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", ListenUri.ToString());
        Environment.SetEnvironmentVariable("BonesHost__DataDirectory", DataDirectory);
        Environment.SetEnvironmentVariable("BonesHost__ListenUrl", ListenUri.ToString());
        Environment.SetEnvironmentVariable("BonesHost__IterationDelayMs", "0");
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", "stub");
        Environment.SetEnvironmentVariable("BonesHost__DefaultParameters__GameCount", "1");
        Environment.SetEnvironmentVariable("BonesHost__DefaultParameters__Seed", "4242");
        Environment.SetEnvironmentVariable("BonesHost__DefaultParameters__TargetScore", "8");
        Environment.SetEnvironmentVariable("BonesHost__DefaultParameters__LearningPlayerId", "1");

        _app = BonesHostApplication.Build(["--urls", ListenUri.ToString()]);
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(DataDirectory))
        {
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public HttpClient CreateClient() => new() { BaseAddress = ListenUri };
}

internal static class BonesHostTestPaths
{
    public static string CreateTempDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"modus-bones-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}

internal static class BonesHostTestNetwork
{
    public static Uri AllocateListenUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"http://127.0.0.1:{port}");
    }
}

internal sealed class BonesHostWebApplicationFactory : WebApplicationFactory<BonesLearningLoopHost>
{
    public string DataDirectory { get; }

    public Uri ListenUri { get; }

    private readonly Action<IDictionary<string, string?>>? _configureConfiguration;

    private readonly HttpMessageHandler? _httpMessageHandler;

    private readonly Action<IServiceCollection>? _configureTestServices;

    public BonesHostWebApplicationFactory(
        string? dataDirectory = null,
        Uri? listenUri = null,
        Action<IDictionary<string, string?>>? configureConfiguration = null,
        HttpMessageHandler? httpMessageHandler = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        DataDirectory = dataDirectory ?? BonesHostTestPaths.CreateTempDataDirectory();
        ListenUri = listenUri ?? BonesHostTestNetwork.AllocateListenUrl();
        _configureConfiguration = configureConfiguration;
        _httpMessageHandler = httpMessageHandler;
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseSetting(WebHostDefaults.ServerUrlsKey, ListenUri.ToString());

        var settings = new Dictionary<string, string?>
        {
            ["BonesHost:DataDirectory"] = DataDirectory,
            ["BonesHost:ListenUrl"] = ListenUri.ToString(),
            ["BonesHost:IterationDelayMs"] = "0",
            ["BonesHost:ModelProvider"] = "stub",
            ["BonesHost:DefaultParameters:GameCount"] = "1",
            ["BonesHost:DefaultParameters:Seed"] = "4242",
            ["BonesHost:DefaultParameters:TargetScore"] = "8",
            ["BonesHost:DefaultParameters:LearningPlayerId"] = "1",
        };

        _configureConfiguration?.Invoke(settings);

        foreach (var (key, value) in settings)
        {
            if (!string.IsNullOrWhiteSpace(value))
                builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(settings);
        });

        if (_httpMessageHandler is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_ => new HttpClient(_httpMessageHandler, disposeHandler: false)
                {
                    BaseAddress = new Uri("https://api.deepseek.com"),
                });
            });
        }

        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(DataDirectory))
        {
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        base.Dispose(disposing);
    }
}

internal static class BonesHostStatusPolling
{
    public static async Task<BonesHostStatusResponse> WaitForStatusAsync(
        HttpClient client,
        Func<BonesHostStatusResponse, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var status = await BonesHostStatusClient.GetStatusAsync(client);
                if (predicate(status))
                    return status;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Timed out waiting for bones host status predicate after {timeout}.",
            lastError);
    }
}

internal static class BonesHostTestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

internal static class BonesHostStatusClient
{
    public static async Task<BonesHostStatusResponse> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/bones/status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BonesHostStatusResponse>(BonesHostTestJson.Options)
            ?? throw new InvalidOperationException("Status payload was null.");
    }

    public static async Task<BonesHostStopResponse> StopAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/bones/stop", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BonesHostStopResponse>(BonesHostTestJson.Options)
            ?? throw new InvalidOperationException("Stop payload was null.");
    }
}

internal sealed record BonesHostStatusLearningPlayerResponse(
    int Seat,
    string ActiveStrategyId,
    string? CandidateStrategyId,
    BonesHostStatusEffectivenessResponse Effectiveness);

internal sealed record BonesHostStatusEffectivenessResponse(
    int MatchesPlayed,
    int Wins,
    int Losses,
    int CumulativeScoreDifferential);

internal sealed record BonesHostStatusResponse(
    bool IsRunning,
    long IterationCount,
    string CurrentStage,
    string? ViewerUrl,
    string HostSessionId,
    int GamesSimulated,
    int MaxGamesPerRun,
    bool ResumeFromBest,
    bool CapReached,
    BonesHostStatusLearningPlayerResponse? LearningPlayer,
    string? LearningPlayerStatus = null,
    string? LearningPlayerError = null,
    BonesHostStatusLastIterationErrorResponse? LastIterationError = null,
    string? LastPromotionOutcome = null,
    BonesHostStatusLibraryEffectivenessResponse? LibraryEffectiveness = null,
    bool AllSeatsLearning = false,
    IReadOnlyList<BonesHostStatusPerSeatStrategyResponse>? PerSeatStrategies = null,
    long CoLearningIterationsCompleted = 0,
    int StrategiesPromoted = 0,
    int StrategiesRejected = 0);

internal sealed record BonesHostStatusPerSeatStrategyResponse(
    int Seat,
    string StrategyId,
    string Kind,
    double WinRate,
    int MatchesPlayed);

internal sealed record BonesHostStatusLastIterationErrorResponse(
    string Message,
    string? Stage);

internal sealed record BonesHostStatusLibraryEffectivenessResponse(
    string StrategyId,
    int MatchesPlayed,
    int Wins,
    int Losses,
    int CumulativeScoreDifferential);

internal sealed record BonesHostStopResponse(
    bool IsRunning,
    bool StopRequested,
    long IterationCount);

internal static class BonesHostSessionStoreTestExtensions
{
    public static int GetDerivedSeed(BonesHostSessionStore store, long iteration, BonesLearningWorkflowParameters defaults, string dataDirectory)
        => store.BeginIteration(iteration, defaults, dataDirectory).DerivedSeed;
}