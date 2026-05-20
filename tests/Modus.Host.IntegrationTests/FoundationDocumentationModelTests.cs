using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class FoundationDocumentationModelTests
{
    [Fact]
    [Trait("ChecklistItem", "Document updated Core/Host foundation model and architectural invariants for plugin-driven modular monolith composition")]
    public void FoundationDocumentationModel_GivenUpdatedArchitecture_ExpectedBoundaryRulesAreExplicitlyDocumented()
    {
        var rootReadme = ReadRepositoryFile("README.md");
        var coreReadme = ReadRepositoryFile(Path.Combine("src", "Modus.Core", "README.md"));
        var hostReadme = ReadRepositoryFile(Path.Combine("src", "Modus.Host", "README.md"));

        Assert.Contains("## Core-Host Foundation Model", rootReadme, StringComparison.Ordinal);
        Assert.Contains("## Architectural Invariants", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Core owns contracts and extension points; it never composes runtime dependencies.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Host is the only composition root and is solely responsible for plugin discovery, validation, registration, and activation.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Modules and plugins communicate through contracts and events; direct cross-module internals access is forbidden.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Contracts are stable and versioned; plugins depend on Modus.Core contracts, never on Modus.Host internals.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("The host runtime composes dependencies, orchestrates lifecycle stages, and enforces boundary validation before activation.", hostReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Document updated Core/Host foundation model and architectural invariants for plugin-driven modular monolith composition")]
    public void FoundationDocumentationModel_GivenRuntimePipeline_ExpectedLifecycleStagesAreDocumentedInDeterministicOrder()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        var discoveryIndex = rootReadme.IndexOf("1. discovery", StringComparison.Ordinal);
        var validationIndex = rootReadme.IndexOf("2. validation", StringComparison.Ordinal);
        var loadIndex = rootReadme.IndexOf("3. load", StringComparison.Ordinal);
        var registrationIndex = rootReadme.IndexOf("4. registration", StringComparison.Ordinal);
        var activationIndex = rootReadme.IndexOf("5. activation", StringComparison.Ordinal);
        var operationIndex = rootReadme.IndexOf("6. operation", StringComparison.Ordinal);

        Assert.True(discoveryIndex >= 0, "Expected deterministic runtime stage 'discovery' to be documented.");
        Assert.True(validationIndex > discoveryIndex, "Expected 'validation' after 'discovery'.");
        Assert.True(loadIndex > validationIndex, "Expected 'load' after 'validation'.");
        Assert.True(registrationIndex > loadIndex, "Expected 'registration' after 'load'.");
        Assert.True(activationIndex > registrationIndex, "Expected 'activation' after 'registration'.");
        Assert.True(operationIndex > activationIndex, "Expected 'operation' after 'activation'.");
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
