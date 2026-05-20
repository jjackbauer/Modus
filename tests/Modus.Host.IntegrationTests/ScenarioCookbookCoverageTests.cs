using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class ScenarioCookbookCoverageTests
{
    [Fact]
    [Trait("ChecklistItem", "Create scenario cookbook entries for common plugin and host integration use-cases")]
    public void ScenarioCookbookCoverage_GivenPluginAuthoringScenario_ExpectedRecipeMapsToCurrentContractsAndLifecycle()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("## Scenario Cookbook", rootReadme, StringComparison.Ordinal);
        Assert.Contains("### Recipe 1: Standard plugin contract plus deterministic lifecycle", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Use `SingletonPlugin<TSelf>`, `ScopedPlugin<TSelf>`, or `TransientPlugin<TSelf>` as the base class for declared service lifetime.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Define `PluginId`, `ContractName`, and `ContractVersion` with stable values before activation.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Declare supported operations through `SupportedOperations` and keep names deterministic.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Implement lifecycle hooks in deterministic order: `Load`, `Start`, `Stop`, `Unload`.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Register plugin services via `RegisterPluginServices(IServiceCollection services)` and `AddPluginServiceInterface<TService, TImplementation>(DeclaredServiceLifetime)`.", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Create scenario cookbook entries for common plugin and host integration use-cases")]
    public void ScenarioCookbookCoverage_GivenHostIntegrationScenario_ExpectedRecipeIncludesDeterministicValidationAndDiagnostics()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("### Recipe 2: Host integration with deterministic validation and diagnostics", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Start with `dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins --run-once` to verify startup deterministically.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Treat runtime stages as the diagnostic spine: discovery -> validation -> load -> registration -> activation -> operation.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Validate expected output markers: `stage=di outcome=success`, `stage=discovery outcome=success`, `stage=validation outcome=success`, `stage=activation outcome=success`.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- If one plugin fails activation, confirm healthy plugins continue and isolate remediation to the failing descriptor or contract.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Re-run `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build -v minimal` after docs or sample command changes.", rootReadme, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var filePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(solutionPath) && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing Modus.slnx and {relativePath}.");
    }
}