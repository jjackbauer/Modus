using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class MigrationDocumentationTests
{
    [Fact]
    [Trait("ChecklistItem", "Documentation.UpdateMigrationWithPluginsFolderStructureAndRationale")]
    [Trait("AuditArtifact", "iterative-implementation-modus-host-plugins-migration-structure-docs-2026-05-18")]
    public void MigrationDocs_GivenPluginsFolderRefactoring_ExpectedFolderStructureAndRationaleGuidePresent()
    {
        var content = ReadHostMigrationDocument();

        Assert.Contains("## Plugins Folder Refactoring Structure", content, StringComparison.Ordinal);
        Assert.Contains("| Subfolder | Primary Classes | Rationale |", content, StringComparison.Ordinal);
        Assert.Contains("| Host/ |", content, StringComparison.Ordinal);
        Assert.Contains("| Scanning/ |", content, StringComparison.Ordinal);
        Assert.Contains("| Descriptors/ |", content, StringComparison.Ordinal);
        Assert.Contains("| Validation/ |", content, StringComparison.Ordinal);
        Assert.Contains("| Lifecycle/ |", content, StringComparison.Ordinal);
        Assert.Contains("| Results/ |", content, StringComparison.Ordinal);
        Assert.Contains("### Navigation Guidance", content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Portability.MigrationDocumentation.ConsoleAndEmbeddedExamples")]
    public void MigrationDocs_GivenConsoleToEmbeddedScenario_ExpectedExamplesCoverEquivalentConfiguration()
    {
        var content = ReadHostMigrationDocument();

        Assert.Contains("## Migration Guide: Console Host to Embedded Host", content, StringComparison.Ordinal);
        Assert.Contains("| Console host usage | Embedded host usage |", content, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins", content, StringComparison.Ordinal);
        Assert.Contains("services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);", content, StringComparison.Ordinal);
        Assert.Contains("var runner = provider.GetRequiredService<HostRunner>();", content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Portability.MigrationDocumentation.ConsoleAndEmbeddedExamples")]
    public void MigrationDocs_GivenStartupFailureScenario_ExpectedTroubleshootingSectionDocumentsDiagnostics()
    {
        var content = ReadHostMigrationDocument();

        Assert.Contains("## Troubleshooting Startup Failures", content, StringComparison.Ordinal);
        Assert.Contains("stage=startup outcome=failure", content, StringComparison.Ordinal);
        Assert.Contains("plugins directory missing", content, StringComparison.Ordinal);
        Assert.Contains("AddModusPluginHosting", content, StringComparison.Ordinal);
    }

    private static string ReadHostMigrationDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var migrationDocPath = Path.Combine(directory.FullName, "src", "Modus.Host", "MIGRATION.md");
            if (File.Exists(solutionPath) && File.Exists(migrationDocPath))
            {
                return File.ReadAllText(migrationDocPath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx and src/Modus.Host/MIGRATION.md.");
    }
}