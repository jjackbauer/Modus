using System.Diagnostics;
using Wip.Bones.Web.Tests.Compliance;
using Wip.Shell.E2E.Compliance;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ViewerVisualizationPolicyItem =
        BonesViewerVisualizationRequirementsChecklistItems.BehaviorProofPolicy;

    private const string ViewerVisualizationBehaviorProofTestsItem =
        BonesViewerVisualizationRequirementsChecklistItems.BehaviorProofTests;

    private const string ViewerReplayScalingPolicyItem =
        BonesViewerReplayScalingRequirementsChecklistItems.BehaviorProofPolicy;

    private const string ViewerReplayScalingBehaviorProofTestsItem =
        BonesViewerReplayScalingRequirementsChecklistItems.BehaviorProofTests;

    private const string ViewerBoardChainLayoutPolicyItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.BehaviorProofPolicy;

    private const string ViewerBoardChainLayoutComplianceRegistryItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerDominoRenderingPolicyItem =
        BonesViewerDominoRenderingRequirementsChecklistItems.BehaviorProofPolicy;

    private const string ViewerDominoRenderingComplianceRegistryItem =
        BonesViewerDominoRenderingRequirementsChecklistItems.ComplianceRegistry;

    private const string ViewerLayoutAndStabilityPolicyItem =
        BonesViewerLayoutAndStabilityRequirementsChecklistItems.BehaviorProofPolicy;

    private const string ViewerLayoutAndStabilityBehaviorProofTestsItem =
        BonesViewerLayoutAndStabilityRequirementsChecklistItems.BehaviorProofTests;

    private const string ViewerLearningLoopStatusPolicyItem =
        BonesViewerLearningLoopStatusRequirementsChecklistItems.BehaviorProofPolicy;

    private const string ViewerLearningLoopStatusBehaviorProofTestsItem =
        BonesViewerLearningLoopStatusRequirementsChecklistItems.BehaviorProofTests;

    private static readonly ComplianceTestProject[] BehaviorProofTestProjects =
    [
        new("WIP/Wip.Bones.Web.Tests", "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", UseNoBuild: true),
    ];

    [Fact]
    [Trait("ChecklistItem", ViewerVisualizationPolicyItem)]
    [Trait("ChecklistItem", ViewerVisualizationBehaviorProofTestsItem)]
    public async Task BehaviorProofCompliance_GivenViewerVisualizationRequirements_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetViewerVisualizationRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesWebBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesWebBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenViewerVisualizationRequirements_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Web compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerReplayScalingPolicyItem)]
    [Trait("ChecklistItem", ViewerReplayScalingBehaviorProofTestsItem)]
    public async Task BehaviorProofCompliance_GivenViewerReplayScalingRequirements_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetViewerReplayScalingRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesWebViewerReplayScalingBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesWebViewerReplayScalingBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenViewerReplayScalingRequirements_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Web replay-scaling compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerBoardChainLayoutPolicyItem)]
    [Trait("ChecklistItem", "Enforce absolute behavior-proof verification [mandatory - behavior-proof policy]")]
    [Trait("ChecklistItem", ViewerBoardChainLayoutComplianceRegistryItem)]
    public async Task BehaviorProofCompliance_GivenViewerBoardChainLayoutRequirements_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetViewerBoardChainLayoutRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesWebViewerBoardChainLayoutBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesWebViewerBoardChainLayoutBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenViewerBoardChainLayoutRequirements_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Web board-chain layout compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerDominoRenderingPolicyItem)]
    [Trait("ChecklistItem", "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]")]
    [Trait("ChecklistItem", ViewerDominoRenderingComplianceRegistryItem)]
    public async Task BehaviorProofCompliance_GivenViewerDominoRenderingRequirements_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetViewerDominoRenderingRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesWebViewerDominoRenderingBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesWebViewerDominoRenderingBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenViewerDominoRenderingRequirements_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Web domino-rendering compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerLayoutAndStabilityPolicyItem)]
    [Trait("ChecklistItem", ViewerLayoutAndStabilityBehaviorProofTestsItem)]
    public async Task BehaviorProofCompliance_GivenViewerLayoutAndStabilityRequirements_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetViewerLayoutAndStabilityRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesWebViewerLayoutAndStabilityBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesWebViewerLayoutAndStabilityBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenViewerLayoutAndStabilityRequirements_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Web layout-and-stability compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerLearningLoopStatusPolicyItem)]
    [Trait("ChecklistItem", ViewerLearningLoopStatusBehaviorProofTestsItem)]
    public async Task BehaviorProofCompliance_GivenViewerLearningLoopStatusRequirements_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = BonesWebTestPaths.FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetViewerLearningLoopStatusRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesWebViewerLearningLoopStatusBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesWebViewerLearningLoopStatusBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenViewerLearningLoopStatusRequirements_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones.Web learning-loop status compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");
    }

    [Fact]
    [Trait("ChecklistItem", ViewerVisualizationPolicyItem)]
    [Trait("ChecklistItem", ViewerReplayScalingPolicyItem)]
    [Trait("ChecklistItem", ViewerBoardChainLayoutPolicyItem)]
    [Trait("ChecklistItem", ViewerDominoRenderingPolicyItem)]
    [Trait("ChecklistItem", ViewerLayoutAndStabilityPolicyItem)]
    [Trait("ChecklistItem", ViewerLearningLoopStatusPolicyItem)]
    [Trait("ChecklistItem", "Enforce absolute behavior-proof verification [mandatory - behavior-proof policy]")]
    [Trait("ChecklistItem", "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]")]
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
        Assert.Equal(WipBonesWebBehaviorProofVerifier.MetadataOnlyRejectionTestName, failedEntry);
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

    private static string GetViewerVisualizationRequirementsDocumentPath()
        => Path.Combine(
            BonesWebTestPaths.FindRepositoryRoot(),
            WipBonesWebBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetViewerReplayScalingRequirementsDocumentPath()
        => Path.Combine(
            BonesWebTestPaths.FindRepositoryRoot(),
            WipBonesWebViewerReplayScalingBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetViewerBoardChainLayoutRequirementsDocumentPath()
        => Path.Combine(
            BonesWebTestPaths.FindRepositoryRoot(),
            WipBonesWebViewerBoardChainLayoutBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetViewerDominoRenderingRequirementsDocumentPath()
        => Path.Combine(
            BonesWebTestPaths.FindRepositoryRoot(),
            WipBonesWebViewerDominoRenderingBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetViewerLayoutAndStabilityRequirementsDocumentPath()
        => Path.Combine(
            BonesWebTestPaths.FindRepositoryRoot(),
            WipBonesWebViewerLayoutAndStabilityBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetViewerLearningLoopStatusRequirementsDocumentPath()
        => Path.Combine(
            BonesWebTestPaths.FindRepositoryRoot(),
            WipBonesWebViewerLearningLoopStatusBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed record TestExecutionResult(string ProjectFilePath, int ExitCode, string Output);
}