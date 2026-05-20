using System.Reflection;
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

        return new PluginDescriptor(
            PluginId: new PluginId(pluginId),
            AssemblyName: resolvedName,
            Version: version,
            Capabilities: [new CapabilityName($"Cap.{pluginId}")],
            DependsOn: [],
            AssemblyPath: fullPath,
            AssemblyFileSizeBytes: fileSize);
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
