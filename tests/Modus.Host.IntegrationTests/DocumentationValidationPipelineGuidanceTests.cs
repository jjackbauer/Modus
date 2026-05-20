using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DocumentationValidationPipelineGuidanceTests
{
    [Fact]
    [Trait("ChecklistItem", "Add documentation validation pipeline guidance covering link checks, snippet compile checks, and command verification")]
    public void DocumentationValidationPipelineGuidance_GivenChangedDocs_ExpectedLinkSnippetAndCommandChecksAreRunnable()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("## Documentation Validation Pipeline", rootReadme, StringComparison.Ordinal);
        Assert.Contains("### Local validation commands", rootReadme, StringComparison.Ordinal);
        Assert.Contains("1. Link checks", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Select-String -Path README.md,src/Modus.Core/README.md,src/Modus.Host/README.md -Pattern '\\[[^\\]]+\\]\\((?!https?://)(?!#)[^)]+\\)'", rootReadme, StringComparison.Ordinal);
        Assert.Contains("2. Snippet compile checks", rootReadme, StringComparison.Ordinal);
        Assert.Contains("dotnet build src/Modus.Host/Modus.Host.csproj -v minimal", rootReadme, StringComparison.Ordinal);
        Assert.Contains("3. Command verification", rootReadme, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build -v minimal", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Expected success signal:", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Add documentation validation pipeline guidance covering link checks, snippet compile checks, and command verification")]
    public void DocumentationValidationPipelineGuidance_GivenCiIntegration_ExpectedFailureSignalsAndFixPathAreDocumented()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("### CI integration expectations", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Trigger these checks on every PR that changes docs, snippets, or command examples.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Treat any non-zero command exit code as a docs validation failure.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Keep command output in CI logs so contributors can identify which validation gate failed.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("### Failure signals and fix path", rootReadme, StringComparison.Ordinal);
        Assert.Contains("1. Identify failing gate (links, snippet compile, or command verification) from CI output.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("2. Update the corresponding docs or snippet to match the current repository contracts and paths.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("3. Re-run local validation commands and attach passing output summary in the PR.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("4. If commands changed, update docs and requirements checklist transition proof together.", rootReadme, StringComparison.Ordinal);
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