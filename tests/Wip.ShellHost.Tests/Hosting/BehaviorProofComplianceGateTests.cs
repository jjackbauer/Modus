using System.Reflection;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.ShellHost.Tests.Hosting;

public sealed class BehaviorProofComplianceGateTests
{
    private const string ChecklistItem = "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
    private const string RequirementsDocumentPath = BehaviorProofPolicy.RequirementsDocumentPath;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void IntegrationPlan_GivenAnyChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofTest()
    {
        var requirements = File.ReadAllText(GetRequirementsPath());
        var result = BehaviorProofPolicy.VerifyAbsoluteBehaviorProofCompliance(
            requirements,
            Assembly.GetExecutingAssembly(),
            ChecklistItem);

        Assert.NotEmpty(result.PlannedTests);
        Assert.DoesNotContain(result.UncheckedChecklistItems, static item => string.Equals(item, ChecklistItem, StringComparison.Ordinal));
        Assert.True(result.IsCompliant, string.Join(Environment.NewLine, result.Violations.Select(static violation => violation.Message)));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void IntegrationPlan_GivenMetadataOnlyAssertions_ExpectedComplianceGateRejectsPlan()
    {
        Assert.False(BehaviorProofPolicy.IsAbsoluteBehaviorProofAssumption("Metadata-only docs snapshot; no runtime execution evidence."));
        Assert.True(BehaviorProofPolicy.IsAbsoluteBehaviorProofAssumption("Executes runtime command path and verifies deterministic output state evidence."));
    }

    [Fact]
    public void IntegrationPlan_GivenPlannedBehaviorProofTestWithoutExecutableMapping_ExpectedComplianceGateRejectsPlan()
    {
        const string syntheticRequirements = """
## Test Plan

### Lifecycle safety and policy compliance

1. `IntegrationPlan_GivenAnyChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofTest`
   *Assumption*: Checklist completion is accepted only if at least one test executes runtime behavior assertions for the item.

2. `IntegrationPlan_GivenImaginaryBehaviorProofTest_ExpectedExecutableMapping`
   *Assumption*: Runtime command output remains deterministic and observable across the compliance gate.
""";

        var result = BehaviorProofPolicy.VerifyAbsoluteBehaviorProofCompliance(
            syntheticRequirements,
            Assembly.GetExecutingAssembly(),
            ChecklistItem);

        var violation = Assert.Single(result.Violations, static violation => violation.Code == "MissingExecutableChecklistTest");
        Assert.Contains("IntegrationPlan_GivenImaginaryBehaviorProofTest_ExpectedExecutableMapping", violation.Message, StringComparison.Ordinal);
    }

    private static string GetRequirementsPath()
    {
        var root = FindRepositoryRoot();
        return Path.Combine(root, RequirementsDocumentPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}