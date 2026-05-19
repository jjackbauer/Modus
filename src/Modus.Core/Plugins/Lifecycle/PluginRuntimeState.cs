namespace Modus.Core.Plugins;

public enum PluginRuntimeState
{
    Discovered,
    Validated,
    Loaded,
    Registered,
    Activated,
    Active,
    Deactivating,
    Unloaded,
    RollbackPending,
    Failed,
}