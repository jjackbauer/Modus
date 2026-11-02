using Wip.Shell.E2E.Compliance;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[Collection(E2EComplianceSequentialCollection.Name)]
public sealed class E2EMatrixComplianceGateTests
{
    private const string IntegrityRemediationChecklistItem =
        "Upgrade E2E-001..E2E-035 matrix compliance to execute each mapped xUnit test (or transcript harness) and assert pass, not only discover method names in source files [depends on compliance registry] [mandatory - transcript execution gate]";

    [Fact]
    [Trait("ChecklistItem", IntegrityRemediationChecklistItem)]
    public async Task E2EMatrix_GivenMappedTests_ExpectedEachRowExecutesAndPassesViaFilteredDotnetTest()
    {
        if (E2EMatrixComplianceRegistry.ShouldSkipE2EMatrixRowExecution())
        {
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var requirementsPath = Path.Combine(
            repositoryRoot,
            E2EMatrixComplianceRegistry.NextStepsDocument.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var requirements = await File.ReadAllTextAsync(requirementsPath);
        var matrixEntries = E2EMatrixComplianceRegistry.ParseMatrixEntries(requirements);
        var mappings = E2EMatrixComplianceRegistry.DiscoverTestProjectMappings(repositoryRoot);

        Assert.Equal(E2EMatrixComplianceRegistry.ExpectedRowCount, matrixEntries.Count);

        var missingMappings = matrixEntries
            .Where(entry => mappings.All(mapping => !string.Equals(mapping.TestName, entry.TestName, StringComparison.Ordinal)))
            .Select(static entry => $"{entry.ScenarioId} => {entry.TestName}")
            .ToArray();

        Assert.True(
            missingMappings.Length == 0,
            $"Every E2E matrix entry must map to an executable xUnit test before execution. Missing: {string.Join(", ", missingMappings)}");

        var executionResults = await E2EMatrixComplianceRegistry.ExecuteAllRowsAsync(
            repositoryRoot,
            matrixEntries,
            mappings: mappings,
            useNoBuild: true);

        Assert.Equal(matrixEntries.Count, executionResults.Count);

        var gateResult = E2EMatrixComplianceRegistry.EvaluateRowExecutions(executionResults);
        Assert.True(
            gateResult.IsCompliant,
            $"Every E2E matrix row must execute and pass via filtered dotnet test.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, gateResult.Messages)}");
    }

    [Fact]
    [Trait("ChecklistItem", IntegrityRemediationChecklistItem)]
    public void E2EMatrix_GivenFailingMappedTest_ExpectedGateReportsScenarioIdAndTranscriptAssertionFailure()
    {
        const string scenarioId = "E2E-099";
        const string testName = "E2EMatrix_GivenSyntheticFailure_ExpectedGateReportsScenarioIdAndTranscriptAssertionFailure";
        const string projectPath = "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj";
        const string filter = "FullyQualifiedName~E2EMatrix_GivenSyntheticFailure_ExpectedGateReportsScenarioIdAndTranscriptAssertionFailure";
        const string transcriptOutput = "Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1";

        var executionResult = new E2EMatrixRowExecutionResult(
            ScenarioId: scenarioId,
            TestName: testName,
            TestProjectPath: projectPath,
            TestFilter: filter,
            ExitCode: 1,
            Output: transcriptOutput);

        var failure = E2EMatrixComplianceRegistry.CreateRowFailureViolation(executionResult);
        Assert.Equal(scenarioId, failure.ScenarioId);
        Assert.Equal(testName, failure.TestName);
        Assert.Equal(transcriptOutput, failure.Output);

        var gateResult = E2EMatrixComplianceRegistry.EvaluateRowExecutions([executionResult]);
        Assert.False(gateResult.IsCompliant);

        var message = Assert.Single(gateResult.Messages);
        Assert.Contains(scenarioId, message, StringComparison.Ordinal);
        Assert.Contains(testName, message, StringComparison.Ordinal);
        Assert.Contains(filter, message, StringComparison.Ordinal);
        Assert.Contains(projectPath, message, StringComparison.Ordinal);
        Assert.Contains(transcriptOutput, message, StringComparison.Ordinal);

        var rowFailure = Assert.Single(gateResult.RowFailures);
        Assert.Equal(scenarioId, rowFailure.ScenarioId);
        Assert.Equal(testName, rowFailure.TestName);
        Assert.Equal(transcriptOutput, rowFailure.Output);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}