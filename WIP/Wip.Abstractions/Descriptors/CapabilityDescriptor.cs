using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Descriptors;

public interface ICapabilityDescriptor
{
    CapabilityId CapabilityId { get; }

    string DisplayName { get; }

    CapabilityVersion Version { get; }

    CapabilityKind Kind { get; }

    IReadOnlyList<CapabilityPermission> RequiredPermissions { get; }

    PluginOrigin? PluginOrigin { get; }

    Type CapabilityType { get; }

    Type RequestType { get; }

    Type ResultType { get; }
}

public interface ICapabilityDescriptor<TRequest, TResult> : ICapabilityDescriptor
    where TRequest : notnull
    where TResult : notnull
{
}

public sealed record CapabilityDescriptor<TRequest, TResult> : ICapabilityDescriptor<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    public CapabilityDescriptor(
        CapabilityId capabilityId,
        string displayName,
        CapabilityKind kind,
        Type capabilityType,
        CapabilityVersion? version = null,
        IReadOnlyList<CapabilityPermission>? requiredPermissions = null,
        PluginOrigin? pluginOrigin = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(displayName));

        ArgumentNullException.ThrowIfNull(capabilityType);

        if (typeof(TRequest) == typeof(object))
            throw new ArgumentException("Request type cannot be object.", nameof(TRequest));

        if (typeof(TResult) == typeof(object))
            throw new ArgumentException("Result type cannot be object.", nameof(TResult));

        if (!typeof(ICapability<TRequest, TResult>).IsAssignableFrom(capabilityType))
            throw new ArgumentException(
                $"{capabilityType} must implement {typeof(ICapability<TRequest, TResult>)}.",
                nameof(capabilityType));

        CapabilityId = capabilityId;
        DisplayName = displayName;
        Version = version ?? new CapabilityVersion("1.0.0");
        Kind = kind;
        RequiredPermissions = (requiredPermissions ?? []).ToArray();
        PluginOrigin = pluginOrigin;
        CapabilityType = capabilityType;
    }

    public CapabilityId CapabilityId { get; }

    public string DisplayName { get; }

    public CapabilityVersion Version { get; }

    public CapabilityKind Kind { get; }

    public IReadOnlyList<CapabilityPermission> RequiredPermissions { get; }

    public PluginOrigin? PluginOrigin { get; }

    public Type CapabilityType { get; }

    public Type RequestType => typeof(TRequest);

    public Type ResultType => typeof(TResult);
}

public static class CapabilityDescriptor
{
    public static CapabilityDescriptor<TRequest, TResult> For<TCapability, TRequest, TResult>(
        CapabilityId capabilityId,
        string displayName,
        CapabilityKind kind,
        CapabilityVersion? version = null,
        IReadOnlyList<CapabilityPermission>? requiredPermissions = null,
        PluginOrigin? pluginOrigin = null)
        where TCapability : class, ICapability<TRequest, TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        return new CapabilityDescriptor<TRequest, TResult>(
            capabilityId,
            displayName,
            kind,
            typeof(TCapability),
            version,
            requiredPermissions,
            pluginOrigin);
    }
}
