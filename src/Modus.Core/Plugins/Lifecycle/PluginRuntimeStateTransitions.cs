namespace Modus.Core.Plugins;

public static class PluginRuntimeStateTransitions
{
    private static readonly IReadOnlyDictionary<PluginRuntimeState, PluginRuntimeState[]> AllowedTransitions =
        new Dictionary<PluginRuntimeState, PluginRuntimeState[]>
        {
            [PluginRuntimeState.Discovered] = [PluginRuntimeState.Validated, PluginRuntimeState.Failed],
            [PluginRuntimeState.Validated] = [PluginRuntimeState.Loaded, PluginRuntimeState.Failed],
            [PluginRuntimeState.Loaded] = [PluginRuntimeState.Registered, PluginRuntimeState.Failed],
            [PluginRuntimeState.Registered] = [PluginRuntimeState.Activated, PluginRuntimeState.RollbackPending],
            [PluginRuntimeState.Activated] = [PluginRuntimeState.Active, PluginRuntimeState.RollbackPending],
            [PluginRuntimeState.Active] = [PluginRuntimeState.Deactivating],
            [PluginRuntimeState.Deactivating] = [PluginRuntimeState.Unloaded],
            [PluginRuntimeState.RollbackPending] = [PluginRuntimeState.Failed],
            [PluginRuntimeState.Failed] = [PluginRuntimeState.Discovered],
            [PluginRuntimeState.Unloaded] = [],
        };

    public static IReadOnlyList<PluginRuntimeState> GetAllowedNextStates(PluginRuntimeState state)
    {
        return AllowedTransitions.TryGetValue(state, out var nextStates)
            ? nextStates
            : [];
    }

    public static bool IsAllowedTransition(PluginRuntimeState from, PluginRuntimeState to)
    {
        return GetAllowedNextStates(from).Contains(to);
    }
}