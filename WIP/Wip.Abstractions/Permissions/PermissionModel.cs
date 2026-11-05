using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Permissions;

/// <summary>
/// Canonical permission names (WP-1.7). Capabilities and plugins declare the permissions they
/// need; the evaluator decides whether the current mode and grants allow them.
/// </summary>
public static class PermissionCatalog
{
    public const string WorkspaceRead = "workspace.read";
    public const string WorkspaceWrite = "workspace.write";
    public const string ArtifactRead = "artifact.read";
    public const string ArtifactWrite = "artifact.write";
    public const string ModelInvoke = "model.invoke";
    public const string Network = "network";
    public const string Process = "process";
    public const string PluginLoad = "plugin.load";

    public static IReadOnlyList<string> All { get; } =
    [
        WorkspaceRead,
        WorkspaceWrite,
        ArtifactRead,
        ArtifactWrite,
        ModelInvoke,
        Network,
        Process,
        PluginLoad,
    ];
}

public enum PermissionMode
{
    /// <summary>Deny requests that are not granted.</summary>
    Enforce,

    /// <summary>Allow requests but mark and record the would-be denials.</summary>
    Audit,
}

public sealed record PermissionRequest(
    string Permission,
    CapabilityId? CapabilityId = null,
    PluginOrigin? PluginOrigin = null,
    SessionId? SessionId = null,
    bool IsBuiltIn = true);

public sealed record PermissionDecision(
    string Permission,
    bool Allowed,
    bool Audited,
    bool BuiltIn,
    string Reason);

public interface IPermissionEvaluator
{
    PermissionDecision Evaluate(PermissionRequest request);
}

public sealed class PermissionDeniedException : InvalidOperationException
{
    public PermissionDeniedException(string permission, string reason)
        : base($"Permission '{permission}' denied: {reason}")
    {
        Permission = permission;
        Reason = reason;
    }

    public string Permission { get; }

    public string Reason { get; }
}