using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Workspaces;

namespace Wip.Abstractions.Capabilities;

public readonly record struct CapabilityVersion
{
    public CapabilityVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct CapabilityPermission
{
    public CapabilityPermission(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record PluginOrigin
{
    public PluginOrigin(string pluginId, CapabilityVersion version, string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(pluginId));

        if (string.IsNullOrWhiteSpace(assemblyName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(assemblyName));

        PluginId = pluginId;
        Version = version;
        AssemblyName = assemblyName;
    }

    public string PluginId { get; }

    public CapabilityVersion Version { get; }

    public string AssemblyName { get; }
}

public sealed record CapabilityMetadata
{
    public CapabilityMetadata(
        CapabilityId capabilityId,
        string displayName,
        CapabilityKind kind,
        Type capabilityType,
        CapabilityVersion version,
        IReadOnlyList<CapabilityPermission> permissions,
        PluginOrigin? pluginOrigin = null,
        WorkspaceDescriptor? workspace = null,
        ArtifactDescriptor? artifact = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(displayName));

        ArgumentNullException.ThrowIfNull(capabilityType);
        ArgumentNullException.ThrowIfNull(permissions);

        CapabilityId = capabilityId;
        DisplayName = displayName;
        Kind = kind;
        CapabilityType = capabilityType;
        Version = version;
        Permissions = permissions.ToArray();
        PluginOrigin = pluginOrigin;
        Workspace = workspace;
        Artifact = artifact;
    }

    public CapabilityId CapabilityId { get; }

    public string DisplayName { get; }

    public CapabilityKind Kind { get; }

    public Type CapabilityType { get; }

    public CapabilityVersion Version { get; }

    public IReadOnlyList<CapabilityPermission> Permissions { get; }

    public PluginOrigin? PluginOrigin { get; }

    public WorkspaceDescriptor? Workspace { get; }

    public ArtifactDescriptor? Artifact { get; }
}