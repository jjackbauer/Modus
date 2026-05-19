namespace Modus.Host.Plugins;

public sealed record PluginAssemblyScanResult(
    IReadOnlyList<PluginDescriptor> Descriptors,
    IReadOnlyList<string> Diagnostics);
