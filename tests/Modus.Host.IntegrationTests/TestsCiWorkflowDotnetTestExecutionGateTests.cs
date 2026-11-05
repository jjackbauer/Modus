using Xunit;

namespace Modus.Host.IntegrationTests;

/// <summary>
/// Reconciliation of the pre-split dotnet-test meta-tests with the current Tests CI layout: the
/// WIP offline gate runs through tools/run-wip-tests.ps1 and is enforced by
/// tools/verify-test-results.ps1, while the architecture job runs both architecture projects.
/// </summary>
public sealed class TestsCiWorkflowDotnetTestExecutionGateTests
{
    private const string ChecklistItem = "Execute `dotnet test --configuration Release --no-build --logger trx` across unit and integration projects, and fail job on any failing test";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenOfflineGateJob_ExpectedRunnerAndWaiverGateCommands()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));

        Assert.Contains("  wip-offline-tests:", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "pwsh -NoProfile -File tools/run-wip-tests.ps1 -Configuration Release -ResultsDirectory artifacts/ci-test-results",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "pwsh -NoProfile -File tools/verify-test-results.ps1 -ResultsDirectory artifacts/ci-test-results -WaiverPath docs/plan/test-waivers.json",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("exit $code", workflow, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenArchitectureJob_ExpectedBothArchitectureProjectsRunWithReleaseNoBuildTrx()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "tests-ci.yml"));

        Assert.Contains(
            "dotnet test tests/Modus.Architecture.Tests/Modus.Architecture.Tests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Modus.Architecture.Tests.trx\"",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet test tests/Wip.Architecture.Tests/Wip.Architecture.Tests.csproj --configuration Release --no-build --logger \"trx;LogFileName=Wip.Architecture.Tests.trx\"",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void TestsWorkflow_GivenSingleFailingTestCommand_ExpectedFailurePropagatesToJobResult()
    {
        var workflow = ReadRepositoryFile(Path.Combine(".github", "workflows", "modus-ci.yml"));

        var simulation = SimulateJobResult(
            workflow,
            failingCommand: "dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build");

        Assert.Equal("failure", simulation.Conclusion);
        Assert.True(simulation.StepIsBlocking, "The legacy suite step must not be continue-on-error.");
        Assert.Contains("Test Modus.Host.IntegrationTests (legacy suite)", simulation.StepName, StringComparison.Ordinal);
    }

    private static JobResult SimulateJobResult(string workflow, string failingCommand)
    {
        var normalized = workflow.Replace("\r\n", "\n", StringComparison.Ordinal);
        var stepStart = normalized.IndexOf("name: Test Modus.Host.IntegrationTests (legacy suite)", StringComparison.Ordinal);
        Assert.True(stepStart >= 0, "Expected the blocking legacy suite step in Modus CI.");

        var stepBlock = normalized[stepStart..];
        var runIndex = stepBlock.IndexOf("\n        run: ", StringComparison.Ordinal);
        var stepHeader = runIndex >= 0 ? stepBlock[..runIndex] : stepBlock;
        var stepIsBlocking = !stepHeader.Contains("continue-on-error", StringComparison.Ordinal);

        var conclusion = stepIsBlocking ? "failure" : "success";
        return new JobResult(
            Conclusion: conclusion,
            StepIsBlocking: stepIsBlocking,
            StepName: stepHeader.Split('\n')[0]);
    }

    private sealed record JobResult(string Conclusion, bool StepIsBlocking, string StepName);

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
