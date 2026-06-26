using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostConfigurationTests : IDisposable
{
    private const string ChecklistItem = "Align shell-host configuration and startup defaults with .wip/config.json, .wip/plugins, ~/.wip/plugins, and deterministic effective config rendering [depends on shell-host baseline]";
    private readonly List<string> _tempRoots = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ConfigLoader_GivenNoRepositoryConfigFile_UsesWipPluginAndUserPluginDefaults()
    {
        var repositoryRoot = CreateRepositoryRoot();

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "plugins")), options.PluginsPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "config.json")), options.EffectiveConfig.ConfigPath);
        Assert.Equal(WipShellHostConfigSource.Defaults, options.EffectiveConfig.ConfigSource);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip")), options.EffectiveConfig.WipRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "sessions")), options.EffectiveConfig.SessionsPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "worktrees")), options.EffectiveConfig.WorktreesPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wip", "plugins")), options.EffectiveConfig.UserPluginsPath);
        Assert.Equal("workflow.linear", options.EffectiveConfig.DefaultWorkflowId);
        Assert.Equal("(defaults)", options.EffectiveConfig.SourceFile);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ConfigLoader_GivenRepositoryConfigFile_MergesDefaultsAndOverridesIntoEffectiveRuntimeConfiguration()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
                            "PluginsPath": "custom-plugins",
                            "WorkspaceRoot": "src",
                            "PolicyId": "local-safe-custom",
                            "ValidationCommands": ["dotnet build", "dotnet test --no-build"],
                            "PluginStartupMode": "autoload"
            }
            """);

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, "custom-plugins")), options.PluginsPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, "src")), options.EffectiveConfig.WorkspaceRoot);
        Assert.Equal("local-safe-custom", options.EffectiveConfig.PolicyId);
        Assert.Equal(2, options.EffectiveConfig.ValidationCommands.Count);
        Assert.Equal("dotnet build", options.EffectiveConfig.ValidationCommands[0]);
        Assert.Equal("dotnet test --no-build", options.EffectiveConfig.ValidationCommands[1]);
        Assert.Equal(WipShellPluginStartupMode.AutoLoadPlugins, options.EffectiveConfig.PluginStartupMode);
        Assert.Equal(WipShellHostConfigSource.Disk, options.EffectiveConfig.ConfigSource);
        Assert.True(options.AutoLoadPluginsOnStartup);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "config.json")), options.EffectiveConfig.ConfigPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip")), options.EffectiveConfig.WipRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "sessions")), options.EffectiveConfig.SessionsPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "worktrees")), options.EffectiveConfig.WorktreesPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wip", "plugins")), options.EffectiveConfig.UserPluginsPath);
        Assert.Equal("workflow.linear", options.EffectiveConfig.DefaultWorkflowId);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "config.json")), options.EffectiveConfig.SourceFile);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ConfigCommand_GivenLoadedConfig_DisplaysEffectivePolicyPluginPathsValidationCommandsAndSourceFile()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
                            "PluginsPath": "custom-plugins",
                            "WorkspaceRoot": "src",
                            "PolicyId": "local-safe-custom",
                            "ValidationCommands": ["dotnet build", "dotnet test --no-build"],
                            "PluginStartupMode": "autoload"
            }
            """);

        using var input = new StringReader("effective-config\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Effective configuration:", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"repositoryPath: {options.EffectiveConfig.RepositoryPath}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {options.EffectiveConfig.WorkspaceRoot}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"configPath: {options.EffectiveConfig.SourceFile}", shellOutput, StringComparison.Ordinal);
        Assert.Contains("configSource: disk", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"wipRoot: {Path.Combine(options.EffectiveConfig.RepositoryPath, ".wip")}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"sessionsPath: {Path.Combine(options.EffectiveConfig.RepositoryPath, ".wip", "sessions")}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"worktreesPath: {Path.Combine(options.EffectiveConfig.RepositoryPath, ".wip", "worktrees")}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"pluginsPath: {options.EffectiveConfig.PluginsPath}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"userPluginsPath: {options.EffectiveConfig.UserPluginsPath}", shellOutput, StringComparison.Ordinal);
        Assert.Contains("defaultWorkflowId: workflow.linear", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"policy: {options.EffectiveConfig.PolicyId}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"pluginStartupMode: {options.EffectiveConfig.PluginStartupMode}", shellOutput, StringComparison.Ordinal);
        Assert.Contains("validationCommands: dotnet build | dotnet test --no-build", shellOutput, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"modus-wip-shellhost-tests-{Guid.NewGuid():N}");
        _tempRoots.Add(repositoryRoot);

        Directory.CreateDirectory(repositoryRoot);

        return repositoryRoot;
    }

    private string CreateRepositoryWithConfig(string configJson)
    {
        var repositoryRoot = CreateRepositoryRoot();
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".wip"));
        File.WriteAllText(Path.Combine(repositoryRoot, ".wip", "config.json"), configJson);

        return repositoryRoot;
    }
}