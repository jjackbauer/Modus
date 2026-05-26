using System.Reflection;
using System.Text.RegularExpressions;

namespace Wip.ShellHost.Hosting;

public sealed record PlannedIntegrationTest(string Section, string Name, string Assumption);

public sealed record ChecklistBoundTest(string ChecklistItem, string TestName);

public sealed record BehaviorProofViolation(string Code, string Message);

public sealed record BehaviorProofVerificationResult(
    IReadOnlyList<PlannedIntegrationTest> PlannedTests,
    IReadOnlyList<string> UncheckedChecklistItems,
    IReadOnlyList<ChecklistBoundTest> ChecklistBoundTests,
    IReadOnlyList<BehaviorProofViolation> Violations)
{
    public bool IsCompliant => Violations.Count == 0;
}

public static class BehaviorProofPolicy
{
    public const string RequirementsDocumentPath = ".github/requirements/Wip.ShellHost.md";

    private static readonly string[] BehaviorProofAnchors =
    [
        "runtime",
        "execution",
        "deterministic",
        "observable",
        "evidence",
        "artifact",
        "state",
        "output",
        "exit",
        "hash",
        "error",
        "status",
        "policy",
        "contract",
        "workspace",
        "review",
        "workflow",
        "dispatch",
        "integration",
        "process",
        "behavior-proof",
        "behavior proof",
        "build",
        "test"
    ];

    private static readonly string[] ApiProofDimensions =
    [
        "owner resolution",
        "business semantics",
        "lifetime correlation",
        "isolation",
        "negative contracts"
    ];

    public static IReadOnlyList<string> RequiredApiProofDimensions => ApiProofDimensions;

    public static IReadOnlyList<PlannedIntegrationTest> ParsePlannedIntegrationTests(string requirementsMarkdown)
    {
        var lines = SplitLines(requirementsMarkdown);
        var results = new List<PlannedIntegrationTest>();
        string? currentSection = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                currentSection = line[4..].Trim();
                continue;
            }

            if (!Regex.IsMatch(line, @"^\d+\.\s+", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var nameMatch = Regex.Match(line, "`([^`]+)`", RegexOptions.CultureInvariant);
            var testName = nameMatch.Success
                ? nameMatch.Groups[1].Value.Trim()
                : Regex.Replace(line, @"^\d+\.\s+", string.Empty, RegexOptions.CultureInvariant).Trim();

            var assumption = string.Empty;
            for (var probe = index + 1; probe < lines.Length; probe++)
            {
                var nextLine = lines[probe].Trim();
                if (nextLine.StartsWith("### ", StringComparison.Ordinal)
                    || Regex.IsMatch(nextLine, @"^\d+\.\s+", RegexOptions.CultureInvariant))
                {
                    break;
                }

                const string assumptionPrefix = "*Assumption*:";
                if (!nextLine.StartsWith(assumptionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                assumption = nextLine[assumptionPrefix.Length..].Trim();
                break;
            }

            if (!string.IsNullOrWhiteSpace(currentSection))
            {
                results.Add(new PlannedIntegrationTest(currentSection, testName, assumption));
            }
        }

        return results;
    }

    public static IReadOnlyList<string> ParseUncheckedChecklistItems(string requirementsMarkdown)
    {
        return SplitLines(requirementsMarkdown)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- [ ] ", StringComparison.Ordinal))
            .Select(static line => line[6..].Trim())
            .ToArray();
    }

    public static IReadOnlyList<ChecklistBoundTest> DiscoverChecklistBoundTests(Assembly testAssembly, string checklistItem)
    {
        var results = new List<ChecklistBoundTest>();

        foreach (var type in testAssembly.GetTypes())
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

                foreach (var attribute in attributes)
                {
                    if (!string.Equals(attribute.AttributeType.FullName, "Xunit.TraitAttribute", StringComparison.Ordinal)
                        || attribute.ConstructorArguments.Count < 2)
                    {
                        continue;
                    }

                    var name = attribute.ConstructorArguments[0].Value as string;
                    var value = attribute.ConstructorArguments[1].Value as string;
                    if (!string.Equals(name, "ChecklistItem", StringComparison.Ordinal)
                        || !string.Equals(value, checklistItem, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    results.Add(new ChecklistBoundTest(checklistItem, method.Name));
                }
            }
        }

        return results;
    }

    public static BehaviorProofVerificationResult VerifyAbsoluteBehaviorProofCompliance(
        string requirementsMarkdown,
        Assembly testAssembly,
        string checklistItem)
    {
        var plannedTests = ParsePlannedIntegrationTests(requirementsMarkdown);
        var uncheckedChecklistItems = ParseUncheckedChecklistItems(requirementsMarkdown);
        var checklistBoundTests = DiscoverChecklistBoundTests(testAssembly, checklistItem);
        var violations = new List<BehaviorProofViolation>();

        violations.AddRange(EvaluateBehaviorProofAssumptions(plannedTests));
        violations.AddRange(EvaluateChecklistBoundTestCoverage(plannedTests, checklistBoundTests));

        if (uncheckedChecklistItems.Contains(checklistItem, StringComparer.Ordinal))
        {
            violations.Add(new BehaviorProofViolation(
                "ChecklistItemStillUnchecked",
                $"Checklist item '{checklistItem}' must be checked once executable behavior-proof verification is enforced."));
        }

        return new BehaviorProofVerificationResult(plannedTests, uncheckedChecklistItems, checklistBoundTests, violations);
    }

    public static bool IsAbsoluteBehaviorProofAssumption(string assumption)
    {
        if (string.IsNullOrWhiteSpace(assumption))
            return false;

        return IsBehaviorProofAssumption(assumption);
    }

    public static bool IsBehaviorProofAssumption(string assumption)
    {
        if (string.IsNullOrWhiteSpace(assumption))
            return false;

        var normalized = assumption.Trim().ToLowerInvariant();
        if (normalized.Contains("metadata-only", StringComparison.Ordinal)
            && !normalized.Contains("cannot", StringComparison.Ordinal)
            && !normalized.Contains("not", StringComparison.Ordinal)
            && !normalized.Contains("reject", StringComparison.Ordinal)
            && !normalized.Contains("insufficient", StringComparison.Ordinal))
        {
            return false;
        }

        var hasBehaviorProofAnchor = BehaviorProofAnchors.Any(normalized.Contains);

        return hasBehaviorProofAnchor;
    }

    public static IReadOnlyList<BehaviorProofViolation> EvaluateBehaviorProofAssumptions(IEnumerable<PlannedIntegrationTest> plannedTests)
    {
        var violations = new List<BehaviorProofViolation>();

        foreach (var plannedTest in plannedTests)
        {
            if (string.IsNullOrWhiteSpace(plannedTest.Assumption))
            {
                violations.Add(new BehaviorProofViolation(
                    "MissingAssumption",
                    $"Planned integration test '{plannedTest.Name}' must include an assumption."));
                continue;
            }

            if (!IsAbsoluteBehaviorProofAssumption(plannedTest.Assumption))
            {
                violations.Add(new BehaviorProofViolation(
                    "MetadataOnlyAssumption",
                    $"Planned integration test '{plannedTest.Name}' must include executable behavior-proof evidence in its assumption."));
            }
        }

        return violations;
    }

    private static IReadOnlyList<BehaviorProofViolation> EvaluateChecklistBoundTestCoverage(
        IReadOnlyCollection<PlannedIntegrationTest> plannedTests,
        IReadOnlyCollection<ChecklistBoundTest> checklistBoundTests)
    {
        var violations = new List<BehaviorProofViolation>();
        var plannedPolicyTests = plannedTests
            .Where(static test => test.Name.StartsWith("IntegrationPlan_", StringComparison.Ordinal))
            .Select(static test => test.Name)
            .ToArray();
        var boundNames = checklistBoundTests
            .Select(static mapping => mapping.TestName)
            .ToHashSet(StringComparer.Ordinal);

        if (plannedPolicyTests.Length == 0)
        {
            violations.Add(new BehaviorProofViolation(
                "MissingPlannedChecklistTests",
                "Requirements must declare at least one planned behavior-proof integration test."));
            return violations;
        }

        if (boundNames.Count == 0)
        {
            violations.Add(new BehaviorProofViolation(
                "MissingExecutableChecklistTests",
                "Requirements must be backed by at least one executable checklist-bound behavior-proof test."));
            return violations;
        }

        foreach (var plannedPolicyTest in plannedPolicyTests)
        {
            if (!boundNames.Contains(plannedPolicyTest))
            {
                violations.Add(new BehaviorProofViolation(
                    "MissingExecutableChecklistTest",
                    $"Planned behavior-proof test '{plannedPolicyTest}' must exist as an executable checklist-bound xUnit test."));
            }
        }

        var plannedNames = plannedTests
            .Select(static test => test.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var boundName in boundNames)
        {
            if (!plannedNames.Contains(boundName))
            {
                violations.Add(new BehaviorProofViolation(
                    "UnplannedExecutableChecklistTest",
                    $"Checklist-bound behavior-proof test '{boundName}' must be declared in the requirements test plan."));
            }
        }

        return violations;
    }

    private static string[] SplitLines(string content)
        => content.Split(["\r\n", "\n"], StringSplitOptions.None);
}