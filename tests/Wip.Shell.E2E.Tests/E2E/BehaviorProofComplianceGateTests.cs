using System.Reflection;
using System.Text.RegularExpressions;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
    private const string BuilderRequirementsDocumentPath = ".github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenAllPlannedIntegrationTests_ExpectedBehaviorProofAssumptionsRequired()
    {
        var requirements = File.ReadAllText(GetBuilderRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);

        Assert.NotEmpty(plannedIntegrationTests);

        var nonCompliant = plannedIntegrationTests
            .Where(entry => !BehaviorProofPolicy.IsBehaviorProofAssumption(entry.Assumption))
            .Select(entry => $"{entry.Section}: {entry.Name}")
            .ToArray();

        Assert.True(
            nonCompliant.Length == 0,
            $"Planned integration tests must include behavior-proof assumptions with runtime-observable outcomes. Non-compliant entries: {string.Join("; ", nonCompliant)}");

        var uncheckedChecklistItems = ParseUncheckedChecklistItems(requirements);
        Assert.DoesNotContain(uncheckedChecklistItems, static item => string.Equals(item, ChecklistItem, StringComparison.Ordinal));

        var plannedPolicyTests = plannedIntegrationTests
            .Where(static entry => string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .Select(static entry => entry.Name)
            .ToArray();

        Assert.NotEmpty(plannedPolicyTests);

        var executableChecklistTests = DiscoverChecklistBoundTestNames(Assembly.GetExecutingAssembly(), ChecklistItem);
        var missingExecutableMappings = plannedPolicyTests
            .Where(planned => !executableChecklistTests.Contains(planned, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            missingExecutableMappings.Length == 0,
            $"Behavior-proof compliance plan entries must map to executable checklist-bound tests. Missing: {string.Join("; ", missingExecutableMappings)}");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenChecklistItemWithoutExecutableRuntimeAssertion_ExpectedPlanRejected()
    {
        const string syntheticRequirements = """
## Test Plan

### Absolute Behavior-Proof Compliance Gate

1. `BehaviorProofCompliance_GivenChecklistItemWithoutExecutableRuntimeAssertion_ExpectedPlanRejected`
   *Assumption*: Checklist items without executable runtime assertions are non-compliant and rejected by compliance gate.

2. `BehaviorProofCompliance_GivenImaginaryExecutableRequirement_ExpectedMappingExists`
   *Assumption*: Runtime command path remains observable with deterministic evidence for compliance proof.
""";

        var plannedIntegrationTests = ParsePlannedIntegrationTests(syntheticRequirements);
        var executableChecklistTests = DiscoverChecklistBoundTestNames(Assembly.GetExecutingAssembly(), ChecklistItem);

        var plannedPolicyTests = plannedIntegrationTests
            .Where(static entry => string.Equals(entry.Section, "Absolute Behavior-Proof Compliance Gate", StringComparison.Ordinal))
            .Select(static entry => entry.Name)
            .ToArray();

        var missingExecutableMappings = plannedPolicyTests
            .Where(planned => !executableChecklistTests.Contains(planned, StringComparer.Ordinal))
            .ToArray();

        var missingMapping = Assert.Single(missingExecutableMappings);
        Assert.Equal("BehaviorProofCompliance_GivenImaginaryExecutableRequirement_ExpectedMappingExists", missingMapping);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenApiFocusedPlan_ExpectedOwnerSemanticsLifetimeCorrelationAndIsolationAssertionsRequired()
    {
        var requirements = File.ReadAllText(GetBuilderRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);

        var apiComplianceEntry = plannedIntegrationTests.SingleOrDefault(
            entry => entry.Name.Equals(
                "BehaviorProofCompliance_GivenApiFocusedPlan_ExpectedOwnerSemanticsLifetimeCorrelationAndIsolationAssertionsRequired",
                StringComparison.Ordinal));

        Assert.NotNull(apiComplianceEntry);

        var missingDimensions = BehaviorProofPolicy.RequiredApiProofDimensions
            .Where(dimension => !apiComplianceEntry!.Assumption.Contains(dimension, StringComparison.OrdinalIgnoreCase))
            .Select(dimension => $"{apiComplianceEntry!.Name} missing '{dimension}'")
            .ToArray();

        Assert.True(
            missingDimensions.Length == 0,
            $"API-focused integration plans must include absolute proof dimensions. Violations: {string.Join("; ", missingDimensions)}");
    }

    private static IReadOnlyCollection<string> DiscoverChecklistBoundTestNames(Assembly testAssembly, string checklistItem)
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal);

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

    private static string GetBuilderRequirementsDocumentPath()
    {
        var root = FindRepositoryRoot();
        return Path.Combine(root, BuilderRequirementsDocumentPath.Replace('/', Path.DirectorySeparatorChar));
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
}