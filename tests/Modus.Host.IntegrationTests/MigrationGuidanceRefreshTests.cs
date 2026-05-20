using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class MigrationGuidanceRefreshTests
{
    [Fact]
    [Trait("ChecklistItem", "Add migration notes for foundation/API changes with explicit before/after guidance and compatibility caveats")]
    public void MigrationGuidanceRefresh_GivenFoundationApiChanges_ExpectedBeforeAfterExamplesForEachBreakingChange()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("## Migration Notes: Foundation and API Changes", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| Before (legacy) | After (current) |", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| Plugin reaches into host internals for DI wiring | Plugin dependency registration only through `IPluginDependencyRegister` and host-managed composition |", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| Plugin operation identifiers are raw strings scattered across implementation | Operations exposed via deterministic `IPluginOperationCatalog` with stable `OperationName` values |", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| Scheduled behavior is ad hoc timer code inside plugin startup | Scheduled work declared via `IPluginScheduledEvents.RegisterSchedules(IPluginScheduler)` with explicit recurring jobs |", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Add migration notes for foundation/API changes with explicit before/after guidance and compatibility caveats")]
    public void MigrationGuidanceRefresh_GivenCompatibilityConcerns_ExpectedKnownRisksAndFallbackPathsAreDocumented()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("### Compatibility Caveats and Fallback Paths", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Caveat: Legacy plugins that depend on host internals can fail validation during startup.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Fallback: migrate dependency wiring into plugin contracts and registrars, then rerun host validation.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Caveat: Non-deterministic operation naming can break diagnostics comparability.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Fallback: normalize operation names in plugin catalogs and keep names stable across releases.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Caveat: Ad hoc timers may not map to host-observable lifecycle stages.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("Fallback: move recurring work to `RegisterSchedules` and validate with run-once startup output markers.", rootReadme, StringComparison.Ordinal);
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