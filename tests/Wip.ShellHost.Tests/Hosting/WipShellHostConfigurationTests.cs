using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostConfigurationTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void ConfigLoader_GivenRepositoryConfigFile_MergesDefaultsAndOverridesIntoEffectiveRuntimeConfiguration()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
              "pluginsPath": "custom-plugins",
              "workspaceRoot": "src",
              "policyId": "local-safe-custom",
                            "validationCommands": ["dotnet build", "dotnet test --no-build"],
                            "pluginStartupMode": "autoload"
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
        Assert.True(options.AutoLoadPluginsOnStartup);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, ".wip", "config.json")), options.EffectiveConfig.SourceFile);
    }

    [Fact]
    public async Task ConfigCommand_GivenLoadedConfig_DisplaysEffectivePolicyPluginPathsValidationCommandsAndSourceFile()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
              "pluginsPath": "custom-plugins",
              "workspaceRoot": "src",
              "policyId": "local-safe-custom",
                            "validationCommands": ["dotnet build", "dotnet test --no-build"],
                            "pluginStartupMode": "autoload"
            }
            """);

        using var input = new StringReader("config\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Effective configuration:", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"source: {options.EffectiveConfig.SourceFile}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"policy: {options.EffectiveConfig.PolicyId}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"pluginsPath: {options.EffectiveConfig.PluginsPath}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"pluginStartupMode: {options.EffectiveConfig.PluginStartupMode}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {options.EffectiveConfig.WorkspaceRoot}", shellOutput, StringComparison.Ordinal);
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

    private string CreateRepositoryWithConfig(string configJson)
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"modus-wip-shellhost-tests-{Guid.NewGuid():N}");
        _tempRoots.Add(repositoryRoot);

        Directory.CreateDirectory(repositoryRoot);
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".wip"));
        File.WriteAllText(Path.Combine(repositoryRoot, ".wip", "config.json"), configJson);

        return repositoryRoot;
    }
}