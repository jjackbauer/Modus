using System.Text.RegularExpressions;
using Wip.ShellHost.Hosting;

namespace Wip.Shell.E2E.Compliance;

public sealed record ChecklistTraitBinding(string ChecklistItem, string TestName, string ProjectFilePath);

public sealed record IntegrityRemediationBehaviorProofReport(
    IReadOnlyList<PlannedIntegrationTest> PlannedIntegrationTests,
    IReadOnlyList<string> CheckedChecklistItems,
    IReadOnlyList<string> UncheckedChecklistItems,
    IReadOnlyList<ChecklistTraitBinding> ChecklistTraitBindings,
    IReadOnlyList<string> MissingCheckedChecklistBindings,
    IReadOnlyList<string> MissingPlannedExecutableTests,
    IReadOnlyList<string> MissingBehaviorProofGateBindings)
{
    public bool IsCompliant =>
        MissingCheckedChecklistBindings.Count == 0
        && MissingPlannedExecutableTests.Count == 0
        && MissingBehaviorProofGateBindings.Count == 0;
}

public static class IntegrityRemediationBehaviorProofVerifier
{
    public const string RequirementsRelativePath = "harness/requirements/Wip.Integrity-Remediation.md";

    public const string BehaviorProofPolicyChecklistItem =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string MetadataOnlyRejectionTestName =
        "BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant";

    public const string ApiFocusedCoverageTestName =
        "BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts";

    private static readonly string[] BehaviorProofGateTestNames =
    [
        BehaviorProofComplianceRegistry.CanonicalComplianceTestName,
        MetadataOnlyRejectionTestName,
        ApiFocusedCoverageTestName,
    ];

    public static IntegrityRemediationBehaviorProofReport Evaluate(
        string requirementsMarkdown,
        IReadOnlyCollection<DiscoveredComplianceTestMethod> discoveredTests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementsMarkdown);
        ArgumentNullException.ThrowIfNull(discoveredTests);

        var plannedIntegrationTests = BehaviorProofPolicy.ParsePlannedIntegrationTests(requirementsMarkdown);
        var checkedChecklistItems = ParseCheckedChecklistItems(requirementsMarkdown);
        var uncheckedChecklistItems = BehaviorProofComplianceRegistry.ParseUncheckedChecklistItems(requirementsMarkdown);
        var checklistTraitBindings = DiscoverChecklistTraitBindings(discoveredTests);
        var discoveredByName = discoveredTests
            .GroupBy(static test => test.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        var boundChecklistItems = checklistTraitBindings
            .Select(static binding => binding.ChecklistItem)
            .ToHashSet(StringComparer.Ordinal);

        var normalizedBoundChecklistItems = boundChecklistItems
            .Select(NormalizeChecklistItemText)
            .ToHashSet(StringComparer.Ordinal);

        var missingCheckedChecklistBindings = checkedChecklistItems
            .Where(item => !normalizedBoundChecklistItems.Contains(NormalizeChecklistItemText(item)))
            .ToArray();

        var missingPlannedExecutableTests = plannedIntegrationTests
            .Select(static test => test.Name)
            .Where(name => !discoveredByName.ContainsKey(name))
            .ToArray();

        var boundBehaviorProofGateTests = checklistTraitBindings
            .Where(binding => string.Equals(binding.ChecklistItem, BehaviorProofPolicyChecklistItem, StringComparison.Ordinal))
            .Select(static binding => binding.TestName)
            .ToHashSet(StringComparer.Ordinal);

        var missingBehaviorProofGateBindings = BehaviorProofGateTestNames
            .Where(name => !boundBehaviorProofGateTests.Contains(name))
            .ToArray();

        return new IntegrityRemediationBehaviorProofReport(
            plannedIntegrationTests,
            checkedChecklistItems,
            uncheckedChecklistItems,
            checklistTraitBindings,
            missingCheckedChecklistBindings,
            missingPlannedExecutableTests,
            missingBehaviorProofGateBindings);
    }

    public static IReadOnlyList<string> ParseCheckedChecklistItems(string requirementsMarkdown)
    {
        return requirementsMarkdown
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- [x] ", StringComparison.Ordinal))
            .Select(static line => NormalizeChecklistItemText(line[6..].Trim()))
            .ToArray();
    }

    public static string NormalizeChecklistItemText(string checklistItem)
        => checklistItem.Replace("`", string.Empty, StringComparison.Ordinal).Trim();

    public static IReadOnlyList<ChecklistTraitBinding> DiscoverChecklistTraitBindings(
        IReadOnlyCollection<DiscoveredComplianceTestMethod> discoveredTests)
    {
        return discoveredTests
            .SelectMany(static test => test.ChecklistItems.Select(item => new ChecklistTraitBinding(item, test.Name, test.ProjectFilePath)))
            .ToArray();
    }

    public static IReadOnlyList<DiscoveredComplianceTestMethod> DiscoverComplianceTests(
        string repositoryRoot,
        IReadOnlyList<ComplianceTestProject> testProjects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(testProjects);

        var discovered = new List<DiscoveredComplianceTestMethod>();

        foreach (var project in testProjects)
        {
            var projectDirectory = Path.Combine(repositoryRoot, project.ProjectDirectoryPath);
            if (!Directory.Exists(projectDirectory))
            {
                continue;
            }

            var projectFilePath = Path.Combine(repositoryRoot, project.ProjectFilePath);
            var sharedChecklistConstants = LoadSharedChecklistConstants(projectDirectory);
            foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                             && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                discovered.AddRange(ParseExecutableTests(filePath, projectFilePath, sharedChecklistConstants));
            }
        }

        return discovered;
    }

    public static IReadOnlyList<DiscoveredComplianceTestMethod> DeduplicateDiscoveredTestsByName(
        IReadOnlyCollection<DiscoveredComplianceTestMethod> discoveredTests)
        => discoveredTests
            .GroupBy(static entry => entry.Name, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .ToArray();

    public static IReadOnlyList<PlannedTestExecutionGroup> BuildPlannedExecutions(
        IReadOnlyList<PlannedIntegrationTest> plannedIntegrationTests,
        IReadOnlyCollection<DiscoveredComplianceTestMethod> discoveredTests,
        IReadOnlyList<ComplianceTestProject> testProjects,
        string repositoryRoot,
        string currentExecutingTestName)
    {
        var testLookup = discoveredTests.ToDictionary(static entry => entry.Name, StringComparer.Ordinal);
        var plannedExecutions = new List<PlannedTestExecutionGroup>();

        var plannedTestsByProject = plannedIntegrationTests
            .Where(entry => testLookup.ContainsKey(entry.Name))
            .Select(entry => testLookup[entry.Name])
            .GroupBy(static entry => entry.ProjectFilePath, StringComparer.Ordinal);

        foreach (var projectGroup in plannedTestsByProject)
        {
            var projectSpec = testProjects.Single(spec =>
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

            plannedExecutions.Add(new PlannedTestExecutionGroup(projectSpec, testNames));
        }

        return plannedExecutions;
    }

    private static Dictionary<string, string> LoadSharedChecklistConstants(string projectDirectory)
    {
        var sharedConstants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sharedFilePath in Directory.EnumerateFiles(projectDirectory, "*RequirementsChecklistItems.cs", SearchOption.TopDirectoryOnly))
        {
            var className = Path.GetFileNameWithoutExtension(sharedFilePath);
            foreach (var pair in ParseChecklistItemConstants(File.ReadAllText(sharedFilePath)))
            {
                sharedConstants[pair.Key] = pair.Value;
                sharedConstants[$"{className}.{pair.Key}"] = pair.Value;
            }
        }

        return sharedConstants;
    }

    private static Dictionary<string, string> ResolveChecklistConstants(
        string contents,
        IReadOnlyDictionary<string, string> sharedConstants)
    {
        var resolved = new Dictionary<string, string>(sharedConstants, StringComparer.Ordinal);
        foreach (var pair in ParseChecklistItemConstants(contents))
        {
            resolved[pair.Key] = pair.Value;
        }

        var memberLookup = new Dictionary<string, string>(resolved, StringComparer.Ordinal);

        const string aliasPattern =
            "(?:private|public|internal)\\s+const\\s+string\\s+(?<name>[A-Za-z0-9_]+)\\s*=\\s*(?<reference>[A-Za-z0-9_]+(?:\\.[A-Za-z0-9_]+)?)\\s*;";
        foreach (Match match in Regex.Matches(contents, aliasPattern, RegexOptions.Multiline))
        {
            var aliasName = match.Groups["name"].Value;
            var reference = match.Groups["reference"].Value;
            if (memberLookup.TryGetValue(reference, out var memberValue)
                || memberLookup.TryGetValue(reference[(reference.LastIndexOf('.') + 1)..], out memberValue))
            {
                resolved[aliasName] = memberValue;
            }
        }

        return resolved;
    }

    private static IReadOnlyList<DiscoveredComplianceTestMethod> ParseExecutableTests(
        string filePath,
        string projectFilePath,
        IReadOnlyDictionary<string, string> sharedChecklistConstants)
    {
        const string methodPattern = "public\\s+(?:async\\s+)?(?:Task(?:<[^>]+>)?|void)\\s+(?<name>[A-Za-z0-9_]+)\\s*\\(";
        var contents = File.ReadAllText(filePath);
        var checklistConstants = ResolveChecklistConstants(contents, sharedChecklistConstants);
        var matches = Regex.Matches(contents, methodPattern, RegexOptions.Multiline);
        var discovered = new List<DiscoveredComplianceTestMethod>();

        foreach (Match match in matches)
        {
            var attributeWindowStart = Math.Max(0, match.Index - 4000);
            var attributeWindowLength = match.Index - attributeWindowStart;
            var attributes = contents.Substring(attributeWindowStart, attributeWindowLength);
            var isExecutableXunitTest = attributes.Contains("[Fact", StringComparison.Ordinal)
                || attributes.Contains("[Theory", StringComparison.Ordinal);

            if (!isExecutableXunitTest)
            {
                continue;
            }

            var checklistItems = ExtractChecklistTraitValues(attributes, checklistConstants);
            discovered.Add(new DiscoveredComplianceTestMethod(
                match.Groups["name"].Value,
                projectFilePath,
                checklistItems));
        }

        return discovered;
    }

    private static Dictionary<string, string> ParseChecklistItemConstants(string contents)
    {
        const string constantPattern =
            "(?:private|public|internal)\\s+const\\s+string\\s+(?<name>[A-Za-z0-9_]+)\\s*=\\s*(?:\\r?\\n\\s*)?\"(?<value>(?:\\\\.|[^\"\\\\])*)\"\\s*;";
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(contents, constantPattern, RegexOptions.Multiline))
        {
            constants[match.Groups["name"].Value] = match.Groups["value"].Value
                .Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return constants;
    }

    private static IReadOnlyList<string> ExtractChecklistTraitValues(
        string attributes,
        IReadOnlyDictionary<string, string> checklistConstants)
    {
        const string literalTraitPattern =
            "\\[Trait\\(\\s*\"ChecklistItem\"\\s*,\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"\\s*\\)\\]";
        const string constantTraitPattern =
            "\\[Trait\\(\\s*\"ChecklistItem\"\\s*,\\s*(?<name>[A-Za-z0-9_]+)\\s*\\)\\]";
        const string qualifiedConstantTraitPattern =
            "\\[Trait\\(\\s*\"ChecklistItem\"\\s*,\\s*(?<type>[A-Za-z0-9_]+)\\.(?<member>[A-Za-z0-9_]+)\\s*\\)\\]";

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(attributes, literalTraitPattern, RegexOptions.CultureInvariant))
        {
            values.Add(NormalizeChecklistItemText(match.Groups["value"].Value
                .Replace("\\\"", "\"", StringComparison.Ordinal)));
        }

        foreach (Match match in Regex.Matches(attributes, constantTraitPattern, RegexOptions.CultureInvariant))
        {
            var constantName = match.Groups["name"].Value;
            if (checklistConstants.TryGetValue(constantName, out var constantValue))
            {
                values.Add(NormalizeChecklistItemText(constantValue));
            }
        }

        foreach (Match match in Regex.Matches(attributes, qualifiedConstantTraitPattern, RegexOptions.CultureInvariant))
        {
            var typeName = match.Groups["type"].Value;
            var memberName = match.Groups["member"].Value;
            if (checklistConstants.TryGetValue($"{typeName}.{memberName}", out var qualifiedValue)
                || checklistConstants.TryGetValue(memberName, out qualifiedValue))
            {
                values.Add(NormalizeChecklistItemText(qualifiedValue));
            }
        }

        return values.ToArray();
    }
}

public sealed record ComplianceTestProject(string ProjectDirectoryPath, string ProjectFilePath, bool UseNoBuild);

public sealed record DiscoveredComplianceTestMethod(string Name, string ProjectFilePath, IReadOnlyList<string> ChecklistItems);

public sealed record PlannedTestExecutionGroup(ComplianceTestProject Project, IReadOnlyList<string> TestNames);
