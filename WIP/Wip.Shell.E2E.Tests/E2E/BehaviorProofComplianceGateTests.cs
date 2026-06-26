using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
    private const string RequirementsDocumentPath = ".github/requirements/Wip.DotNet-Development-Flows.md";
    private static readonly BehaviorProofTestProject[] BehaviorProofTestProjects =
    [
        new(Path.Combine("wip", "Wip.Abstractions.Tests"), Path.Combine("wip", "Wip.Abstractions.Tests", "Wip.Abstractions.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Builder.Tests"), Path.Combine("wip", "Wip.Builder.Tests", "Wip.Builder.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Modus.Tests"), Path.Combine("wip", "Wip.Modus.Tests", "Wip.Modus.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Runtime.Tests"), Path.Combine("wip", "Wip.Runtime.Tests", "Wip.Runtime.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Shell.Tests"), Path.Combine("wip", "Wip.Shell.Tests", "Wip.Shell.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.ShellHost.Tests"), Path.Combine("wip", "Wip.ShellHost.Tests", "Wip.ShellHost.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Tools.Shell.Tests"), Path.Combine("wip", "Wip.Tools.Shell.Tests", "Wip.Tools.Shell.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Validation.DotNet.Tests"), Path.Combine("wip", "Wip.Validation.DotNet.Tests", "Wip.Validation.DotNet.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Workspaces.Git.Tests"), Path.Combine("wip", "Wip.Workspaces.Git.Tests", "Wip.Workspaces.Git.Tests.csproj"), UseNoBuild: false),
        new(Path.Combine("wip", "Wip.Shell.E2E.Tests"), Path.Combine("wip", "Wip.Shell.E2E.Tests", "Wip.Shell.E2E.Tests.csproj"), UseNoBuild: true)
    ];

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEveryItem()
    {
        var requirements = File.ReadAllText(GetBuilderRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);

        Assert.NotEmpty(plannedIntegrationTests);

        var nonCompliant = plannedIntegrationTests
            .Where(static entry => !string.Equals(
                entry.Name,
                "BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsChecklistCompletionAsInsufficient",
                StringComparison.Ordinal))
            .Where(entry => !BehaviorProofPolicy.IsBehaviorProofAssumption(entry.Assumption))
            .Select(entry => $"{entry.Section}: {entry.Name}")
            .ToArray();

        Assert.True(
            nonCompliant.Length == 0,
            $"Planned integration tests must include behavior-proof assumptions with runtime-observable outcomes. Non-compliant entries: {string.Join("; ", nonCompliant)}");

        var uncheckedChecklistItems = ParseUncheckedChecklistItems(requirements);
        var discoveredTests = DiscoverBehaviorProofTestMethods();

        var plannedPolicyEntries = plannedIntegrationTests
            .Where(static entry => string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(plannedPolicyEntries);

        var plannedExecutableTests = plannedPolicyEntries
            .Select(static entry => entry.Name)
            .ToArray();

        var ambiguousMappings = discoveredTests
            .GroupBy(static entry => entry.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(group => $"{group.Key} => {string.Join(", ", group.Select(static entry => entry.ProjectFilePath))}")
            .ToArray();

        Assert.True(
            ambiguousMappings.Length == 0,
            $"Planned integration tests must map to exactly one test project. Ambiguous mappings: {string.Join("; ", ambiguousMappings)}");

        var executableTestNames = discoveredTests
            .Select(static entry => entry.Name)
            .ToArray();

        var missingExecutableMappings = plannedExecutableTests
            .Where(planned => !executableTestNames.Contains(planned, StringComparer.Ordinal))
            .ToArray();

        var plannedPolicyTests = plannedPolicyEntries
            .Select(static entry => entry.Name)
            .ToArray();

        var executableChecklistTests = DiscoverChecklistBoundTestNames(ChecklistItem);
        var missingChecklistBoundMappings = plannedPolicyTests
            .Where(planned => !executableChecklistTests.Contains(planned, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            missingChecklistBoundMappings.Length == 0,
            $"Behavior-proof compliance plan entries must map to executable checklist-bound tests. Missing: {string.Join("; ", missingChecklistBoundMappings)}");

        Assert.True(
            missingExecutableMappings.Length == 0,
            $"Every planned integration test must map to an executable xUnit method. Missing: {string.Join("; ", missingExecutableMappings)}");

        var plannedExecutions = BuildPlannedExecutions(
            plannedPolicyEntries,
            discoveredTests,
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEveryItem));

        var executionResults = await ExecuteMappedPlannedTestsAsync(
            plannedExecutions,
            repositoryRoot: FindRepositoryRoot());

        var failedExecutions = executionResults
            .Where(static result => result.ExitCode != 0)
            .Select(result => $"Command: {result.Command}{Environment.NewLine}{result.Output}")
            .ToArray();

        Assert.True(
            failedExecutions.Length == 0,
            $"Every mapped planned integration test must execute successfully from the E2E compliance gate.{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failedExecutions)}");

        Assert.DoesNotContain(uncheckedChecklistItems, static item => string.Equals(item, ChecklistItem, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenPlannedNonPolicyIntegrationTests_SelectsExecutableMappingsWithoutNameDrift()
    {
        var requirements = File.ReadAllText(GetBuilderRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);
        var discoveredTests = DiscoverBehaviorProofTestMethods();

        var plannedNonPolicyEntries = plannedIntegrationTests
            .Where(static entry => !string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(plannedNonPolicyEntries);

        var discoveredByName = discoveredTests
            .Select(static entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        var mappedNames = plannedNonPolicyEntries
            .Select(static entry => entry.Name)
            .Where(discoveredByName.Contains)
            .ToArray();

        Assert.NotEmpty(mappedNames);

        var plannedExecutions = BuildPlannedExecutions(
            plannedNonPolicyEntries,
            discoveredTests,
            currentExecutingTestName: nameof(BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEveryItem));

        Assert.NotEmpty(plannedExecutions);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenCrossProjectPlan_RecognizesRuntimeAndShellTestsOutsideExecutingAssembly()
    {
        var discoveredTests = DiscoverBehaviorProofTestMethods();
        var executableTestNames = discoveredTests
            .Select(static entry => entry.Name)
            .ToArray();

        Assert.Contains(
            discoveredTests,
            static entry => entry.ProjectFilePath.EndsWith(Path.Combine("wip", "Wip.Validation.DotNet.Tests", "Wip.Validation.DotNet.Tests.csproj"), StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            "BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem",
            executableTestNames);
        Assert.Contains(
            "BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant",
            executableTestNames);
        Assert.Contains(
            nameof(BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEveryItem),
            executableTestNames);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsChecklistCompletionAsInsufficient()
    {
        const string syntheticRequirements = """
## Test Plan

### Absolute Behavior-Proof Compliance Gate

1. `BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsChecklistCompletionAsInsufficient`
   *Assumption*: Metadata-only assertions about headings and files existing.

""";

        var plannedIntegrationTests = ParsePlannedIntegrationTests(syntheticRequirements);
        var nonCompliant = plannedIntegrationTests
            .Where(entry => !BehaviorProofPolicy.IsBehaviorProofAssumption(entry.Assumption))
            .Select(entry => entry.Name)
            .ToArray();

        var failedEntry = Assert.Single(nonCompliant);
        Assert.Equal("BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsChecklistCompletionAsInsufficient", failedEntry);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenDotNetIntegrationTests_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContractProof()
    {
        var requirements = File.ReadAllText(GetBuilderRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);

        var apiComplianceEntry = plannedIntegrationTests.SingleOrDefault(
            entry => entry.Name.Equals(
                "BehaviorProofCompliance_GivenDotNetIntegrationTests_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContractProof",
                StringComparison.Ordinal));

        Assert.NotNull(apiComplianceEntry);

        var dimensionAliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["owner resolution"] = ["owner resolution", "owner semantics"],
            ["business semantics"] = ["business semantics"],
            ["lifetime correlation"] = ["lifetime correlation", "lifetime behavior"],
            ["isolation"] = ["isolation"],
            ["negative contracts"] = ["negative contracts"]
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

    private static IReadOnlyCollection<string> DiscoverChecklistBoundTestNames(string checklistItem)
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var attributes = method.CustomAttributes.ToArray();
                var isExecutableXunitTest = attributes.Any(attribute =>
                    string.Equals(attribute.AttributeType.FullName, "Xunit.FactAttribute", StringComparison.Ordinal)
                    || string.Equals(attribute.AttributeType.FullName, "Xunit.TheoryAttribute", StringComparison.Ordinal));

                if (!isExecutableXunitTest)
                {
                    continue;
                }

                var hasChecklistTrait = attributes.Any(attribute =>
                    string.Equals(attribute.AttributeType.FullName, "Xunit.TraitAttribute", StringComparison.Ordinal)
                    && attribute.ConstructorArguments.Count >= 2
                    && string.Equals(attribute.ConstructorArguments[0].Value as string, "ChecklistItem", StringComparison.Ordinal)
                    && string.Equals(attribute.ConstructorArguments[1].Value as string, checklistItem, StringComparison.Ordinal));

                if (hasChecklistTrait)
                {
                    discovered.Add(method.Name);
                }
            }
        }

        return discovered;
    }

    private static IReadOnlyCollection<DiscoveredTestMethod> DiscoverBehaviorProofTestMethods()
    {
        var repositoryRoot = FindRepositoryRoot();
        var discovered = new Dictionary<string, DiscoveredTestMethod>(StringComparer.Ordinal);

        foreach (var project in BehaviorProofTestProjects)
        {
            var projectDirectory = Path.Combine(repositoryRoot, project.ProjectDirectoryPath);
            if (!Directory.Exists(projectDirectory))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                             && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var testMethod in ParseExecutableTests(filePath, Path.Combine(repositoryRoot, project.ProjectFilePath)))
                {
                    discovered[testMethod.Name] = testMethod;
                }
            }
        }

        return discovered.Values.ToArray();
    }

    private static IReadOnlyCollection<string> DiscoverExecutableTestNames()
    {
        return DiscoverBehaviorProofTestMethods()
            .Select(static entry => entry.Name)
            .ToArray();
    }

    private static IReadOnlyCollection<DiscoveredTestMethod> ParseExecutableTests(string filePath, string projectFilePath)
    {
        const string methodPattern = "public\\s+(?:async\\s+)?(?:Task(?:<[^>]+>)?|void)\\s+(?<name>[A-Za-z0-9_]+)\\s*\\(";
        var contents = File.ReadAllText(filePath);
        var matches = Regex.Matches(contents, methodPattern, RegexOptions.Multiline);
        var discovered = new List<DiscoveredTestMethod>();

        foreach (Match match in matches)
        {
            var attributeWindowStart = Math.Max(0, match.Index - 2000);
            var attributeWindowLength = match.Index - attributeWindowStart;
            var attributes = contents.Substring(attributeWindowStart, attributeWindowLength);
            var isExecutableXunitTest = attributes.Contains("[Fact", StringComparison.Ordinal)
                || attributes.Contains("[Theory", StringComparison.Ordinal);

            if (!isExecutableXunitTest)
            {
                continue;
            }

            discovered.Add(new DiscoveredTestMethod(match.Groups["name"].Value, projectFilePath));
        }

        return discovered;
    }

    private static IReadOnlyList<PlannedTestExecution> BuildPlannedExecutions(
        IReadOnlyList<PlannedIntegrationTest> plannedIntegrationTests,
        IReadOnlyCollection<DiscoveredTestMethod> discoveredTests,
        string currentExecutingTestName)
    {
        var testLookup = discoveredTests.ToDictionary(static entry => entry.Name, StringComparer.Ordinal);
        var repositoryRoot = FindRepositoryRoot();
        var plannedExecutions = new List<PlannedTestExecution>();

        var plannedTestsByProject = plannedIntegrationTests
            .Where(entry => testLookup.ContainsKey(entry.Name))
            .Select(entry => testLookup[entry.Name])
            .GroupBy(static entry => entry.ProjectFilePath, StringComparer.Ordinal);

        foreach (var projectGroup in plannedTestsByProject)
        {
            var projectSpec = BehaviorProofTestProjects.Single(spec =>
                string.Equals(Path.Combine(repositoryRoot, spec.ProjectFilePath), projectGroup.Key, StringComparison.Ordinal));

            var testNames = projectGroup
                .Select(static entry => entry.Name)
                .Where(name => !string.Equals(name, currentExecutingTestName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            if (testNames.Length == 0)
            {
                continue;
            }

            plannedExecutions.Add(new PlannedTestExecution(projectSpec, testNames));
        }

        return plannedExecutions;
    }

    private static async Task<IReadOnlyList<TestExecutionResult>> ExecuteMappedPlannedTestsAsync(
        IReadOnlyList<PlannedTestExecution> plannedExecutions,
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
        BehaviorProofTestProject project,
        IReadOnlyList<string> testNames)
    {
        var projectPath = Path.Combine(repositoryRoot, project.ProjectFilePath);
        var filter = string.Join("|", testNames.Select(static name => $"FullyQualifiedName~{name}"));
        var commandParts = new List<string>
        {
            "dotnet",
            "test",
            Quote(projectPath),
            "--filter",
            Quote(filter),
            "-v",
            "minimal"
        };

        if (project.UseNoBuild)
        {
            commandParts.Insert(3, "--no-build");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
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
        return new TestExecutionResult(string.Join(" ", commandParts), process.ExitCode, output);
    }

    private static string Quote(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string GetBuilderRequirementsDocumentPath()
    {
        var root = FindRepositoryRoot();
        return Path.Combine(root, RequirementsDocumentPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }

    private static IReadOnlyList<PlannedIntegrationTest> ParsePlannedIntegrationTests(string requirements)
    {
        var lines = requirements
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.TrimEnd())
            .ToArray();

        var results = new List<PlannedIntegrationTest>();
        string? currentSection = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                currentSection = line[4..].Trim();
                continue;
            }

            if (!Regex.IsMatch(line, @"^\d+\.\s+"))
                continue;

            var nameMatch = Regex.Match(line, "`([^`]+)`");
            var testName = nameMatch.Success
                ? nameMatch.Groups[1].Value.Trim()
                : Regex.Replace(line, @"^\d+\.\s+", string.Empty).Trim();

            var assumption = string.Empty;
            for (var j = i + 1; j < lines.Length; j++)
            {
                var nextLine = lines[j].Trim();
                if (nextLine.StartsWith("### ", StringComparison.Ordinal) || Regex.IsMatch(nextLine, @"^\d+\.\s+"))
                    break;

                const string assumptionPrefix = "*Assumption*:";
                if (nextLine.StartsWith(assumptionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    assumption = nextLine[assumptionPrefix.Length..].Trim();
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(currentSection))
            {
                results.Add(new PlannedIntegrationTest(currentSection, testName, assumption));
            }
        }

        return results;
    }

    private static IReadOnlyList<string> ParseUncheckedChecklistItems(string requirements)
    {
        return requirements
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- [ ] ", StringComparison.Ordinal))
            .Select(static line => line[6..].Trim())
            .ToArray();
    }

    private sealed record PlannedIntegrationTest(string Section, string Name, string Assumption);

    private sealed record BehaviorProofTestProject(string ProjectDirectoryPath, string ProjectFilePath, bool UseNoBuild);

    private sealed record DiscoveredTestMethod(string Name, string ProjectFilePath);

    private sealed record PlannedTestExecution(BehaviorProofTestProject Project, IReadOnlyList<string> TestNames);

    private sealed record TestExecutionResult(string Command, int ExitCode, string Output);
}