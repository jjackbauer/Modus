using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
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
    /// <summary>
    /// Registers <typeparamref name="TProvider"/> and exposes it through a permission-guarded
    /// <see cref="IModelProvider{TRequestPayload,TResultPayload}"/> wrapper. Pass the plugin origin
    /// for providers that come from a plugin; built-ins leave it null.
    /// </summary>
    public static IServiceCollection AddPermissionGuardedModelProvider<TRequestPayload, TResultPayload, TProvider>(
        this IServiceCollection services,
        PluginOrigin? pluginOrigin = null)
        where TRequestPayload : notnull
        where TResultPayload : notnull
        where TProvider : class, IModelProvider<TRequestPayload, TResultPayload>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TProvider>();
        services.AddSingleton<IModelProvider<TRequestPayload, TResultPayload>>(serviceProvider =>
            new PermissionGuardedModelProvider<TRequestPayload, TResultPayload>(
                serviceProvider.GetRequiredService<TProvider>(),
                serviceProvider.GetRequiredService<IPermissionEvaluator>(),
                pluginOrigin));
        return services;
    }
}