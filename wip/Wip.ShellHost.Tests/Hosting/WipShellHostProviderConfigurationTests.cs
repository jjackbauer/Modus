using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostProviderConfigurationTests : IDisposable
{
    private const string ChecklistItem = "Add shell-host configuration surface for provider selection and DeepSeek defaults so effective config output proves active model provider and model name at runtime [depends on DeepSeek options contract]";
    private readonly List<string> _tempRoots = new();
    private readonly Dictionary<string, string?> _originalEnvironmentVariables = new(StringComparer.Ordinal);

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ConfigCommand_GivenModelProviderDeepSeekWithoutOverrides_DisplaysDeepSeekDefaultsInEffectiveConfiguration()
    {
        const string variableName = "DEEPSEEK_API_KEY";
        SetEnvironmentVariable(variableName, "test-key");

        var repositoryRoot = CreateRepositoryWithConfig(
            """
            {
                "ModelProvider": "deepseek"
            }
            """);

        using var input = new StringReader("effective-config\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("provider: deepseek", shellOutput, StringComparison.Ordinal);
        Assert.Contains("provider.deepseek.model: deepseek-chat", shellOutput, StringComparison.Ordinal);
        Assert.Contains("provider.deepseek.baseUrl: https://api.deepseek.com/", shellOutput, StringComparison.Ordinal);
        Assert.Contains("provider.deepseek.timeoutSeconds: 30", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"provider.deepseek.keySource: environment:{variableName}", shellOutput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ConfigCommand_GivenModelProviderDeepSeekWithoutApiKey_ExpectedHostStartupAndConfigCommandSucceed()
    {
        const string variableName = "WIP_SHELLHOST_PROVIDER_MISSING_KEY_TEST";
        SetEnvironmentVariable(variableName, null);

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "ModelProvider": "deepseek",
                "DeepSeek": {
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        using var input = new StringReader("effective-config\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("provider: deepseek", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"provider.deepseek.keySource: environment:{variableName}", shellOutput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ConfigCommand_GivenModelProviderDeepSeekWithModelOverride_DisplaysConfiguredModelInEffectiveConfiguration()
    {
        const string variableName = "WIP_SHELLHOST_PROVIDER_TEST_KEY";
        SetEnvironmentVariable(variableName, "test-key");

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "ModelProvider": "deepseek",
                "DeepSeek": {
                    "Model": "deepseek-reasoner",
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        using var input = new StringReader("effective-config\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("provider: deepseek", shellOutput, StringComparison.Ordinal);
        Assert.Contains("provider.deepseek.model: deepseek-reasoner", shellOutput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task PlanCommand_GivenModelProviderDeepSeekWithoutApiKey_ExpectedDeterministicFallbackPlanAndNoStartupBreak()
    {
        const string variableName = "WIP_SHELLHOST_PROVIDER_MISSING_KEY_PLAN_TEST";
        SetEnvironmentVariable(variableName, null);

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "ModelProvider": "deepseek",
                "DeepSeek": {
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        using var input = new StringReader("session start \"Missing key plan\"\nplan\nexit\n");
        using var output = new StringWriter();
        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        await using var host = WipShellHostFactory.CreateDefault(options, input, output);

        var exitCode = await host.RunAsync(CancellationToken.None);
        var shellOutput = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Plan generated:", shellOutput, StringComparison.Ordinal);
        Assert.Contains($"Model provider skipped: provider-credentials-missing:{variableName}", shellOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan failed:", shellOutput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ConfigLoader_GivenModelProviderNoneAndDeepSeekConfig_DoesNotActivateProvider()
    {
        const string variableName = "WIP_SHELLHOST_PROVIDER_NONE_TEST_KEY";
        SetEnvironmentVariable(variableName, "test-key");

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "ModelProvider": "none",
                "DeepSeek": {
                    "Model": "deepseek-reasoner",
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);

        Assert.Null(options.EffectiveConfig.ProviderConfig);
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironmentVariables)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);

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

    private void SetEnvironmentVariable(string name, string? value)
    {
        if (!_originalEnvironmentVariables.ContainsKey(name))
            _originalEnvironmentVariables[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable(name, value);
    }

    private string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"modus-wip-shellhost-provider-config-tests-{Guid.NewGuid():N}");
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
