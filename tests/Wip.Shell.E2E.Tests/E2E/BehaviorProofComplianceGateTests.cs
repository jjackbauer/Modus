using System.Text.RegularExpressions;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class BehaviorProofComplianceGateTests
{
    [Fact]
    public void PlanCompliance_GivenChecklistItems_EnsuresEachItemHasAtLeastOneBehaviorProofIntegrationTest()
    {
        var requirements = File.ReadAllText(GetRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);

        Assert.NotEmpty(plannedIntegrationTests);

        var nonCompliant = plannedIntegrationTests
            .Where(entry => !BehaviorProofPolicy.IsBehaviorProofAssumption(entry.Assumption))
            .Select(entry => $"{entry.Section}: {entry.Name}")
            .ToArray();

        Assert.True(
            nonCompliant.Length == 0,
            $"Planned integration tests must include behavior-proof assumptions with runtime-observable outcomes. Non-compliant entries: {string.Join("; ", nonCompliant)}");
    }

    [Fact]
    public void ApiIntegrationCompliance_GivenApiFocusedTests_RequiresOwnerResolutionBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts()
    {
        var requirements = File.ReadAllText(GetRequirementsDocumentPath());
        var plannedIntegrationTests = ParsePlannedIntegrationTests(requirements);

        var apiComplianceEntry = plannedIntegrationTests.SingleOrDefault(
            entry => entry.Name.Equals(
                "ApiIntegrationCompliance_GivenApiFocusedTests_RequiresOwnerResolutionBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts",
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

    private static string GetRequirementsDocumentPath()
    {
        var root = FindRepositoryRoot();
        return Path.Combine(root, BehaviorProofPolicy.RequirementsDocumentPath.Replace('/', Path.DirectorySeparatorChar));
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

    private sealed record PlannedIntegrationTest(string Section, string Name, string Assumption);
}