using System.Text.Json;

namespace Wip.ShellHost.Hosting;

public enum WipShellModelProviderKind
{
    DeepSeek,
}

public enum WipShellProviderKeySourceKind
{
    EnvironmentVariable,
}

public sealed record WipShellDeepSeekProviderOptions(
    string BaseUrl,
    string ModelIdentifier,
    TimeSpan Timeout,
    WipShellProviderKeySourceKind KeySource,
    string KeySourceReference);

public sealed record WipShellHostProviderConfig(
    WipShellModelProviderKind Provider,
    WipShellDeepSeekProviderOptions? DeepSeek);

public enum WipShellPluginStartupMode
{
    ExplicitCommandOnly,
    AutoLoadPlugins,
}

public enum WipShellHostConfigSource
{
    Defaults,
    Disk,
}

public sealed record WipShellHostEffectiveConfig(
    string SourceFile,
    string PluginsPath,
    string WorkspaceRoot,
    string PolicyId,
    IReadOnlyList<string> ValidationCommands,
    WipShellPluginStartupMode PluginStartupMode = WipShellPluginStartupMode.ExplicitCommandOnly,
    string RepositoryPath = "",
    string ConfigPath = "",
    WipShellHostConfigSource ConfigSource = WipShellHostConfigSource.Defaults,
    string WipRoot = "",
    string SessionsPath = "",
    string WorktreesPath = "",
    string UserPluginsPath = "",
    string DefaultWorkflowId = "workflow.linear",
    WipShellHostProviderConfig? ProviderConfig = null);

public sealed record WipShellHostOptions(string PluginsPath, WipShellHostEffectiveConfig EffectiveConfig)
{
    private static readonly HashSet<string> SupportedDeepSeekModels = new(StringComparer.Ordinal)
    {
        "deepseek-chat",
        "deepseek-reasoner",
    };

    public bool AutoLoadPluginsOnStartup
        => EffectiveConfig.PluginStartupMode == WipShellPluginStartupMode.AutoLoadPlugins;

    public IReadOnlyList<string> GetStartupDiagnostics()
    {
        var validationCatalog = EffectiveConfig.ValidationCommands.Count == 0
            ? "(none)"
            : string.Join("|", EffectiveConfig.ValidationCommands);

        var diagnostics = new List<string>
        {
            $"config.source={EffectiveConfig.ConfigSource.ToString().ToLowerInvariant()}",
            $"repository.path={EffectiveConfig.RepositoryPath}",
            $"workspace.root={EffectiveConfig.WorkspaceRoot}",
            $"config.path={EffectiveConfig.ConfigPath}",
            $"wip.root={EffectiveConfig.WipRoot}",
            $"sessions.path={EffectiveConfig.SessionsPath}",
            $"worktrees.path={EffectiveConfig.WorktreesPath}",
            $"plugins.path={EffectiveConfig.PluginsPath}",
            $"userPlugins.path={EffectiveConfig.UserPluginsPath}",
            $"workflow.default={EffectiveConfig.DefaultWorkflowId}",
            $"policy.id={EffectiveConfig.PolicyId}",
            $"plugin.startupMode={EffectiveConfig.PluginStartupMode}",
            $"validation.commands={validationCatalog}",
        };

        var provider = EffectiveConfig.ProviderConfig;
        if (provider is null)
        {
            diagnostics.Add("provider=none");
            return diagnostics;
        }

        diagnostics.Add($"provider={provider.Provider.ToString().ToLowerInvariant()}");

        if (provider.Provider == WipShellModelProviderKind.DeepSeek && provider.DeepSeek is { } deepSeek)
        {
            diagnostics.Add($"provider.deepseek.baseUrl={deepSeek.BaseUrl}");
            diagnostics.Add($"provider.deepseek.model={deepSeek.ModelIdentifier}");
            diagnostics.Add($"provider.deepseek.timeoutSeconds={(int)deepSeek.Timeout.TotalSeconds}");
            diagnostics.Add($"provider.deepseek.keySource=environment:{deepSeek.KeySourceReference}");
        }

        return diagnostics;
    }

    public static WipShellHostOptions FromArgs(string[] args, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);

        var repositoryRoot = Path.GetFullPath(currentDirectory);
        var wipRoot = Path.Combine(repositoryRoot, ".wip");
        var configPath = Path.Combine(wipRoot, "config.json");
        var userPluginsPath = Path.Combine(ResolveUserProfilePath(), ".wip", "plugins");
        var explicitPluginPath = args.FirstOrDefault(static arg => !arg.StartsWith("--", StringComparison.Ordinal));
        var explicitStartupModeValue = args
            .FirstOrDefault(static arg => arg.StartsWith("--startup-mode=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

        var defaultPluginsPath = Path.GetFullPath(Path.Combine(wipRoot, "plugins"));
        var defaultWorkspaceRoot = repositoryRoot;
        var defaultSessionsPath = Path.GetFullPath(Path.Combine(wipRoot, "sessions"));
        var defaultWorktreesPath = Path.GetFullPath(Path.Combine(wipRoot, "worktrees"));
        var effectivePluginsPath = defaultPluginsPath;
        var effectiveWorkspaceRoot = defaultWorkspaceRoot;
        var effectivePolicyId = "local-safe";
        var effectiveWipRoot = Path.GetFullPath(wipRoot);
        var effectiveSessionsPath = defaultSessionsPath;
        var effectiveWorktreesPath = defaultWorktreesPath;
        var effectiveDefaultWorkflowId = "workflow.linear";
        IReadOnlyList<string> effectiveValidationCommands = ["dotnet build", "dotnet test"];
        var effectivePluginStartupMode = WipShellPluginStartupMode.ExplicitCommandOnly;
        WipShellHostProviderConfig? effectiveProviderConfig = null;
        var sourceFile = "(defaults)";
        var configSource = WipShellHostConfigSource.Defaults;

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

            if (!string.IsNullOrWhiteSpace(config.WipRoot))
                effectiveWipRoot = ResolvePath(repositoryRoot, config.WipRoot);

            if (!string.IsNullOrWhiteSpace(config.SessionsPath))
                effectiveSessionsPath = ResolvePath(repositoryRoot, config.SessionsPath);

            if (!string.IsNullOrWhiteSpace(config.WorktreesPath))
                effectiveWorktreesPath = ResolvePath(repositoryRoot, config.WorktreesPath);

            if (!string.IsNullOrWhiteSpace(config.PolicyId))
                effectivePolicyId = config.PolicyId.Trim();

            if (!string.IsNullOrWhiteSpace(config.DefaultWorkflowId))
                effectiveDefaultWorkflowId = config.DefaultWorkflowId.Trim();

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

            effectiveProviderConfig = BuildProviderConfig(config);

            sourceFile = Path.GetFullPath(configPath);
            configSource = WipShellHostConfigSource.Disk;
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
            PluginStartupMode: effectivePluginStartupMode,
            RepositoryPath: repositoryRoot,
            ConfigPath: Path.GetFullPath(configPath),
            ConfigSource: configSource,
            WipRoot: effectiveWipRoot,
            SessionsPath: effectiveSessionsPath,
            WorktreesPath: effectiveWorktreesPath,
            UserPluginsPath: Path.GetFullPath(userPluginsPath),
            DefaultWorkflowId: effectiveDefaultWorkflowId,
            ProviderConfig: effectiveProviderConfig);

        return new WipShellHostOptions(effectiveConfig.PluginsPath, effectiveConfig);
    }

    private static string ResolveUserProfilePath()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("USERPROFILE"),
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(".");
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

    private static WipShellHostProviderConfig BuildDeepSeekProviderConfig(DeepSeekProviderConfigFile config)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.deepseek.com"
            : config.BaseUrl.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
            || (parsedBaseUrl.Scheme != Uri.UriSchemeHttps && parsedBaseUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        var model = string.IsNullOrWhiteSpace(config.Model)
            ? "deepseek-chat"
            : config.Model.Trim();

        if (!SupportedDeepSeekModels.Contains(model))
        {
            throw new InvalidOperationException(
                $"DeepSeek provider configuration is invalid: Model '{model}' is not supported. Supported models: deepseek-chat, deepseek-reasoner.");
        }

        var timeoutSeconds = config.TimeoutSeconds ?? 30;
        if (timeoutSeconds is < 1 or > 600)
        {
            throw new InvalidOperationException(
                $"DeepSeek provider configuration is invalid: TimeoutSeconds '{timeoutSeconds}' must be between 1 and 600.");
        }

        var keySourceValue = string.IsNullOrWhiteSpace(config.ApiKeySource)
            ? "env:DEEPSEEK_API_KEY"
            : config.ApiKeySource.Trim();

        if (!TryParseEnvironmentKeySource(keySourceValue, out var keyVariableName))
        {
            throw new InvalidOperationException(
                "DeepSeek provider configuration is invalid: ApiKeySource must use 'env:<VARIABLE_NAME>' format.");
        }

        return new WipShellHostProviderConfig(
            Provider: WipShellModelProviderKind.DeepSeek,
            DeepSeek: new WipShellDeepSeekProviderOptions(
                BaseUrl: parsedBaseUrl.ToString(),
                ModelIdentifier: model,
                Timeout: TimeSpan.FromSeconds(timeoutSeconds),
                KeySource: WipShellProviderKeySourceKind.EnvironmentVariable,
                KeySourceReference: keyVariableName));
    }

    private static WipShellHostProviderConfig? BuildProviderConfig(RepositoryConfigFile config)
    {
        if (!string.IsNullOrWhiteSpace(config.ModelProvider))
        {
            var normalizedProvider = config.ModelProvider.Trim().ToLowerInvariant();
            return normalizedProvider switch
            {
                "none" => null,
                "deepseek" => BuildDeepSeekProviderConfig(config.DeepSeek ?? new DeepSeekProviderConfigFile()),
                _ => throw new InvalidOperationException(
                    "Shell host provider configuration is invalid: ModelProvider must be 'none' or 'deepseek'."),
            };
        }

        return config.DeepSeek is null
            ? null
            : BuildDeepSeekProviderConfig(config.DeepSeek);
    }

    private static bool TryParseEnvironmentKeySource(string value, out string variableName)
    {
        variableName = string.Empty;
        if (!value.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return false;

        var parsed = value[4..].Trim();
        if (string.IsNullOrWhiteSpace(parsed))
            return false;

        variableName = parsed;
        return true;
    }

    private sealed record RepositoryConfigFile(
        string? PluginsPath = null,
        string? WorkspaceRoot = null,
        string? WipRoot = null,
        string? SessionsPath = null,
        string? WorktreesPath = null,
        string? PolicyId = null,
        IReadOnlyList<string>? ValidationCommands = null,
        string? PluginStartupMode = null,
        string? DefaultWorkflowId = null,
        string? ModelProvider = null,
        DeepSeekProviderConfigFile? DeepSeek = null);

    private sealed record DeepSeekProviderConfigFile(
        string? BaseUrl = null,
        string? Model = null,
        int? TimeoutSeconds = null,
        string? ApiKeySource = null);
}
