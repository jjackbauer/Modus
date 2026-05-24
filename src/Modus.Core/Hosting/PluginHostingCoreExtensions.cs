using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modus.Core.Plugins;

namespace Modus.Core.Hosting;

public static class PluginHostingCoreExtensions
{
    private static readonly PluginDiDescriptorPolicy DescriptorPolicy = new();

    public static IServiceCollection AddPluginHostingCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PluginHostingOptions>();
        services.TryAddSingleton<IPluginHostPortabilityContract>(
            static provider => provider.GetRequiredService<PluginHostingOptions>());

        return services;
    }

    public static IServiceCollection AddDiscoveredPlugins(this IServiceCollection services, IEnumerable<Assembly> pluginAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pluginAssemblies);

        var state = RegistrationState.FromServices(services);
        var diagnostics = GetOrCreateDiagnostics(services);
        var registrarTypes = DiscoverPluginRegistrarTypes(pluginAssemblies);

        foreach (var registrarType in registrarTypes)
        {
            if (state.HasProcessedRegistrar(registrarType))
            {
                continue;
            }

            var typeName = registrarType.FullName ?? registrarType.Name;

            IPluginDependencyRegister? registrar;
            try
            {
                registrar = Activator.CreateInstance(registrarType, nonPublic: true) as IPluginDependencyRegister;
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                diagnostics.RecordFailure(typeName, typeName, innerMessage);
                state.MarkRegistrarAsProcessed(registrarType, services);
                continue;
            }

            if (registrar is null)
            {
                state.MarkRegistrarAsProcessed(registrarType, services);
                continue;
            }

            var pluginId = NormalizePluginId(((IPluginContract)registrar).PluginId.Value, registrarType);

            if (state.HasWinningPluginId(pluginId))
            {
                diagnostics.RecordSkipped(typeName, pluginId, $"Plugin identity '{pluginId}' already claimed by another registrar");
                state.MarkRegistrarAsProcessed(registrarType, services);
                continue;
            }

            // First, call the plugin's own Register method to register the concrete instance
            // and any plugin-specific services it needs.
            var registrationBuffer = new ServiceCollection();
            registrar.Register(registrationBuffer);

            foreach (var descriptor in registrationBuffer)
            {
                if (state.HasWinningCapability(descriptor))
                {
                    continue;
                }

                services.Add(descriptor);
                state.MarkCapabilityAsWinning(descriptor);
            }

            if (TryDetermineCapabilityLifetime(registrarType, registrationBuffer, services, out var capabilityLifetime))
            {
                // Then, add the capability contract descriptors that resolve through the concrete plugin
                // registration while preserving its declared lifetime semantics.
                foreach (var descriptor in DescriptorPolicy.BuildDescriptors(registrarType, capabilityLifetime))
                {
                    if (state.HasWinningCapability(descriptor))
                    {
                        continue;
                    }

                    services.Add(descriptor);
                    state.MarkCapabilityAsWinning(descriptor);
                }
            }

            state.MarkPluginIdAsWinning(pluginId, services);
            state.MarkRegistrarAsProcessed(registrarType, services);
            diagnostics.RecordSuccess(typeName, pluginId, capabilityLifetime);
        }

        return services;
    }

    private static bool TryDetermineCapabilityLifetime(
        Type registrarType,
        IServiceCollection registrationBuffer,
        IServiceCollection destinationServices,
        out ServiceLifetime lifetime)
    {
        if (PluginServiceLifetimeMapping.TryResolveLifetime(registrarType, registrationBuffer, out var bufferedLifetime))
        {
            lifetime = bufferedLifetime;
            return true;
        }

        if (PluginServiceLifetimeMapping.TryResolveLifetime(registrarType, destinationServices, out var destinationLifetime))
        {
            lifetime = destinationLifetime;
            return true;
        }

        lifetime = default;
        return false;
    }

    private static PluginDiRegistrationDiagnostics GetOrCreateDiagnostics(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(PluginDiRegistrationDiagnostics)
                && descriptor.ImplementationInstance is PluginDiRegistrationDiagnostics existing)
            {
                return existing;
            }
        }

        var diagnostics = new PluginDiRegistrationDiagnostics();
        services.AddSingleton(diagnostics);
        return diagnostics;
    }

    private static string NormalizePluginId(string? pluginId, Type registrarType)
    {
        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            return pluginId.Trim();
        }

        return registrarType.FullName ?? registrarType.Name;
    }

    private static IReadOnlyList<Type> DiscoverPluginRegistrarTypes(IEnumerable<Assembly> pluginAssemblies)
    {
        return pluginAssemblies
            .OfType<Assembly>()
            .SelectMany(GetLoadableTypes)
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false, ContainsGenericParameters: false }
                && typeof(IPluginDependencyRegister).IsAssignableFrom(type)
                && typeof(IPluginContract).IsAssignableFrom(type)
                && type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null) is not null)
            .OrderBy(static type => type.Assembly.FullName, StringComparer.Ordinal)
            .ThenBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
    }

    private sealed class RegistrarAppliedMarker<TRegistrar>
    {
    }

    private sealed record PluginIdentityAppliedMarker(string PluginId);

    private sealed class RegistrationState
    {
        private readonly HashSet<Type> _processedRegistrars;
        private readonly HashSet<string> _winningPluginIds;
        private readonly HashSet<CapabilityRegistrationKey> _winningCapabilities;

        private RegistrationState(
            HashSet<Type> processedRegistrars,
            HashSet<string> winningPluginIds,
            HashSet<CapabilityRegistrationKey> winningCapabilities)
        {
            _processedRegistrars = processedRegistrars;
            _winningPluginIds = winningPluginIds;
            _winningCapabilities = winningCapabilities;
        }

        public static RegistrationState FromServices(IServiceCollection services)
        {
            var processedRegistrars = new HashSet<Type>();
            var winningPluginIds = new HashSet<string>(StringComparer.Ordinal);
            var winningCapabilities = new HashSet<CapabilityRegistrationKey>();

            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(RegistrarAppliedMarker<>))
                {
                    processedRegistrars.Add(descriptor.ServiceType.GenericTypeArguments[0]);
                }

                if (descriptor.ServiceType == typeof(PluginIdentityAppliedMarker)
                    && descriptor.ImplementationInstance is PluginIdentityAppliedMarker marker)
                {
                    winningPluginIds.Add(marker.PluginId);
                }

                winningCapabilities.Add(CapabilityRegistrationKey.Create(descriptor));
            }

            return new RegistrationState(processedRegistrars, winningPluginIds, winningCapabilities);
        }

        public bool HasProcessedRegistrar(Type registrarType)
            => _processedRegistrars.Contains(registrarType);

        public bool HasWinningPluginId(string pluginId)
            => _winningPluginIds.Contains(pluginId);

        public bool HasWinningCapability(ServiceDescriptor descriptor)
            => _winningCapabilities.Contains(CapabilityRegistrationKey.Create(descriptor));

        public void MarkRegistrarAsProcessed(Type registrarType, IServiceCollection services)
        {
            if (!_processedRegistrars.Add(registrarType))
            {
                return;
            }

            var markerType = typeof(RegistrarAppliedMarker<>).MakeGenericType(registrarType);
            services.AddSingleton(markerType, Activator.CreateInstance(markerType, nonPublic: true)!);
        }

        public void MarkPluginIdAsWinning(string pluginId, IServiceCollection services)
        {
            if (!_winningPluginIds.Add(pluginId))
            {
                return;
            }

            services.AddSingleton(typeof(PluginIdentityAppliedMarker), new PluginIdentityAppliedMarker(pluginId));
        }

        public void MarkCapabilityAsWinning(ServiceDescriptor descriptor)
            => _winningCapabilities.Add(CapabilityRegistrationKey.Create(descriptor));
    }

    private readonly record struct CapabilityRegistrationKey(Type ServiceType, string ImplementationIdentity)
    {
        public static CapabilityRegistrationKey Create(ServiceDescriptor descriptor)
            => new(descriptor.ServiceType, GetImplementationIdentity(descriptor));

        private static string GetImplementationIdentity(ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationType is not null)
            {
                return descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name;
            }

            if (descriptor.ImplementationInstance is not null)
            {
                var instanceType = descriptor.ImplementationInstance.GetType();
                return instanceType.FullName ?? instanceType.Name;
            }

            if (descriptor.ImplementationFactory is not null)
            {
                var method = descriptor.ImplementationFactory.Method;
                var declaringType = method.DeclaringType?.FullName ?? "<factory>";
                var capturedPluginTypes = descriptor.ImplementationFactory.Target?
                    .GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(static field => field.FieldType == typeof(Type))
                    .Select(field => field.GetValue(descriptor.ImplementationFactory.Target) as Type)
                    .Where(static type => type is not null)
                    .Select(static type => type!.FullName ?? type.Name)
                    .OrderBy(static typeName => typeName, StringComparer.Ordinal)
                    .ToArray();

                return capturedPluginTypes is { Length: > 0 }
                    ? $"{declaringType}.{method.Name}[{string.Join("|", capturedPluginTypes)}]"
                    : $"{declaringType}.{method.Name}";
            }

            return "<unknown>";
        }
    }
}
