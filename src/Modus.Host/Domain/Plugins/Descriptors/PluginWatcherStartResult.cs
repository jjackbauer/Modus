namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginWatcherStartResult(
    bool HostHealthy,
    bool WatcherRegistered,
    string PluginsPath,
    bool PluginsDirectoryExists,
    IReadOnlyList<string> Diagnostics);