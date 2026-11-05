using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Permissions;

namespace Wip.Runtime.Runtime;

/// <summary>
/// WP-1.7: single evaluation point for capability permissions. Used by the workflow stage
/// dispatcher and the runtime tool gateway so both paths enforce the same catalog and mode.
/// </summary>
internal static class PermissionGuard
{
    public static void Ensure(
        IServiceProvider serviceProvider,
        ICapabilityDescriptor capabilityDescriptor,
        SessionId? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(capabilityDescriptor);

        if (capabilityDescriptor.RequiredPermissions.Count == 0)
        {
            return;
        }

        var evaluator = serviceProvider.GetService(typeof(IPermissionEvaluator)) as IPermissionEvaluator
            ?? PermissionEvaluator.EnforceAllowBuiltIns;

        foreach (var permission in capabilityDescriptor.RequiredPermissions)
        {
            var decision = evaluator.Evaluate(new PermissionRequest(
                permission.Value,
                capabilityDescriptor.CapabilityId,
                capabilityDescriptor.PluginOrigin,
                sessionId,
                IsBuiltIn: capabilityDescriptor.PluginOrigin is null));

            if (!decision.Allowed)
            {
                throw new PermissionDeniedException(permission.Value, decision.Reason);
            }
        }
    }
}