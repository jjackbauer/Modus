namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginSpec(string PluginId, bool IsValid, bool FailOnActivation);
