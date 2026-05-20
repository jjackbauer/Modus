using Modus.Core.Hosting;
using Modus.Host.Plugins.Descriptors;
using Modus.Host.Plugins.Scanning;

namespace Modus.Host.Plugins.Host;


public sealed class HostRunner
{
    private readonly PluginFolderWatcher _watcher;
    private readonly PluginHostingOptions? _options;

    public HostRunner()
        : this(options: null, serviceProvider: null, watcher: null)
    {
    }

    public HostRunner(PluginHostingOptions options)
        : this(options, serviceProvider: null, watcher: null)
    {
    }

    public HostRunner(PluginHostingOptions options, IServiceProvider serviceProvider)
        : this(options, serviceProvider, watcher: null)
    {
    }

    internal HostRunner(PluginHostingOptions? options, IServiceProvider? serviceProvider, PluginFolderWatcher? watcher = null)
    {
        _options = options;
        _watcher = watcher ?? new PluginFolderWatcher(serviceProvider);
    }

    public Task<PluginWatcherStartResult> StartAsync(CancellationToken ct)
    {
        var path = _options?.Normalize(Directory.GetCurrentDirectory()).PluginsPath
            ?? Path.Combine(Directory.GetCurrentDirectory(), "plugins");
        return StartAsync(path, ct);
    }

    public Task<PluginWatcherStartResult> StartAsync(string pluginsPath, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(
                new PluginWatcherStartResult(
                    HostHealthy: false,
                    WatcherRegistered: false,
                    PluginsPath: string.Empty,
                    PluginsDirectoryExists: false,
                    Diagnostics: ["stage=startup outcome=failure reason=startup canceled"]));
        }

        var result = _watcher.Start(pluginsPath);
        return Task.FromResult(result);
    }
}