using Microsoft.Extensions.DependencyInjection;

namespace Modus.Core.Plugins;

public abstract class PluginBase : IPluginContract, IPluginLifecycle, IPluginOperationCatalog, IPluginScheduledEvents
{
    public virtual PluginId PluginId => new PluginId(GetType().FullName ?? GetType().Name);

    public virtual ContractName ContractName => new ContractName("Modus.PluginContract");

    public virtual Version ContractVersion => new(1, 0, 0);

    public virtual IReadOnlyCollection<OperationName> SupportedOperations => Array.Empty<OperationName>();

    public virtual void Load(PluginLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public virtual void Start(PluginStartContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public virtual void Stop(PluginStopContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public virtual void Unload(PluginUnloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public virtual void RegisterSchedules(IPluginScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
    }
}

/// <summary>
/// Generic base class for plugins with explicit lifetime declaration.
/// Use the concrete lifetime-specific derived classes: SingletonPlugin, ScopedPlugin, or TransientPlugin.
/// </summary>
/// <typeparam name="TPluginImpl">The concrete plugin implementation type.</typeparam>
public abstract class PluginBase<TPluginImpl> : PluginBase, IPluginDependencyRegister
    where TPluginImpl : class
{
    /// <summary>
    /// Declares the service lifetime for this plugin.
    /// Override in derived classes to specify Singleton, Scoped, or Transient.
    /// </summary>
    protected abstract PluginServiceLifetime DeclaredServiceLifetime { get; }

    /// <summary>
    /// Registers the plugin instance and any additional services with the declared lifetime.
    /// </summary>
    public virtual void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RegisterPluginServices(services);
    }

    /// <summary>
    /// Override this method to register additional plugin services beyond the plugin instance itself.
    /// </summary>
    protected virtual void RegisterPluginServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the concrete plugin implementation with the declared lifetime
        services.AddPluginService<TPluginImpl, TPluginImpl>(DeclaredServiceLifetime);
    }
}

/// <summary>
/// Base class for plugins that register with Singleton lifetime.
/// </summary>
public abstract class SingletonPlugin<TPluginImpl> : PluginBase<TPluginImpl>
    where TPluginImpl : class
{
    protected override PluginServiceLifetime DeclaredServiceLifetime => PluginServiceLifetime.Singleton;
}

/// <summary>
/// Base class for plugins that register with Scoped lifetime.
/// </summary>
public abstract class ScopedPlugin<TPluginImpl> : PluginBase<TPluginImpl>
    where TPluginImpl : class
{
    protected override PluginServiceLifetime DeclaredServiceLifetime => PluginServiceLifetime.Scoped;
}

/// <summary>
/// Base class for plugins that register with Transient lifetime.
/// </summary>
public abstract class TransientPlugin<TPluginImpl> : PluginBase<TPluginImpl>
    where TPluginImpl : class
{
    protected override PluginServiceLifetime DeclaredServiceLifetime => PluginServiceLifetime.Transient;
}
