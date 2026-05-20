namespace Modus.Core.Plugins;

public sealed record PluginLoadContext(PluginId PluginId, CancellationToken CancellationToken);

public sealed record PluginStartContext(PluginId PluginId, CancellationToken CancellationToken);

public sealed record PluginStopContext(PluginId PluginId, CancellationToken CancellationToken);

public enum PluginUnloadReason
{
    GracefulShutdown,
    Reload,
    FailureRecovery,
}

public sealed record PluginUnloadContext(
    PluginId PluginId,
    PluginUnloadReason UnloadReason,
    DateTimeOffset Deadline,
    CancellationToken CancellationToken);