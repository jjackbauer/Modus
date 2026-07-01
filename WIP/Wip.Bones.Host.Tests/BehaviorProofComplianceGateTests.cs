using System.Diagnostics;
using Wip.Bones.Host.Tests.Compliance;
using Wip.Shell.E2E.Compliance;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ChecklistItem = BonesHostRequirementsChecklistItems.BehaviorProofPolicy;

    private static readonly ComplianceTestProject[] BehaviorProofTestProjects =
    [
        new("WIP/Wip.Bones.Host.Tests", "WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj", UseNoBuild: true),
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenWipBonesHostChecklistItems_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesHostTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesHostBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesHostBehaviorProofVerifier.MetadataOnlyRejectionTestName,
                StringComparison.Ordinal))
            .Where(entry => !BehaviorProofPolicy.IsBehaviorProofAssumption(entry.Assumption))
            .Select(entry => $"{entry.Section}: {entry.Name}")
            .ToArray();

        Assert.True(
            nonCompliant.Length == 0,
            $"Planned integration tests must include behavior-proof assumptions with runtime-observable outcomes. Non-compliant entries: {string.Join("; ", nonCompliant)}");

        Assert.True(
            report.MissingCheckedChecklistBindings.Count == 0,
            $"Every checked checklist item must map to at least one executable Trait-bound test. Missing: {string.Join("; ", report.MissingCheckedChecklistBindings)}");

        Assert.True(
            report.MissingPlannedExecutableTests.Count == 0,
            $"Every planned integration test must map to an executable xUnit method. Missing: {string.Join("; ", report.MissingPlannedExecutableTests)}");

        Assert.True(
            report.MissingBehaviorProofGateBindings.Count == 0,
            $"Behavior-proof compliance gate tests must be checklist-bound to the behavior-proof policy item. Missing: {string.Join("; ", report.MissingBehaviorProofGateBindings)}");

        var plannedExecutions = IntegrityRemediationBehaviorProofVerifier.BuildPlannedExecutions(
            report.PlannedIntegrationTests,
            discoveredTestsUniqueByName,
            BehaviorProofTestProjects,
            repositoryRoot,
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenWipBonesHostChecklistItems_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Host compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant()
    {
        const string syntheticRequirements = """
## Test Plan

### Absolute Behavior-Proof Compliance Gate

1. `BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant`
   *Assumption*: Metadata-only assertions about headings and files existing.

""";

        var plannedIntegrationTests = BehaviorProofPolicy.ParsePlannedIntegrationTests(syntheticRequirements);
        var nonCompliant = plannedIntegrationTests
            .Where(entry => !BehaviorProofPolicy.IsBehaviorProofAssumption(entry.Assumption))
            .Select(entry => entry.Name)
            .ToArray();

        var failedEntry = Assert.Single(nonCompliant);
        Assert.Equal(WipBonesHostBehaviorProofVerifier.MetadataOnlyRejectionTestName, failedEntry);
    }

    private static async Task<IReadOnlyList<TestExecutionResult>> ExecuteMappedPlannedTestsAsync(
        IReadOnlyList<PlannedTestExecutionGroup> plannedExecutions,
        string repositoryRoot)
    {
        var results = new List<TestExecutionResult>();

        foreach (var plannedExecution in plannedExecutions)
        {
            results.Add(await ExecuteDotnetTestAsync(repositoryRoot, plannedExecution.Project, plannedExecution.TestNames));
        }

        return results;
    }

    private static async Task<TestExecutionResult> ExecuteDotnetTestAsync(
        string repositoryRoot,
        ComplianceTestProject project,
        IReadOnlyList<string> testNames)
    {
        var projectPath = Path.Combine(repositoryRoot, project.ProjectFilePath);
        var filter = BehaviorProofComplianceRegistry.BuildMappedTestFilter(testNames);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        E2EMatrixComplianceRegistry.ConfigureNestedTestProcessEnvironment(startInfo);

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(projectPath);
        if (project.UseNoBuild)
        {
            startInfo.ArgumentList.Add("--no-build");
        }

        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("minimal");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start dotnet test for '{projectPath}'.");

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = (await standardOutput) + (await standardError);
        return new TestExecutionResult(project.ProjectFilePath, process.ExitCode, output);
    }

    private static string GetRequirementsDocumentPath()
        => Path.Combine(
            BonesHostTestPaths.FindRepositoryRoot(),
            WipBonesHostBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed record TestExecutionResult(string ProjectFilePath, int ExitCode, string Output);
}
