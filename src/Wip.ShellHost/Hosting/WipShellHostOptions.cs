using System.Text.Json;

namespace Wip.ShellHost.Hosting;

public enum WipShellPluginStartupMode
{
    ExplicitCommandOnly,
    AutoLoadPlugins,
}

public sealed record WipShellHostEffectiveConfig(
    string SourceFile,
    string PluginsPath,
    string WorkspaceRoot,
    string PolicyId,
    IReadOnlyList<string> ValidationCommands,
    WipShellPluginStartupMode PluginStartupMode = WipShellPluginStartupMode.ExplicitCommandOnly);

public sealed record WipShellHostOptions(string PluginsPath, WipShellHostEffectiveConfig EffectiveConfig)
{
    public bool AutoLoadPluginsOnStartup
        => EffectiveConfig.PluginStartupMode == WipShellPluginStartupMode.AutoLoadPlugins;

    public static WipShellHostOptions FromArgs(string[] args, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);

        var repositoryRoot = Path.GetFullPath(currentDirectory);
        var explicitPluginPath = args.FirstOrDefault(static arg => !arg.StartsWith("--", StringComparison.Ordinal));
        var explicitStartupModeValue = args
            .FirstOrDefault(static arg => arg.StartsWith("--startup-mode=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

        var defaultPluginsPath = Path.GetFullPath(Path.Combine(repositoryRoot, "plugins"));
        var defaultWorkspaceRoot = repositoryRoot;
        var effectivePluginsPath = defaultPluginsPath;
        var effectiveWorkspaceRoot = defaultWorkspaceRoot;
        var effectivePolicyId = "local-safe";
        IReadOnlyList<string> effectiveValidationCommands = ["dotnet build", "dotnet test"];
        var effectivePluginStartupMode = WipShellPluginStartupMode.ExplicitCommandOnly;
        var sourceFile = "(defaults)";

        var configPath = Path.Combine(repositoryRoot, ".wip", "config.json");
        if (File.Exists(configPath))
        {
            var rawConfig = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<RepositoryConfigFile>(rawConfig, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new RepositoryConfigFile();

            if (!string.IsNullOrWhiteSpace(config.PluginsPath))
                effectivePluginsPath = ResolvePath(repositoryRoot, config.PluginsPath);

            if (!string.IsNullOrWhiteSpace(config.WorkspaceRoot))
                effectiveWorkspaceRoot = ResolvePath(repositoryRoot, config.WorkspaceRoot);

            if (!string.IsNullOrWhiteSpace(config.PolicyId))
                effectivePolicyId = config.PolicyId.Trim();

            if (config.ValidationCommands is { Count: > 0 })
            {
                var commands = config.ValidationCommands
                    .Where(static command => !string.IsNullOrWhiteSpace(command))
                    .Select(static command => command.Trim())
                    .ToArray();

                if (commands.Length > 0)
                    effectiveValidationCommands = commands;
            }

            if (TryParsePluginStartupMode(config.PluginStartupMode, out var configuredStartupMode))
                effectivePluginStartupMode = configuredStartupMode;

            sourceFile = Path.GetFullPath(configPath);
        }

        if (!string.IsNullOrWhiteSpace(explicitPluginPath))
            effectivePluginsPath = ResolvePath(repositoryRoot, explicitPluginPath);

        if (TryParsePluginStartupMode(explicitStartupModeValue, out var explicitStartupMode))
            effectivePluginStartupMode = explicitStartupMode;

        var effectiveConfig = new WipShellHostEffectiveConfig(
            SourceFile: sourceFile,
            PluginsPath: effectivePluginsPath,
            WorkspaceRoot: effectiveWorkspaceRoot,
            PolicyId: effectivePolicyId,
            ValidationCommands: effectiveValidationCommands,
            PluginStartupMode: effectivePluginStartupMode);

        return new WipShellHostOptions(effectiveConfig.PluginsPath, effectiveConfig);
    }

    private static string ResolvePath(string repositoryRoot, string value)
    {
        var trimmed = value.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(repositoryRoot, trimmed));
    }

    private static bool TryParsePluginStartupMode(string? value, out WipShellPluginStartupMode mode)
    {
        mode = WipShellPluginStartupMode.ExplicitCommandOnly;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToLowerInvariant();
        mode = normalized switch
        {
            "explicit" or "explicit-command" or "explicit-command-only" or "manual" => WipShellPluginStartupMode.ExplicitCommandOnly,
            "autoload" or "auto-load" or "auto" => WipShellPluginStartupMode.AutoLoadPlugins,
            _ => WipShellPluginStartupMode.ExplicitCommandOnly,
        };

        return true;
    }

    private sealed record RepositoryConfigFile(
        string? PluginsPath = null,
        string? WorkspaceRoot = null,
        string? PolicyId = null,
        IReadOnlyList<string>? ValidationCommands = null,
        string? PluginStartupMode = null);
}
