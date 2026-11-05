using Wip.Abstractions.Capabilities;

namespace Wip.Abstractions.Permissions;

/// <summary>
/// Default permission evaluator (WP-1.7). Built-in capabilities are trusted and allowed; plugin
/// capabilities need a grant (<c>*</c>, the permission, or <c>pluginId:permission</c>). In Audit
/// mode a would-be denial is allowed but flagged so callers can record it.
/// </summary>
public sealed class PermissionEvaluator : IPermissionEvaluator
{
    private readonly PermissionMode _mode;
    private readonly HashSet<string> _grants;
    private readonly Action<PermissionDecision>? _auditSink;

    public PermissionEvaluator(
        PermissionMode mode = PermissionMode.Enforce,
        IEnumerable<string>? grants = null,
        Action<PermissionDecision>? auditSink = null)
    {
        _mode = mode;
        _grants = new HashSet<string>(grants ?? [], StringComparer.OrdinalIgnoreCase);
        _auditSink = auditSink;
    }

    public static PermissionEvaluator EnforceAllowBuiltIns { get; } = new(PermissionMode.Enforce);

    public PermissionDecision Evaluate(PermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Permission))
        {
            throw new ArgumentException("A permission name is required.", nameof(request));
        }

        if (request.IsBuiltIn && request.PluginOrigin is null)
        {
            return new PermissionDecision(request.Permission, true, false, true, "built-in capability");
        }

        var pluginId = request.PluginOrigin?.PluginId;
        var granted = _grants.Contains("*")
            || _grants.Contains(request.Permission)
            || (pluginId is not null && _grants.Contains($"{pluginId}:{request.Permission}"));

        if (granted)
        {
            return new PermissionDecision(request.Permission, true, false, false, "granted");
        }

        if (_mode == PermissionMode.Audit)
        {
            var audited = new PermissionDecision(
                request.Permission,
                Allowed: true,
                Audited: true,
                BuiltIn: false,
                Reason: pluginId is null
                    ? "audit mode: would deny ungranted permission"
                    : $"audit mode: would deny ungranted permission for plugin '{pluginId}'");
            _auditSink?.Invoke(audited);
            return audited;
        }

        var denied = new PermissionDecision(
            request.Permission,
            Allowed: false,
            Audited: false,
            BuiltIn: false,
            Reason: pluginId is null
                ? "no grant for the requested permission"
                : $"no grant for plugin '{pluginId}'");
        _auditSink?.Invoke(denied);
        return denied;
    }
}