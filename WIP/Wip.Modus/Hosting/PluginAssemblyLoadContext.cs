using System.Reflection;
using System.Runtime.Loader;

namespace Wip.Modus.Hosting;

/// <summary>
/// Collectible load context for one plugin assembly (WP-2.7). Dependencies that ship beside the
/// plugin resolve inside the context, so two plugins may carry conflicting versions; host
/// contracts and framework assemblies always resolve from the default context so type identity is
/// preserved.
/// </summary>
public sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly string[] SharedAssemblyPrefixes =
    [
        "Wip.",
        "Modus.",
        "Microsoft.",
        "System.",
        "netstandard",
        "mscorlib",
        "WindowsBase",
    ];

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginAssemblyPath;

    public PluginAssemblyLoadContext(string pluginAssemblyPath)
        : base(
            $"wip-plugin:{Path.GetFileNameWithoutExtension(pluginAssemblyPath)}:{Guid.NewGuid():N}",
            isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
        _pluginAssemblyPath = Path.GetFullPath(pluginAssemblyPath);
        _resolver = new AssemblyDependencyResolver(_pluginAssemblyPath);
    }

    public string PluginAssemblyPath => _pluginAssemblyPath;

    public Assembly LoadPluginAssembly()
    {
        // Read through a stream so the plugin DLL is not mapped/locked while it is loaded;
        // unload can then release it deterministically without waiting on a file handle.
        using var stream = File.OpenRead(_pluginAssemblyPath);
        return LoadFromStream(stream);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        if (IsSharedAssembly(assemblyName.Name))
        {
            // null delegates to the default context: contract and framework types stay unified.
            return null;
        }

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolvedPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(resolvedPath);
    }

    private static bool IsSharedAssembly(string? assemblyName)
        => assemblyName is not null
           && SharedAssemblyPrefixes.Any(prefix =>
               assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}