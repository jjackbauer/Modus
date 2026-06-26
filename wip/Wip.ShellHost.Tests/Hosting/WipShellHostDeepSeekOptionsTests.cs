using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class WipShellHostDeepSeekOptionsTests : IDisposable
{
    private const string ChecklistItem = "Implement DeepSeek provider options contract (base URL, model identifier, timeout, key source) with deterministic validation and startup diagnostics [depends on typed provider contracts]";
    private readonly List<string> _tempRoots = new();
    private readonly Dictionary<string, string?> _originalEnvironmentVariables = new(StringComparer.Ordinal);

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DeepSeekOptions_GivenMissingApiKey_ExpectedStartupConfigurationToDeferKeyResolution()
    {
        const string variableName = "WIP_DEEPSEEK_OPTIONS_TEST_MISSING";
        SetEnvironmentVariable(variableName, null);

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "DeepSeek": {
                    "BaseUrl": "https://api.deepseek.com",
                    "Model": "deepseek-chat",
                    "TimeoutSeconds": 30,
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);

        Assert.NotNull(options.EffectiveConfig.ProviderConfig);
        Assert.Equal(WipShellModelProviderKind.DeepSeek, options.EffectiveConfig.ProviderConfig!.Provider);
        Assert.Equal(variableName, options.EffectiveConfig.ProviderConfig.DeepSeek!.KeySourceReference);
        Assert.Contains(
            $"provider.deepseek.keySource=environment:{variableName}",
            options.GetStartupDiagnostics(),
            StringComparer.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DeepSeekOptions_GivenUnsupportedModelValue_ExpectedDeterministicValidationContract()
    {
        const string variableName = "WIP_DEEPSEEK_OPTIONS_TEST_KEY";
        SetEnvironmentVariable(variableName, "test-key");

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "DeepSeek": {
                    "BaseUrl": "https://api.deepseek.com",
                    "Model": "deepseek-unsupported",
                    "TimeoutSeconds": 30,
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot));

        Assert.Equal(
            "DeepSeek provider configuration is invalid: Model 'deepseek-unsupported' is not supported. Supported models: deepseek-chat, deepseek-reasoner.",
            error.Message);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void DeepSeekOptions_GivenValidConfiguration_ExpectedStartupDiagnosticsExposeEffectiveProviderSettings()
    {
        const string variableName = "WIP_DEEPSEEK_OPTIONS_TEST_DIAGNOSTIC_KEY";
        SetEnvironmentVariable(variableName, "test-key");

        var repositoryRoot = CreateRepositoryWithConfig(
            $$"""
            {
                "DeepSeek": {
                    "BaseUrl": "https://api.deepseek.com/v1",
                    "Model": "deepseek-reasoner",
                    "TimeoutSeconds": 45,
                    "ApiKeySource": "env:{{variableName}}"
                }
            }
            """);

        var options = WipShellHostOptions.FromArgs(Array.Empty<string>(), repositoryRoot);
        var diagnostics = options.GetStartupDiagnostics();

        Assert.NotNull(options.EffectiveConfig.ProviderConfig);
        Assert.Equal(WipShellModelProviderKind.DeepSeek, options.EffectiveConfig.ProviderConfig!.Provider);
        Assert.Contains("provider=deepseek", diagnostics, StringComparer.Ordinal);
        Assert.Contains("provider.deepseek.baseUrl=https://api.deepseek.com/v1", diagnostics, StringComparer.Ordinal);
        Assert.Contains("provider.deepseek.model=deepseek-reasoner", diagnostics, StringComparer.Ordinal);
        Assert.Contains("provider.deepseek.timeoutSeconds=45", diagnostics, StringComparer.Ordinal);
        Assert.Contains($"provider.deepseek.keySource=environment:{variableName}", diagnostics, StringComparer.Ordinal);
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
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"modus-wip-shellhost-deepseek-options-tests-{Guid.NewGuid():N}");
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