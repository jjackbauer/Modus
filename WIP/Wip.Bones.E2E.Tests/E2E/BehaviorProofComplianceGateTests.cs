using System.Diagnostics;
using Wip.Shell.E2E.Compliance;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Bones.E2E.Tests.E2E;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
    private const string BonesE2EEnvironmentVariable = "WIP_BONES_E2E";

    private static readonly ComplianceTestProject[] BehaviorProofTestProjects =
    [
        new("WIP/Wip.Bones.Tests", "WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Bones.Web.Tests", "WIP/Wip.Bones.Web.Tests/Wip.Bones.Web.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Bones.E2E.Tests", "WIP/Wip.Bones.E2E.Tests/Wip.Bones.E2E.Tests.csproj", UseNoBuild: true),
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenWipBonesChecklistItems_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = WipBonesBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                WipBonesBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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

        var plannedPolicyEntries = report.PlannedIntegrationTests
            .Where(static entry => string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(plannedPolicyEntries);

        var plannedExecutions = IntegrityRemediationBehaviorProofVerifier.BuildPlannedExecutions(
            plannedPolicyEntries,
            discoveredTestsUniqueByName,
            BehaviorProofTestProjects,
            repositoryRoot,
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenWipBonesChecklistItems_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the Wip.Bones compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");

        Assert.DoesNotContain(report.UncheckedChecklistItems, static item => string.Equals(item, ChecklistItem, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenPlannedNonPolicyIntegrationTests_SelectsExecutableMappingsWithoutNameDrift()
    {
        var repositoryRoot = FindRepositoryRoot();
        var requirements = File.ReadAllText(GetRequirementsDocumentPath());
        var plannedIntegrationTests = BehaviorProofPolicy.ParsePlannedIntegrationTests(requirements);
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);

        var plannedNonPolicyEntries = plannedIntegrationTests
            .Where(static entry => !string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(plannedNonPolicyEntries);

        var discoveredByName = discoveredTestsUniqueByName
            .Select(static entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        var mappedNames = plannedNonPolicyEntries
            .Select(static entry => entry.Name)
            .Where(discoveredByName.Contains)
            .ToArray();

        Assert.NotEmpty(mappedNames);

        var plannedExecutions = IntegrityRemediationBehaviorProofVerifier.BuildPlannedExecutions(
            plannedNonPolicyEntries,
            discoveredTestsUniqueByName,
            BehaviorProofTestProjects,
            repositoryRoot,
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenWipBonesChecklistItems_RequiresExecutableRuntimeProofForEachItem));

        Assert.NotEmpty(plannedExecutions);
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
        Assert.Equal(WipBonesBehaviorProofVerifier.MetadataOnlyRejectionTestName, failedEntry);
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
        if (project.ProjectFilePath.Contains("Wip.Bones.E2E.Tests", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment[BonesE2EEnvironmentVariable] = "1";
        }

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
    {
        var root = FindRepositoryRoot();
        return Path.Combine(
            root,
            WipBonesBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));
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

    private sealed record TestExecutionResult(string ProjectFilePath, int ExitCode, string Output);
}