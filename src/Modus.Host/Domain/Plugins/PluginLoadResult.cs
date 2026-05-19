namespace Modus.Host.Plugins;

public sealed record PluginLoadResult(bool IsLoaded, bool ThirdPartyDependencyDetected, IReadOnlyList<string> Diagnostics);
