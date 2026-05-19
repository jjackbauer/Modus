namespace Modus.Core.Plugins;

public enum PluginRegistrationStepKind
{
    RegisterOperation,
    SubscribeEvents,
    RegisterSchedules,
}

public sealed record PluginRegistrationStep(int Sequence, PluginRegistrationStepKind Kind, string Name);