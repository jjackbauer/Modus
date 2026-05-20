using Modus.Host.Plugins.Descriptors;
using PluginAssemblyDescriptorFactory = Modus.Host.Plugins.PluginAssemblyDescriptorFactory;
using PluginAssemblyScanResult = Modus.Host.Plugins.PluginAssemblyScanResult;

namespace Modus.Host.Plugins.Scanning;

public sealed class PluginLoader
{
    private readonly PluginAssemblyDescriptorFactory _descriptorFactory;

    public PluginLoader()
        : this(new PluginAssemblyDescriptorFactory())
    {
    }

    internal PluginLoader(PluginAssemblyDescriptorFactory descriptorFactory)
    {
        _descriptorFactory = descriptorFactory;
    }

    public PluginAssemblyScanResult ScanRuntimeAssemblies(string pluginsPath)
    {
        if (string.IsNullOrWhiteSpace(pluginsPath))
        {
            return new PluginAssemblyScanResult([], ["stage=scan outcome=ignored reason=plugins path missing"]);
        }

        var fullRootPath = Path.GetFullPath(pluginsPath);
        if (!Directory.Exists(fullRootPath))
        {
            return new PluginAssemblyScanResult([], [$"stage=scan outcome=ignored reason=plugins directory missing path={fullRootPath}"]);
        }

        var diagnostics = new List<string>();
        var descriptorsByPluginId = new Dictionary<string, PluginDescriptor>(StringComparer.Ordinal);
        var sequence = 0;

        foreach (var assemblyPath in Directory
            .EnumerateFiles(fullRootPath, "*.dll", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            sequence++;
            var fullAssemblyPath = Path.GetFullPath(assemblyPath);

            try
            {
                var descriptor = _descriptorFactory.CreateFromAssembly(fullAssemblyPath);
                if (!descriptorsByPluginId.TryAdd(descriptor.PluginId.Value, descriptor))
                {
                    diagnostics.Add($"stage=scan sequence={sequence:D4} outcome=ignored reason=duplicate plugin id plugin={descriptor.PluginId} path={fullAssemblyPath}");
                    continue;
                }

                diagnostics.Add($"stage=scan sequence={sequence:D4} outcome=success plugin={descriptor.PluginId} assembly={fullAssemblyPath}");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"stage=scan sequence={sequence:D4} outcome=failure path={fullAssemblyPath} reason={ex.Message}");
            }
        }

        var descriptors = descriptorsByPluginId
            .Values
            .OrderBy(x => x.PluginId.Value, StringComparer.Ordinal)
            .ToArray();

        return new PluginAssemblyScanResult(descriptors, diagnostics);
    }
}
