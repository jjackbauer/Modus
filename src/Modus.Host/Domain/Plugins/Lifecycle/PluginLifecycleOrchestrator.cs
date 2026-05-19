using Modus.Core.Plugins;
using Modus.Host.Plugins.Descriptors;

namespace Modus.Host.Plugins.Lifecycle;

internal sealed record PluginHotLoadOrchestration(
    IReadOnlyList<PluginRuntimeState> Transitions,
    IReadOnlyList<string> Diagnostics,
    bool Activated,
    bool RegisterFailure,
    bool Quarantined);

internal sealed class PluginLifecycleOrchestrator
{
    public PluginHotLoadOrchestration OrchestrateHotLoad(PluginSpec spec, bool isQuarantined)
    {
        if (isQuarantined)
        {
            return new PluginHotLoadOrchestration(
                Transitions: [PluginRuntimeState.Discovered, PluginRuntimeState.Failed],
                Diagnostics:
                [
                    $"phase=discovered plugin={spec.PluginId} outcome=rejected reason=quarantined",
                    "plugin quarantined",
                ],
                Activated: false,
                RegisterFailure: false,
                Quarantined: true);
        }

        if (!spec.IsValid)
        {
            return new PluginHotLoadOrchestration(
                Transitions: [PluginRuntimeState.Discovered, PluginRuntimeState.Failed],
                Diagnostics:
                [
                    $"phase=discovered plugin={spec.PluginId} outcome=success",
                    $"phase=validated plugin={spec.PluginId} outcome=failure",
                    "validation failed",
                ],
                Activated: false,
                RegisterFailure: true,
                Quarantined: false);
        }

        if (spec.FailOnActivation)
        {
            return new PluginHotLoadOrchestration(
                Transitions:
                [
                    PluginRuntimeState.Discovered,
                    PluginRuntimeState.Validated,
                    PluginRuntimeState.Loaded,
                    PluginRuntimeState.Registered,
                    PluginRuntimeState.RollbackPending,
                    PluginRuntimeState.Failed,
                ],
                Diagnostics:
                [
                    $"phase=discovered plugin={spec.PluginId} outcome=success",
                    $"phase=validated plugin={spec.PluginId} outcome=success",
                    $"phase=loaded plugin={spec.PluginId} outcome=success",
                    $"phase=registered plugin={spec.PluginId} outcome=success",
                    $"phase=activated plugin={spec.PluginId} outcome=failure",
                    $"phase=rollbackpending plugin={spec.PluginId} outcome=success",
                    $"phase=failed plugin={spec.PluginId} outcome=success",
                    "activation failed",
                    "rollback complete",
                ],
                Activated: false,
                RegisterFailure: true,
                Quarantined: false);
        }

        return new PluginHotLoadOrchestration(
            Transitions:
            [
                PluginRuntimeState.Discovered,
                PluginRuntimeState.Validated,
                PluginRuntimeState.Loaded,
                PluginRuntimeState.Registered,
                PluginRuntimeState.Activated,
                PluginRuntimeState.Active,
            ],
            Diagnostics:
            [
                $"phase=discovered plugin={spec.PluginId} outcome=success",
                $"phase=validated plugin={spec.PluginId} outcome=success",
                $"phase=loaded plugin={spec.PluginId} outcome=success",
                $"phase=registered plugin={spec.PluginId} outcome=success",
                $"phase=activated plugin={spec.PluginId} outcome=success",
                $"phase=active plugin={spec.PluginId} outcome=success",
            ],
            Activated: true,
            RegisterFailure: false,
            Quarantined: false);
    }
}