namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginOnboardingResult(
    bool HostHealthy,
    bool EventAccepted,
    bool PluginActivated,
    string? PluginId,
    IReadOnlyList<string> ActivePluginIds,
    IReadOnlyList<string> FailedPluginIds,
    IReadOnlyList<string> Diagnostics);