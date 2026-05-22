namespace Modus.Core.Hosting;

public sealed class PluginHostingOptions : IPluginHostPortabilityContract
{
    public const string DefaultContractName = "Modus.Hosting.Portability";
    public static readonly Version DefaultContractVersion = new(1, 0);

    public string ContractName => DefaultContractName;

    public Version ContractVersion => DefaultContractVersion;

    public string PluginsPath { get; set; } = "plugins";

    public bool RunOnce { get; set; }

    public PluginHostingOptions Normalize(string basePath)
    {
        var normalizedBasePath = string.IsNullOrWhiteSpace(basePath)
            ? Directory.GetCurrentDirectory()
            : basePath.Trim();
        var configuredPath = string.IsNullOrWhiteSpace(PluginsPath)
            ? "plugins"
            : PluginsPath.Trim()
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

        var absolutePath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(normalizedBasePath, configuredPath));

        return new PluginHostingOptions
        {
            PluginsPath = absolutePath,
            RunOnce = RunOnce,
        };
    }
}