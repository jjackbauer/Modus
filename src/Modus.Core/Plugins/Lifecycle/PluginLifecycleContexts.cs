namespace Modus.Core.Plugins;

public sealed record PluginLoadContext(string PluginId, CancellationToken CancellationToken);

public sealed record PluginStartContext(string PluginId, CancellationToken CancellationToken);

public sealed record PluginStopContext(string PluginId, CancellationToken CancellationToken);

public enum PluginUnloadReason
{
    GracefulShutdown,
    Reload,
    FailureRecovery,
}

public sealed record PluginUnloadContext(
    string PluginId,
    PluginUnloadReason UnloadReason,
    DateTimeOffset Deadline,
    CancellationToken CancellationToken);