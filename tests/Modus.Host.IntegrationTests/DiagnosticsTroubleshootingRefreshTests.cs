using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DiagnosticsTroubleshootingRefreshTests
{
    [Fact]
    [Trait("ChecklistItem", "Refresh diagnostics and troubleshooting documentation for discovery, validation, activation, and failure isolation flows")]
    public void DiagnosticsTroubleshootingRefresh_GivenStartupFailure_ExpectedGuideMapsSymptomsToRuntimeStage()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("## Diagnostics and Troubleshooting Guide", rootReadme, StringComparison.Ordinal);
        Assert.Contains("### Stage-to-symptom isolation map", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| Runtime stage | Typical symptom | Isolation checks |", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| discovery | Plugin project is not found or descriptor list is empty | Verify plugins path, `Plugin.*` project naming, and that artifacts are under the configured root |", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| validation | Descriptor rejected before load or capability/contract mismatch | Inspect validation diagnostics for contract compliance, operation catalog, and dependency declarations |", rootReadme, StringComparison.Ordinal);
        Assert.Contains("| activation | Plugin fails during startup hooks after registration | Inspect activation outcome and lifecycle hook diagnostics for the failing plugin identifier |", rootReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Refresh diagnostics and troubleshooting documentation for discovery, validation, activation, and failure isolation flows")]
    public void DiagnosticsTroubleshootingRefresh_GivenPluginIsolationFault_ExpectedGuidePreservesHealthyPluginContinuityModel()
    {
        var rootReadme = ReadRepositoryFile("README.md");

        Assert.Contains("### Failure isolation continuity model", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- A plugin fault is isolated to the failing plugin boundary; healthy plugins continue running.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Treat host shutdown as a last resort: fix or disable the failing plugin, then re-run deterministic startup validation.", rootReadme, StringComparison.Ordinal);
        Assert.Contains("- Confirm continuity by checking that non-failing plugins still report successful activation/operation markers.", rootReadme, StringComparison.Ordinal);
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
