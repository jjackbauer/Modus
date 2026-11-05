using Xunit;

namespace Modus.Host.IntegrationTests;

/// <summary>
/// Reconciliation of the pre-G0 Linux gate meta-tests with the current CI split: the offline suite
/// runs on windows-latest through tools/run-wip-tests.ps1, both platform builds compile Wip.slnx,
/// and the full legacy Modus suite is blocking again in Modus CI.
/// </summary>
public sealed class TestsCiWorkflowLinuxFirstWipGateTests
{
    private const string ChecklistItem = "Add Linux-first CI execution gate for Wip.* build/test matrix to satisfy cross-platform MVP baseline with deterministic artifact retention on failures [depends on NFR cross-platform support]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenPlatformBuildJobs_ExpectedBothBuildWipSolutionInRelease()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));

        Assert.Contains("  wip-build-ubuntu:", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("  wip-build-windows:", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet build Wip.slnx --configuration Release --no-restore", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenOfflineGateJob_ExpectedWindowsRunnerAndAlwaysArtifactUpload()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));

        Assert.Contains("  wip-offline-tests:", workflow, StringComparison.Ordinal);
        Assert.Contains("pwsh -NoProfile -File tools/run-wip-tests.ps1 -Configuration Release -ResultsDirectory artifacts/ci-test-results", workflow, StringComparison.Ordinal);
        Assert.Contains("pwsh -NoProfile -File tools/verify-test-results.ps1 -ResultsDirectory artifacts/ci-test-results -WaiverPath docs/plan/test-waivers.json", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Upload offline test results", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error: true", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void ModusWorkflow_GivenLegacySuiteStep_ExpectedBlockingWithoutContinueOnError()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "modus-ci.yml"));

        Assert.Contains("name: Test Modus.Host.IntegrationTests (legacy suite)", workflow, StringComparison.Ordinal);
        Assert.Contains("--logger \"trx;LogFileName=Modus.Host.IntegrationTests.trx\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error: true", workflow, StringComparison.Ordinal);
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
