using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Host.Hosting;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class EmbeddedHostingTests
{
    [Fact]
    public async Task EmbeddedHost_GivenServiceProviderConfiguredWithAddModusPluginHosting_ExpectedHostRunnerStartSucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-embed-start-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);
            await using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(CancellationToken.None);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddedHost_GivenConfiguredPluginsPathOverride_ExpectedWatcherUsesProvidedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-embed-path-{Guid.NewGuid():N}");
        var customPath = Path.Combine(root, "custom-plugins");
        Directory.CreateDirectory(customPath);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts => opts.PluginsPath = customPath);
            await using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(CancellationToken.None);

            Assert.Equal(Path.GetFullPath(customPath), result.PluginsPath);
            Assert.True(result.HostHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddedHost_GivenCanceledTokenBeforeStart_ExpectedUnhealthyResultWithoutThrow()
    {
        var services = new ServiceCollection();
        services.AddModusPluginHosting(opts => opts.PluginsPath = "plugins");
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<HostRunner>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await runner.StartAsync(cts.Token);

        Assert.False(result.HostHealthy);
        Assert.False(result.WatcherRegistered);
        Assert.Contains(result.Diagnostics, d => d.Contains("reason=startup canceled"));
    }

    [Fact]
    [Trait("ChecklistItem", "Portability.ExternalAppHosting.ValidInvalidPluginPaths")]
    public async Task PortableHostingIntegration_GivenValidPluginPath_ExpectedRegistrationAndStartSucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-portable-valid-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);
            await using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(CancellationToken.None);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.True(result.PluginsDirectoryExists);
            Assert.Equal(Path.GetFullPath(pluginsPath), result.PluginsPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Portability.ExternalAppHosting.ValidInvalidPluginPaths")]
    public async Task PortableHostingIntegration_GivenInvalidPluginPath_ExpectedStartReturnsFailureWithoutThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-portable-invalid-{Guid.NewGuid():N}");
        var missingPath = Path.Combine(root, "nonexistent-plugins");

        var services = new ServiceCollection();
        services.AddModusPluginHosting(opts => opts.PluginsPath = missingPath);
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<HostRunner>();

        PluginWatcherStartResult? result = null;
        var exception = await Record.ExceptionAsync(async () => result = await runner.StartAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.HostHealthy);
        Assert.True(result.WatcherRegistered);
        Assert.False(result.PluginsDirectoryExists);
        Assert.Equal(Path.GetFullPath(missingPath), result.PluginsPath);
        Assert.Contains(result.Diagnostics, d => d.Contains("plugins directory missing"));
    }

    [Fact]
    public async Task PortableHostingIntegration_GivenRunOnceConfiguration_ExpectedDeterministicStartupAndShutdown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-portable-runonce-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts =>
            {
                opts.PluginsPath = pluginsPath;
                opts.RunOnce = true;
            });
            await using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<PluginHostingOptions>();
            var runner = provider.GetRequiredService<HostRunner>();

            Assert.True(options.RunOnce);
            var result = await runner.StartAsync(CancellationToken.None);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
