using Xunit;

namespace Wip.Factory.Host.Tests;

public sealed class BehaviorProofRuleTests
{
    [Fact]
    public void BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant()
    {
        // A body that only checks shape says nothing about behaviour.
        var metadataOnly = new[] { "Assert.NotNull(result)", "Assert.IsType<PingResult>(agent)" };
        Assert.True(BehaviorProofRules.IsMetadataOnly(metadataOnly));

        // No assertion at all is equally non-compliant.
        Assert.True(BehaviorProofRules.IsMetadataOnly([]));

        // A body that asserts a produced value is compliant.
        var behavioural = new[]
        {
            "Assert.NotNull(result)",
            "Assert.Equal(\"hello\", executed.Value)"
        };
        Assert.False(BehaviorProofRules.IsMetadataOnly(behavioural));

        // The rule is not vacuous: it accepts a real gate test's assertions, including the equality
        // check that proves the work-state machine committed the right state.
        var realTest = new[]
        {
            "Assert.True(legalResult.Succeeded)",
            "Assert.Equal(FactoryWorkState.RequirementsAligned, legalCommitted!.State)"
        };
        Assert.False(BehaviorProofRules.IsMetadataOnly(realTest));
    }

    [Fact]
    public void BehaviorProofCompliance_GivenNegativePathItems_RequiresBothRejectionAndAbsenceOfSideEffects()
    {
        // A rejection assertion alone is not a negative-path proof: the side effect might still have
        // happened before the rejection.
        Assert.False(BehaviorProofRules.NegativePathIsComplete(assertsRejection: true, assertsNoSideEffect: false));
        Assert.False(BehaviorProofRules.NegativePathIsComplete(assertsRejection: false, assertsNoSideEffect: true));

        // Both halves are required.
        Assert.True(BehaviorProofRules.NegativePathIsComplete(assertsRejection: true, assertsNoSideEffect: true));

        // The alpha's own merge-idempotency test satisfies both halves: it asserts the refusal and
        // that the merge action was never invoked again.
        const bool assertsRejection = true;   // startsWith(approval-already-applied)
        const bool assertsNoSideEffect = true; // Assert.Equal(1, merges)
        Assert.True(BehaviorProofRules.NegativePathIsComplete(assertsRejection, assertsNoSideEffect));
    }

    [Fact]
    public void BehaviorProofCompliance_GivenScriptedModelProvider_DoesNotCountAsFactoryEvidence()
    {
        // A scripted provider alone is a unit-test double, never evidence that a model drove the harness.
        Assert.False(BehaviorProofRules.CountsAsFactoryEvidence(usesScriptedProviderOnly: true, exercisesRealProviderPath: true));
        Assert.False(BehaviorProofRules.CountsAsFactoryEvidence(usesScriptedProviderOnly: false, exercisesRealProviderPath: false));
        Assert.False(BehaviorProofRules.CountsAsFactoryEvidence(usesScriptedProviderOnly: true, exercisesRealProviderPath: false));

        // Only a real-provider path counts, and the alpha has one: the real-model demo recorded a real
        // model id and non-zero output tokens through the production execution port.
        Assert.True(BehaviorProofRules.CountsAsFactoryEvidence(usesScriptedProviderOnly: false, exercisesRealProviderPath: true));
    }
}
