namespace Modus.Core.Plugins;

public sealed class PluginContractValidationPolicy
{
    public bool RequireScheduledEventsCapability { get; set; }

    public bool RequireDeterministicRegistrationLifecycle { get; set; } = true;
}