using Modus.Core.Hosting;
using Modus.Host.Plugins.Descriptors;
using Modus.Host.Plugins.Scanning;

namespace Modus.Host.Plugins.Host;


public sealed class HostRunner
{
    private readonly PluginFolderWatcher _watcher;
    private readonly PluginHostingOptions? _options;

    public HostRunner()
        : this(options: null, new PluginFolderWatcher())
    {
    }

    public HostRunner(PluginHostingOptions options)
        : this(options, new PluginFolderWatcher())
    {
    }

    internal HostRunner(PluginHostingOptions? options, PluginFolderWatcher watcher)
    {
        _options = options;
        _watcher = watcher;
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