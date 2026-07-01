using Wip.Shell.E2E.Compliance;
using Wip.ShellHost.Hosting;

namespace Wip.Bones.Tests.Compliance;

public sealed record WipBonesLearningLoopReliabilityBehaviorProofReport(
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

public static class WipBonesLearningLoopReliabilityBehaviorProofVerifier
{
    public const string RequirementsRelativePath = "harness/requirements/Wip.Bones.LearningLoopReliability.md";

    public const string CanonicalComplianceTestName =
        "BehaviorProofCompliance_GivenWipBonesLearningLoopReliabilityChecklist_RequiresExecutableRuntimeProofForEachItem";

    public const string BehaviorProofPolicyChecklistItem =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string MetadataOnlyRejectionTestName =
        "BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant";

    private static readonly string[] BehaviorProofGateTestNames =
    [
        CanonicalComplianceTestName,
        MetadataOnlyRejectionTestName,
    ];

    public static WipBonesLearningLoopReliabilityBehaviorProofReport Evaluate(
        string requirementsMarkdown,
        IReadOnlyCollection<DiscoveredComplianceTestMethod> discoveredTests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementsMarkdown);
        ArgumentNullException.ThrowIfNull(discoveredTests);

        var plannedIntegrationTests = BehaviorProofPolicy.ParsePlannedIntegrationTests(requirementsMarkdown);
        var checkedChecklistItems = IntegrityRemediationBehaviorProofVerifier.ParseCheckedChecklistItems(requirementsMarkdown);
        var uncheckedChecklistItems = BehaviorProofComplianceRegistry.ParseUncheckedChecklistItems(requirementsMarkdown);
        var checklistTraitBindings = IntegrityRemediationBehaviorProofVerifier.DiscoverChecklistTraitBindings(discoveredTests);
        var discoveredByName = discoveredTests
            .GroupBy(static test => test.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        var normalizedBoundChecklistItems = checklistTraitBindings
            .Select(static binding => binding.ChecklistItem)
            .Select(IntegrityRemediationBehaviorProofVerifier.NormalizeChecklistItemText)
            .ToHashSet(StringComparer.Ordinal);

        var missingCheckedChecklistBindings = checkedChecklistItems
            .Where(item => !normalizedBoundChecklistItems.Contains(
                IntegrityRemediationBehaviorProofVerifier.NormalizeChecklistItemText(item)))
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

        return new WipBonesLearningLoopReliabilityBehaviorProofReport(
            plannedIntegrationTests,
            checkedChecklistItems,
            uncheckedChecklistItems,
            checklistTraitBindings,
            missingCheckedChecklistBindings,
            missingPlannedExecutableTests,
            missingBehaviorProofGateBindings);
    }
}
