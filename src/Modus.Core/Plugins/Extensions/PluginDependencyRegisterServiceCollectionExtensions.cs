using Microsoft.Extensions.DependencyInjection;

namespace Modus.Core.Plugins;

public static class PluginDependencyRegisterServiceCollectionExtensions
{
    public static IServiceCollection AddPluginService<TService, TImplementation>(
        this IServiceCollection services,
        PluginServiceLifetime lifetime)
        where TService : class
        where TImplementation : class, TService
        => services.AddPluginService(typeof(TService), typeof(TImplementation), lifetime);

    public static IServiceCollection AddPluginService(
        this IServiceCollection services,
        Type serviceType,
        Type implementationType,
        PluginServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);

        var serviceLifetime = PluginServiceLifetimeMapping.ToServiceLifetime(lifetime);
        PluginServiceLifetimeMapping.ValidateCompatibleLifetime(serviceType, services, serviceLifetime);

        var descriptor = ServiceDescriptor.Describe(serviceType, implementationType, serviceLifetime);

        services.Add(descriptor);
        return services;
    }

    /// <summary>
    /// Register a plugin service using a factory delegate with explicit lifetime declaration.
    /// </summary>
    public static IServiceCollection AddPluginService<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> factory,
        PluginServiceLifetime lifetime)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        var serviceLifetime = PluginServiceLifetimeMapping.ToServiceLifetime(lifetime);
        PluginServiceLifetimeMapping.ValidateCompatibleLifetime(typeof(TService), services, serviceLifetime);

        var descriptor = ServiceDescriptor.Describe(typeof(TService), factory, serviceLifetime);

        services.Add(descriptor);
        return services;
    }

    /// <summary>
    /// Register a plugin service instance with explicit Singleton lifetime declaration.
    /// </summary>
    public static IServiceCollection AddPluginServiceInstance<TService>(
        this IServiceCollection services,
        TService instance)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(instance);

        // Instance registration is always singleton
        var descriptor = ServiceDescriptor.Singleton(typeof(TService), instance);
        services.Add(descriptor);
        return services;
    }
}