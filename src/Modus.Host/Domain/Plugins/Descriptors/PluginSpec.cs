using Modus.Core.Plugins;

namespace Modus.Host.Plugins.Descriptors;

public sealed record PluginSpec(PluginId PluginId, bool IsValid, bool FailOnActivation);
