using Microsoft.Extensions.DependencyInjection;

namespace Modus.Core.Plugins;

public static class PluginServiceLifetimeMapping
{
    public static ServiceLifetime ToServiceLifetime(PluginServiceLifetime lifetime)
        => lifetime switch
        {
            PluginServiceLifetime.Singleton => ServiceLifetime.Singleton,
            PluginServiceLifetime.Scoped => ServiceLifetime.Scoped,
            PluginServiceLifetime.Transient => ServiceLifetime.Transient,
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported plugin service lifetime.")
        };

    public static ServiceDescriptor CreateDescriptor(
        Type serviceType,
        Type implementationType,
        PluginServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);

        return ServiceDescriptor.Describe(serviceType, implementationType, ToServiceLifetime(lifetime));
    }

    public static bool TryResolveLifetime(
        Type serviceType,
        IEnumerable<ServiceDescriptor> descriptors,
        out ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(descriptors);

        var matchingDescriptors = descriptors
            .Where(descriptor => descriptor.ServiceType == serviceType)
            .ToArray();

        if (matchingDescriptors.Length == 0)
        {
            lifetime = default;
            return false;
        }

        lifetime = ResolveLifetime(serviceType, matchingDescriptors);
        return true;
    }

    internal static void ValidateCompatibleLifetime(
        Type serviceType,
        IEnumerable<ServiceDescriptor> descriptors,
        ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(descriptors);
        EnsureDefinedLifetime(lifetime);

        var matchingDescriptors = descriptors
            .Where(descriptor => descriptor.ServiceType == serviceType)
            .ToArray();

        if (matchingDescriptors.Length == 0)
        {
            return;
        }

        var resolvedLifetime = ResolveLifetime(serviceType, matchingDescriptors);
        if (resolvedLifetime != lifetime)
        {
            throw new InvalidOperationException($"Conflicting lifetime declarations were found for service contract '{GetServiceContractName(serviceType)}'.");
        }
    }

    private static ServiceLifetime GetEffectiveLifetime(ServiceDescriptor descriptor)
    {
        EnsureDefinedLifetime(descriptor.Lifetime);

        return descriptor.ImplementationInstance is not null
            ? ServiceLifetime.Singleton
            : descriptor.Lifetime;
    }

    private static ServiceLifetime ResolveLifetime(Type serviceType, IReadOnlyCollection<ServiceDescriptor> descriptors)
    {
        var matchedLifetimes = descriptors
            .Select(GetEffectiveLifetime)
            .Distinct()
            .OrderBy(GetDeterministicPrecedence)
            .ToArray();

        if (matchedLifetimes.Length == 1)
        {
            return matchedLifetimes[0];
        }

        throw new InvalidOperationException($"Conflicting lifetime declarations were found for service contract '{GetServiceContractName(serviceType)}'.");
    }

    private static void EnsureDefinedLifetime(ServiceLifetime lifetime)
    {
        if (Enum.IsDefined(typeof(ServiceLifetime), lifetime))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported service lifetime.");
    }

    private static string GetServiceContractName(Type serviceType)
        => serviceType.FullName ?? serviceType.Name;

    private static int GetDeterministicPrecedence(ServiceLifetime lifetime)
        => lifetime switch
        {
            ServiceLifetime.Singleton => 0,
            ServiceLifetime.Scoped => 1,
            ServiceLifetime.Transient => 2,
            _ => int.MaxValue
        };
}