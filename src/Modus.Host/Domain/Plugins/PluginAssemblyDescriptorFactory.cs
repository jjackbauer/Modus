using System.Reflection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;

namespace Modus.Host.Plugins;

public sealed class PluginAssemblyDescriptorFactory
{
    public PluginDescriptor CreateFromAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("An assembly path is required.", nameof(assemblyPath));
        }

        if (!string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Assembly file must have .dll extension.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Assembly file not found.", assemblyPath);
        }

        AssemblyName assemblyName;
        try
        {
            assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Malformed assembly metadata.", ex);
        }

        var resolvedName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(assemblyPath);
        var pluginId = NormalizeIdentifier(resolvedName);
        var version = assemblyName.Version ?? new Version(1, 0, 0, 0);
        var fullPath = Path.GetFullPath(assemblyPath);
        var fileSize = new FileInfo(fullPath).Length;
        var dispatchMetadata = TryResolveDispatchMetadata(fullPath);

        return new PluginDescriptor(
            PluginId: new PluginId(pluginId),
            AssemblyName: resolvedName,
            Version: version,
            Capabilities: [new CapabilityName($"Cap.{pluginId}")],
            DependsOn: [],
            DeclaredOperations: dispatchMetadata.DeclaredOperations,
            AssemblyPath: fullPath,
            AssemblyFileSizeBytes: fileSize,
            RuntimePluginTypeFullName: dispatchMetadata.PluginTypeFullName,
            DeclaredServiceLifetime: dispatchMetadata.ServiceLifetime);
    }

    private static (string? PluginTypeFullName, PluginServiceLifetime? ServiceLifetime, IReadOnlyList<OperationName> DeclaredOperations) TryResolveDispatchMetadata(string assemblyPath)
    {
        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var pluginType = assembly
                .GetTypes()
                .Where(static type =>
                    type is { IsAbstract: false, IsInterface: false }
                    && typeof(IPluginContract).IsAssignableFrom(type)
                    && ImplementsSyncResponderContract(type))
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .FirstOrDefault();

            if (pluginType is null)
            {
                return (null, null, []);
            }

            return (
                pluginType.FullName,
                TryResolveDeclaredServiceLifetime(pluginType),
                TryResolveDeclaredOperations(pluginType));
        }
        catch
        {
            return (null, null, []);
        }
    }

    private static bool ImplementsSyncResponderContract(Type pluginType)
    {
        if (typeof(ISyncResponder).IsAssignableFrom(pluginType))
        {
            return true;
        }

        return pluginType
            .GetInterfaces()
            .Any(static candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(ISyncResponder<,>)
                && candidate.GenericTypeArguments[0] == typeof(SyncRequest)
                && candidate.GenericTypeArguments[1].IsGenericType
                && candidate.GenericTypeArguments[1].GetGenericTypeDefinition() == typeof(SyncResponse<>));
    }

    private static IReadOnlyList<OperationName> TryResolveDeclaredOperations(Type pluginType)
    {
        object? pluginInstance = null;

        try
        {
            pluginInstance = Activator.CreateInstance(pluginType);
            if (pluginInstance is not IPluginOperationCatalog catalog)
            {
                return [];
            }

            return catalog.SupportedOperations
                .Where(static operation => !string.IsNullOrWhiteSpace(operation.Value))
                .DistinctBy(static operation => operation.Value, StringComparer.Ordinal)
                .OrderBy(static operation => operation.Value, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
        finally
        {
            switch (pluginInstance)
            {
                case IAsyncDisposable asyncDisposable:
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private static PluginServiceLifetime? TryResolveDeclaredServiceLifetime(Type pluginType)
    {
        for (var current = pluginType; current is not null && current != typeof(object); current = current.BaseType!)
        {
            if (!current.IsGenericType)
            {
                continue;
            }

            var genericDefinition = current.GetGenericTypeDefinition();
            if (genericDefinition == typeof(SingletonPlugin<>))
            {
                return PluginServiceLifetime.Singleton;
            }

            if (genericDefinition == typeof(ScopedPlugin<>))
            {
                return PluginServiceLifetime.Scoped;
            }

            if (genericDefinition == typeof(TransientPlugin<>))
            {
                return PluginServiceLifetime.Transient;
            }
        }

        return null;
    }

    private static string NormalizeIdentifier(string value)
    {
        var normalizedChars = value
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '.' ? ch : '.')
            .ToArray();

        return string.Join(
            ".",
            new string(normalizedChars)
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
