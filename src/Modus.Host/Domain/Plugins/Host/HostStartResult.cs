namespace Modus.Host.Plugins.Host;

public sealed record HostStartResult(
    bool Started,
    List<string> ActivatedPluginIds,
    List<string> FailedPluginIds,
    Dictionary<string, string> CapabilityOwners,
    List<string> Diagnostics);
