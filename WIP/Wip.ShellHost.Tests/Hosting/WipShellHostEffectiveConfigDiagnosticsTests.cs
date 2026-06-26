using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostEffectiveConfigDiagnosticsTests : IDisposable
{
    private const string ChecklistItem = "Add shell-host effective-config output coverage for .NET workflow settings (validation command catalog, provider diagnostics, repository/workspace roots) with deterministic startup diagnostics [depends on bootstrap and command contract]";
    private readonly List<string> _tempRoots = new();

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void FromArgs_GivenRepositoryConfigValidationCommands_ExpectedEffectiveConfigExposesDotNetCommandCatalog()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
                "WorkspaceRoot": "src",
                "ValidationCommands": ["dotnet build", "dotnet test --no-build"],
                "ModelProvider": "none"
            }
            """);

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);

        Assert.Equal(2, options.EffectiveConfig.ValidationCommands.Count);
        Assert.Equal("dotnet build", options.EffectiveConfig.ValidationCommands[0]);
        Assert.Equal("dotnet test --no-build", options.EffectiveConfig.ValidationCommands[1]);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, "src")), options.EffectiveConfig.WorkspaceRoot);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void GetStartupDiagnostics_GivenProviderAndDotNetSettings_ExpectedDiagnosticsContainDeterministicConfigurationFacts()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
                "WorkspaceRoot": "src/workspace",
                "ValidationCommands": ["dotnet restore", "dotnet build", "dotnet test --no-build"],
                "ModelProvider": "deepseek",
                "DeepSeek": {
                    "Model": "deepseek-reasoner",
                    "ApiKeySource": "env:WIP_SHELLHOST_EFFECTIVE_CONFIG_TEST_KEY"
                }
            }
            """);

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        var diagnostics = options.GetStartupDiagnostics();

        Assert.Contains($"repository.path={repositoryRoot}", diagnostics, StringComparer.Ordinal);
        Assert.Contains($"workspace.root={Path.GetFullPath(Path.Combine(repositoryRoot, "src", "workspace"))}", diagnostics, StringComparer.Ordinal);
        Assert.Contains("validation.commands=dotnet restore|dotnet build|dotnet test --no-build", diagnostics, StringComparer.Ordinal);
        Assert.Contains("provider=deepseek", diagnostics, StringComparer.Ordinal);
        Assert.Contains("provider.deepseek.model=deepseek-reasoner", diagnostics, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ConfigCommand_GivenHostRunning_ExpectedEffectiveConfigOutputMatchesStartupDiagnosticContracts()
    {
        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
                "WorkspaceRoot": "src/workspace",
                "ValidationCommands": ["dotnet restore", "dotnet build", "dotnet test --no-build"],
                "ModelProvider": "deepseek",
                "DeepSeek": {
                    "Model": "deepseek-reasoner",
                    "ApiKeySource": "env:WIP_SHELLHOST_EFFECTIVE_CONFIG_COMMAND_KEY"
                }
            }
            """);

        using var input = new StringReader("effective-config\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        var startupDiagnostics = options.GetStartupDiagnostics();
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains($"repositoryPath: {options.EffectiveConfig.RepositoryPath}", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {options.EffectiveConfig.WorkspaceRoot}", shellOutput, StringComparison.Ordinal);
        Assert.Contains("validationCommands: dotnet restore | dotnet build | dotnet test --no-build", shellOutput, StringComparison.Ordinal);
        Assert.Contains("provider: deepseek", shellOutput, StringComparison.Ordinal);
        Assert.Contains("provider.deepseek.model: deepseek-reasoner", shellOutput, StringComparison.Ordinal);

        Assert.Contains($"repository.path={options.EffectiveConfig.RepositoryPath}", startupDiagnostics, StringComparer.Ordinal);
        Assert.Contains($"workspace.root={options.EffectiveConfig.WorkspaceRoot}", startupDiagnostics, StringComparer.Ordinal);
        Assert.Contains("validation.commands=dotnet restore|dotnet build|dotnet test --no-build", startupDiagnostics, StringComparer.Ordinal);
        Assert.Contains("provider=deepseek", startupDiagnostics, StringComparer.Ordinal);
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
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"modus-wip-shellhost-effective-config-tests-{Guid.NewGuid():N}");
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
