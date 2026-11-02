using System.Diagnostics;
using System.Reflection;
using Wip.Shell.E2E.Compliance;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[Collection(E2EComplianceSequentialCollection.Name)]
public sealed class BehaviorProofComplianceGateTests
{
    private const string CanonicalComplianceTestName =
        BehaviorProofComplianceRegistry.CanonicalComplianceTestName;

    private const string LegacyEveryItemAliasSuffix = "RequiresExecutableRuntimeProofForEveryItem";

    private const string ChecklistItem =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    private const string ComplianceNamingChecklistItem =
        "Standardize behavior-proof compliance test naming to `BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem` across all assemblies and fix cross-project discovery assertions [foundation for compliance integrity]";

    private static readonly ComplianceTestProject[] BehaviorProofTestProjects =
    [
        new("WIP/Wip.Abstractions.Tests", "WIP/Wip.Abstractions.Tests/Wip.Abstractions.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Builder.Tests", "WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Modus.Tests", "WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Runtime.Tests", "WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Shell.Tests", "WIP/Wip.Shell.Tests/Wip.Shell.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.ShellHost.Tests", "WIP/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Tools.Shell.Tests", "WIP/Wip.Tools.Shell.Tests/Wip.Tools.Shell.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Validation.DotNet.Tests", "WIP/Wip.Validation.DotNet.Tests/Wip.Validation.DotNet.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Workspaces.Git.Tests", "WIP/Wip.Workspaces.Git.Tests/Wip.Workspaces.Git.Tests.csproj", UseNoBuild: false),
        new("WIP/Wip.Shell.E2E.Tests", "WIP/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj", UseNoBuild: true),
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem()
    {
        var repositoryRoot = FindRepositoryRoot();
        var requirements = await File.ReadAllTextAsync(GetRequirementsDocumentPath());
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var discoveredTestsUniqueByName =
            IntegrityRemediationBehaviorProofVerifier.DeduplicateDiscoveredTestsByName(discoveredTests);
        var report = IntegrityRemediationBehaviorProofVerifier.Evaluate(requirements, discoveredTestsUniqueByName);

        var nonCompliant = report.PlannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                IntegrityRemediationBehaviorProofVerifier.MetadataOnlyRejectionTestName,
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
            $"Behavior-proof compliance gate tests must be checklist-bound to item 105. Missing: {string.Join("; ", report.MissingBehaviorProofGateBindings)}");

        var plannedPolicyEntries = report.PlannedIntegrationTests
            .Where(static entry => string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(plannedPolicyEntries);

        var plannedExecutions = IntegrityRemediationBehaviorProofVerifier.BuildPlannedExecutions(
            plannedPolicyEntries,
            discoveredTestsUniqueByName,
            BehaviorProofTestProjects,
            repositoryRoot,
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(plannedExecutions, repositoryRoot);
        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Project: {result.ProjectFilePath}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the integrity remediation compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");

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
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem));

        Assert.NotEmpty(plannedExecutions);
    }

    [Fact]
    [Trait("ChecklistItem", ComplianceNamingChecklistItem)]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenCrossProjectPlan_RecognizesRuntimeAndShellTestsOutsideExecutingAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var executableTestNames = discoveredTests
            .Select(static entry => entry.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(
            discoveredTests,
            static entry => MatchesProjectSuffix(
                    entry.ProjectFilePath,
                    "WIP",
                    "Wip.Validation.DotNet.Tests",
                    "Wip.Validation.DotNet.Tests.csproj")
                && string.Equals(entry.Name, CanonicalComplianceTestName, StringComparison.Ordinal));

        Assert.Contains(
            discoveredTests,
            static entry => MatchesProjectSuffix(
                    entry.ProjectFilePath,
                    "WIP",
                    "Wip.ShellHost.Tests",
                    "Wip.ShellHost.Tests.csproj")
                && string.Equals(entry.Name, CanonicalComplianceTestName, StringComparison.Ordinal));

        Assert.Contains(CanonicalComplianceTestName, executableTestNames);
        Assert.Contains(IntegrityRemediationBehaviorProofVerifier.MetadataOnlyRejectionTestName, executableTestNames);
        Assert.Contains(CanonicalComplianceTestName, executableTestNames);
    }

    [Fact]
    [Trait("ChecklistItem", ComplianceNamingChecklistItem)]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenLegacyEveryItemAlias_ExpectedRemovedOrRedirectedToCanonicalEachItemTest()
    {
        var repositoryRoot = FindRepositoryRoot();
        var discoveredTests = IntegrityRemediationBehaviorProofVerifier.DiscoverComplianceTests(
            repositoryRoot,
            BehaviorProofTestProjects);
        var legacyAliasTests = discoveredTests
            .Where(entry => entry.Name.EndsWith(LegacyEveryItemAliasSuffix, StringComparison.Ordinal))
            .Select(entry => $"{entry.Name} ({entry.ProjectFilePath})")
            .ToArray();

        Assert.True(
            legacyAliasTests.Length == 0,
            $"Legacy ForEveryItem compliance test names must be removed or redirected to the canonical ForEachItem name. Found: {string.Join("; ", legacyAliasTests)}");

        var canonicalComplianceTests = discoveredTests
            .Where(entry => string.Equals(entry.Name, CanonicalComplianceTestName, StringComparison.Ordinal))
            .Select(entry => entry.ProjectFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            canonicalComplianceTests.Length >= 3,
            $"Canonical compliance test must be present in Validation.DotNet, ShellHost, and E2E assemblies. Found in: {string.Join("; ", canonicalComplianceTests)}");
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
        Assert.Equal(IntegrityRemediationBehaviorProofVerifier.MetadataOnlyRejectionTestName, failedEntry);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts()
    {
        var requirements = File.ReadAllText(GetRequirementsDocumentPath());
        var plannedIntegrationTests = BehaviorProofPolicy.ParsePlannedIntegrationTests(requirements);

        var apiComplianceEntry = plannedIntegrationTests.SingleOrDefault(
            entry => entry.Name.Equals(
                IntegrityRemediationBehaviorProofVerifier.ApiFocusedCoverageTestName,
                StringComparison.Ordinal));

        Assert.NotNull(apiComplianceEntry);

        var dimensionAliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["owner resolution"] = ["owner resolution", "owner semantics"],
            ["business semantics"] = ["business semantics"],
            ["lifetime correlation"] = ["lifetime correlation", "lifetime path", "lifetime behavior"],
            ["isolation"] = ["isolation"],
            ["negative contracts"] = ["negative contracts"],
        };

        var missingDimensions = BehaviorProofPolicy.RequiredApiProofDimensions
            .Where(dimension =>
            {
                var aliases = dimensionAliases.TryGetValue(dimension, out var mapped)
                    ? mapped
                    : [dimension];

                return !aliases.Any(alias => apiComplianceEntry!.Assumption.Contains(alias, StringComparison.OrdinalIgnoreCase));
            })
            .Select(dimension => $"{apiComplianceEntry!.Name} missing '{dimension}'")
            .ToArray();

        Assert.True(
            missingDimensions.Length == 0,
            $"API-focused integration plans must include absolute proof dimensions. Violations: {string.Join("; ", missingDimensions)}");
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
    {
        var root = FindRepositoryRoot();
        return Path.Combine(
            root,
            IntegrityRemediationBehaviorProofVerifier.RequirementsRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool MatchesProjectSuffix(string projectFilePath, params string[] segments)
    {
        var suffix = string.Join('/', segments);
        var normalized = projectFilePath.Replace('\\', '/');
        return normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
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
