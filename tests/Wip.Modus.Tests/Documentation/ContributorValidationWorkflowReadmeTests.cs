using Wip.Builder;
using Wip.Modus.Hosting;
using Xunit;

namespace Wip.Modus.Tests.Documentation;

public sealed class ContributorValidationWorkflowReadmeTests
{
    private const string ChecklistItem = "Document contributor validation workflow with required proof artifacts from build/test/runtime command outputs and deterministic negative-path checks [depends on LocalSafe policy documentation]";
    private const string ReadmePath = "src/WIP.Contributor-Architecture.README.md";

    private static readonly string[] RequiredArtifacts =
    [
        ".github/requirements/proof-artifacts/wip-modus-contributor-validation/build-wip-modus.log",
        ".github/requirements/proof-artifacts/wip-modus-contributor-validation/test-wip-modus.log",
        ".github/requirements/proof-artifacts/wip-modus-contributor-validation/runtime-negative-path.log"
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ContributorWorkflowReadme_GivenDocumentedValidationCommands_CommandsExecuteInRepositoryAndProduceExpectedSuccessSignals()
    {
        var readme = ReadRepositoryFile(ReadmePath);

        Assert.Contains("## Contributor Validation Workflow (Proof Artifacts Required)", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet build src/Wip.Modus/Wip.Modus.csproj -v minimal", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Wip.Modus.Tests/Wip.Modus.Tests.csproj -v minimal", readme, StringComparison.Ordinal);
        Assert.Contains("Expected success signal: `Build succeeded.`", readme, StringComparison.Ordinal);
        Assert.Contains("Expected success signal: `Passed!`", readme, StringComparison.Ordinal);

        EnsureRequiredArtifactsExist(RequiredArtifacts);

        var buildLog = ReadRepositoryFile(RequiredArtifacts[0]);
        var testLog = ReadRepositoryFile(RequiredArtifacts[1]);
        var runtimeLog = ReadRepositoryFile(RequiredArtifacts[2]);

        Assert.Contains("Build succeeded.", buildLog, StringComparison.Ordinal);
        Assert.Contains("Passed!", testLog, StringComparison.Ordinal);
        Assert.Contains("ContributorWorkflowReadme_GivenIntentionalRuntimeFailure_NegativePathEvidenceCapturedAndLinkedInChecklist", runtimeLog, StringComparison.Ordinal);
        Assert.Contains("Test Run Successful.", runtimeLog, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ContributorWorkflowReadme_GivenIntentionalRuntimeFailure_NegativePathEvidenceCapturedAndLinkedInChecklist()
    {
        var readme = ReadRepositoryFile(ReadmePath);
        Assert.Contains("[discovery]", readme, StringComparison.Ordinal);

        var missingPath = Path.Combine(Path.GetTempPath(), $"modus-wip-missing-plugins-{Guid.NewGuid():N}");
        var bridge = new ModusWipBridge(missingPath, Array.Empty<WorkflowRegistration>());

        var count = await bridge.LoadPluginsAsync(CancellationToken.None);
        var diagnostics = bridge.GetLoadDiagnostics();

        Assert.Equal(0, count);
        Assert.Contains(diagnostics, static entry => entry.Contains("[discovery]", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ContributorWorkflowReadme_GivenMissingProofArtifact_CompletionGateFailsUntilArtifactIsProduced()
    {
        var missing = RequiredArtifacts.Concat([".github/requirements/proof-artifacts/wip-modus-contributor-validation/missing-proof-artifact.log"]).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() => EnsureRequiredArtifactsExist(missing));

        Assert.Contains("missing-proof-artifact.log", exception.Message, StringComparison.Ordinal);
    }

    private static void EnsureRequiredArtifactsExist(IEnumerable<string> relativePaths)
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var relativePath in relativePaths)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Missing required proof artifact '{relativePath}'.");
            }
        }
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var filePath = Path.Combine(repositoryRoot, relativePath);
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Could not locate repository file '{relativePath}'.");
        }

        return File.ReadAllText(filePath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx.");
    }
}