using Xunit;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Engine;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;

namespace Wip.Bones.Web.Tests;

public sealed class BonesPlaywrightHost : IAsyncLifetime
{
    private WebApplication? _app;

    public InMemoryBonesMatchCatalog Catalog { get; } = new();

    public Uri ListeningUri { get; private set; } = null!;

    public IServiceProvider Services => _app?.Services
        ?? throw new InvalidOperationException("Playwright host has not started.");

    public async Task InitializeAsync()
    {
        ListeningUri = CreateListeningUri();
        var contentRoot = Path.Combine(BonesWebTestPaths.FindRepositoryRoot(), "WIP", "Wip.Bones.Web");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
        });
        builder.WebHost.UseUrls(ListeningUri.ToString());
        builder.Services.AddSingleton<BonesGameEngine>();
        builder.Services.AddSingleton<BonesBoardVisualLayoutBuilder>();
        builder.Services.AddSingleton<BonesMatchViewerService>();
        builder.Services.AddSingleton<IBonesMatchCatalog>(Catalog);
        builder.Services.AddSingleton<IBonesPublicMatchFeed, BonesCatalogPublicMatchFeed>();

        _app = builder.Build();
        BonesWebHost.MapMatchRoutes(_app);
        BonesWebHost.MapStaticViewer(_app);
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    public HttpClient CreateListeningClient() => new()
    {
        BaseAddress = ListeningUri,
    };

    private static Uri CreateListeningUri()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"http://127.0.0.1:{port}");
    }
}