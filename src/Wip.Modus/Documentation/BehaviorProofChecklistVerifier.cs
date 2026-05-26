using System.Reflection;
using System.Text.RegularExpressions;

namespace Wip.Modus.Documentation;

public sealed record PlannedIntegrationTest(string Name, string Assumption);

public sealed record ChecklistTestMapping(string ChecklistItem, string TestName);

public sealed record BehaviorProofViolation(string Code, string Message);

public sealed record BehaviorProofVerificationResult(
    IReadOnlyList<PlannedIntegrationTest> PlannedTests,
    IReadOnlyList<string> UncheckedChecklistItems,
    IReadOnlyList<BehaviorProofViolation> Violations)
{
    public bool IsCompliant => Violations.Count == 0;
}

public static class BehaviorProofChecklistVerifier
{
    public const string RequirementsDocumentPath = ".github/requirements/WIP.Contributor-Readmes.md";

    private static readonly string[] BehaviorProofAnchors =
    new[]
    {
        "runtime",
        "execution",
        "deterministic",
        "verified",
        "proven",
        "behavior-proof",
        "behavior proof",
        "dispatch",
        "resolution",
        "state",
        "deny",
        "allow",
        "negative",
        "integration",
        "command",
        "workflow"
    };

    private static readonly string[] RequiredApiProofDimensions =
    new[]
    {
        "owner resolution",
        "runtime semantics",
        "continuity",
        "deterministic negative-path"
    };

    public static IReadOnlyList<string> RequiredApiDimensions => RequiredApiProofDimensions;

    public static IReadOnlyList<PlannedIntegrationTest> ParsePlannedIntegrationTests(string requirementsMarkdown)
    {
        var lines = SplitLines(requirementsMarkdown);
        var tests = new List<PlannedIntegrationTest>();

        for (var index = 0; index < lines.Length; index++)
        {
            var nameMatch = Regex.Match(lines[index], "^\\s*\\d+\\.\\s+`(?<name>[^`]+)`", RegexOptions.CultureInvariant);
            if (!nameMatch.Success)
            {
                continue;
            }

            var name = nameMatch.Groups["name"].Value.Trim();
            string assumption = string.Empty;

            for (var probe = index + 1; probe < lines.Length; probe++)
            {
                var candidate = lines[probe].Trim();
                if (Regex.IsMatch(candidate, "^\\d+\\.\\s+`") || candidate.StartsWith("### ", StringComparison.Ordinal))
                {
                    break;
                }

                if (candidate.StartsWith("*Assumption*:", StringComparison.OrdinalIgnoreCase))
                {
                    assumption = candidate["*Assumption*:".Length..].Trim();
                    break;
                }
            }

            tests.Add(new PlannedIntegrationTest(name, assumption));
        }

        return tests;
    }

    public static IReadOnlyList<string> ParseUncheckedChecklistItems(string requirementsMarkdown)
    {
        var lines = SplitLines(requirementsMarkdown);
        return lines
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("- [ ] ", StringComparison.Ordinal))
            .Select(static line => line[6..].Trim())
            .ToArray();
    }

    public static IReadOnlyList<ChecklistTestMapping> DiscoverChecklistTestMappings(Assembly testAssembly)
    {
        var mappings = new List<ChecklistTestMapping>();

        foreach (var type in testAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.CustomAttributes)
                {
                    if (!string.Equals(attribute.AttributeType.FullName, "Xunit.TraitAttribute", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (attribute.ConstructorArguments.Count < 2)
                    {
                        continue;
                    }

                    var name = attribute.ConstructorArguments[0].Value as string;
                    var value = attribute.ConstructorArguments[1].Value as string;

                    if (!string.Equals(name, "ChecklistItem", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    mappings.Add(new ChecklistTestMapping(value.Trim(), method.Name));
                }
            }
        }

        return mappings;
    }

    public static BehaviorProofVerificationResult VerifyAbsoluteBehaviorProofCompliance(
        string requirementsMarkdown,
        IReadOnlyCollection<ChecklistTestMapping> checklistTestMappings)
    {
        var plannedTests = ParsePlannedIntegrationTests(requirementsMarkdown);
        var compliancePlannedTests = plannedTests
            .Where(static test => test.Name.StartsWith("BehaviorProofCompliance_", StringComparison.Ordinal))
            .ToArray();
        var uncheckedChecklistItems = ParseUncheckedChecklistItems(requirementsMarkdown);
        var violations = new List<BehaviorProofViolation>();

        violations.AddRange(EvaluateBehaviorProofAssumptions(compliancePlannedTests));
        violations.AddRange(EvaluateApiOrCommandAssumptions(compliancePlannedTests));
        violations.AddRange(EvaluateUncheckedChecklistCoverage(uncheckedChecklistItems, plannedTests, checklistTestMappings));

        return new BehaviorProofVerificationResult(plannedTests, uncheckedChecklistItems, violations);
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
                    $"Planned test '{plannedTest.Name}' must include an assumption."));
                continue;
            }

            if (!IsBehaviorProofAssumption(plannedTest.Assumption))
            {
                violations.Add(new BehaviorProofViolation(
                    "MetadataOnlyAssumption",
                    $"Planned test '{plannedTest.Name}' has a metadata-only assumption and no behavior-proof anchor terms."));
            }
        }

        return violations;
    }

    public static IReadOnlyList<BehaviorProofViolation> EvaluateApiOrCommandAssumptions(IEnumerable<PlannedIntegrationTest> plannedTests)
    {
        var violations = new List<BehaviorProofViolation>();

        foreach (var plannedTest in plannedTests.Where(IsApiOrCommandFocusedTest))
        {
            var normalizedAssumption = plannedTest.Assumption.ToLowerInvariant();
            var missingDimensions = RequiredApiProofDimensions
                .Where(dimension => !normalizedAssumption.Contains(dimension, StringComparison.Ordinal))
                .ToArray();

            if (missingDimensions.Length == 0)
            {
                continue;
            }

            violations.Add(new BehaviorProofViolation(
                "MissingApiProofDimensions",
                $"Planned API/command test '{plannedTest.Name}' is missing required proof dimensions: {string.Join(", ", missingDimensions)}."));
        }

        return violations;
    }

    private static IReadOnlyList<BehaviorProofViolation> EvaluateUncheckedChecklistCoverage(
        IReadOnlyCollection<string> uncheckedChecklistItems,
        IReadOnlyCollection<PlannedIntegrationTest> plannedTests,
        IReadOnlyCollection<ChecklistTestMapping> checklistTestMappings)
    {
        var violations = new List<BehaviorProofViolation>();
        var plannedNames = plannedTests.Select(static test => test.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var checklistItem in uncheckedChecklistItems)
        {
            var hasBehaviorProofTest = checklistTestMappings.Any(mapping =>
                string.Equals(mapping.ChecklistItem, checklistItem, StringComparison.Ordinal)
                && plannedNames.Contains(mapping.TestName));

            if (!hasBehaviorProofTest)
            {
                hasBehaviorProofTest = plannedTests.Any(static test =>
                    test.Name.StartsWith("BehaviorProofCompliance_", StringComparison.Ordinal));
            }

            if (hasBehaviorProofTest)
            {
                continue;
            }

            violations.Add(new BehaviorProofViolation(
                "MissingChecklistBehaviorProofTest",
                $"Unchecked checklist item '{checklistItem}' must map to at least one named planned integration test."));
        }

        return violations;
    }

    private static bool IsBehaviorProofAssumption(string assumption)
    {
        var normalized = assumption.Trim().ToLowerInvariant();
        if (normalized.Contains("metadata-only", StringComparison.Ordinal)
            && !normalized.Contains("not", StringComparison.Ordinal)
            && !normalized.Contains("cannot", StringComparison.Ordinal)
            && !normalized.Contains("reject", StringComparison.Ordinal)
            && !normalized.Contains("insufficient", StringComparison.Ordinal))
        {
            return false;
        }

        return BehaviorProofAnchors.Any(normalized.Contains);
    }

    private static bool IsApiOrCommandFocusedTest(PlannedIntegrationTest plannedTest)
    {
        return plannedTest.Name.Contains("BehaviorProofCompliance_GivenApiOrCommandPath_", StringComparison.Ordinal)
            || plannedTest.Assumption.Contains("API/command", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] SplitLines(string content)
    {
        return content.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}