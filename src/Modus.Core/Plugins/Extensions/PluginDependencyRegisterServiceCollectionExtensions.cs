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

    /// <summary>
    /// Register an interface contract as a mapping to a plugin implementation with the declared lifetime.
    /// The interface must be assignable from the implementation type.
    /// The mapping will resolve to the same DI-managed instance as the concrete plugin implementation.
    /// </summary>
    /// <typeparam name="TInterface">The service contract interface to register.</typeparam>
    /// <typeparam name="TImplementation">The plugin implementation type that implements TInterface.</typeparam>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="lifetime">The lifetime declaration for this interface mapping.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when TImplementation does not implement TInterface.</exception>
    public static IServiceCollection AddPluginServiceInterface<TInterface, TImplementation>(
        this IServiceCollection services,
        PluginServiceLifetime lifetime)
        where TInterface : class
        where TImplementation : class, TInterface
        => services.AddPluginServiceInterface(typeof(TInterface), typeof(TImplementation), lifetime);

    /// <summary>
    /// Register an interface contract as a mapping to a plugin implementation with the declared lifetime.
    /// The interface must be assignable from the implementation type.
    /// The mapping will resolve to the same DI-managed instance as the concrete plugin implementation.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="interfaceType">The service contract interface to register.</param>
    /// <param name="implementationType">The plugin implementation type that implements the interface.</param>
    /// <param name="lifetime">The lifetime declaration for this interface mapping.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="ArgumentException">Thrown when implementationType does not implement interfaceType.</exception>
    public static IServiceCollection AddPluginServiceInterface(
        this IServiceCollection services,
        Type interfaceType,
        Type implementationType,
        PluginServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(interfaceType);
        ArgumentNullException.ThrowIfNull(implementationType);

        // Validate that the implementation type is assignable to the interface type
        if (!interfaceType.IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"The implementation type '{implementationType.FullName ?? implementationType.Name}' must implement the interface '{interfaceType.FullName ?? interfaceType.Name}'.",
                nameof(implementationType));
        }

        var serviceLifetime = PluginServiceLifetimeMapping.ToServiceLifetime(lifetime);
        
        // Validate compatibility with existing registrations for the interface
        PluginServiceLifetimeMapping.ValidateCompatibleLifetime(interfaceType, services, serviceLifetime);

        // Create a descriptor using a factory that resolves the implementation type from the service provider.
        // This ensures that the interface resolves to the same instance as the concrete plugin type,
        // preserving the declared lifetime (singleton reuses same instance, scoped reuses within scope, transient creates new).
        var descriptor = ServiceDescriptor.Describe(
            interfaceType,
            provider => provider.GetRequiredService(implementationType),
            serviceLifetime);

        services.Add(descriptor);
        return services;
    }
}