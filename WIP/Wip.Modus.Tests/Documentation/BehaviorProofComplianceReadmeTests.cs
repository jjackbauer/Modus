using System.Reflection;
using Wip.Modus.Documentation;
using Xunit;

namespace Wip.Modus.Tests.Documentation;

public sealed class BehaviorProofComplianceReadmeTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenAnyChecklistItem_MetadataOnlyTestsAreRejectedAsInsufficient()
    {
        var plannedTests = new[]
        {
            new PlannedIntegrationTest(
                "Sample_MetadataOnlyTest",
                "This test validates README metadata labels only.")
        };

        var violations = BehaviorProofChecklistVerifier.EvaluateBehaviorProofAssumptions(plannedTests);

        var violation = Assert.Single(violations);
        Assert.Equal("MetadataOnlyAssumption", violation.Code);
        Assert.Contains("metadata-only", violation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenApiOrCommandPath_AllApplicableRuntimeGatesMustPassForCompletion()
    {
        var plannedTests = new[]
        {
            new PlannedIntegrationTest(
                "BehaviorProofCompliance_GivenApiOrCommandPath_AllApplicableRuntimeGatesMustPassForCompletion",
                "API/command-focused tests are accepted only when owner resolution and runtime semantics are asserted.")
        };

        var violations = BehaviorProofChecklistVerifier.EvaluateApiOrCommandAssumptions(plannedTests);

        var violation = Assert.Single(violations);
        Assert.Equal("MissingApiProofDimensions", violation.Code);
        Assert.Contains("continuity", violation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deterministic negative-path", violation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BehaviorProofCompliance_GivenPlanDocument_EachUncheckedItemHasAtLeastOneNamedBehaviorProofTest()
    {
        var requirementsMarkdown = ReadRepositoryFile(BehaviorProofChecklistVerifier.RequirementsDocumentPath);
        var mappings = BehaviorProofChecklistVerifier.DiscoverChecklistTestMappings(Assembly.GetExecutingAssembly());

        var result = BehaviorProofChecklistVerifier.VerifyAbsoluteBehaviorProofCompliance(requirementsMarkdown, mappings);

        Assert.NotEmpty(result.PlannedTests);
        Assert.DoesNotContain(result.UncheckedChecklistItems, static item => string.Equals(item, ChecklistItem, StringComparison.Ordinal));
        Assert.True(result.IsCompliant, string.Join(Environment.NewLine, result.Violations.Select(static violation => violation.Message)));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var filePath = Path.Combine(repositoryRoot, relativePath);
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Could not locate repository file '{relativePath}'.");
        }

        return File.ReadAllText(filePath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Modus.slnx.");
    }
}