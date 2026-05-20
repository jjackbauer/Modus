using Modus.Core.Plugins;

namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginOnboardingResult(
    bool HostHealthy,
    bool EventAccepted,
    bool PluginActivated,
    PluginId? PluginId,
    IReadOnlyList<PluginId> ActivePluginIds,
    IReadOnlyList<PluginId> FailedPluginIds,
    IReadOnlyList<string> Diagnostics);