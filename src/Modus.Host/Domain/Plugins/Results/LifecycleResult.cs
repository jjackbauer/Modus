using Modus.Core.Plugins;

namespace Modus.Host.Plugins.Results;

public sealed record LifecycleResult(
    IReadOnlyList<PluginRuntimeState> Transitions,
    IReadOnlyList<string> Diagnostics,
    bool HostHealthy,
    bool Quarantined = false);