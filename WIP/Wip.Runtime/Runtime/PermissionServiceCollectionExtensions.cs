using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Permissions;

namespace Wip.Runtime.Runtime;

/// <summary>
/// Host wiring for WP-1.7: register the permission evaluator with the desired mode and grants.
/// Hosts that do not register one keep the secure fallback (built-ins allowed, plugins denied).
/// </summary>
public static class PermissionServiceCollectionExtensions
{
    public static IServiceCollection AddWipPermissions(
        this IServiceCollection services,
        PermissionMode mode = PermissionMode.Enforce,
        IEnumerable<string>? grants = null,
        Action<PermissionDecision>? auditSink = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPermissionEvaluator>(_ => new PermissionEvaluator(mode, grants, auditSink));
        return services;
    }
}