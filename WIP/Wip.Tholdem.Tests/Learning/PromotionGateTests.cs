using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class PromotionGateTests
{
    [Fact]
    public void Decide_GivenLowerBoundAboveMarginAndEnoughSeeds_ExpectedPromoted()
    {
        var criteria = new EvaluationCriteria();
        var report = LearningTestSupport.SyntheticReport(lowerBound: 6.0, seeds: 24);

        var decision = new StatisticalPromotionPolicy().Decide(report, criteria);

        Assert.True(decision.Promoted);
        Assert.Equal("promoted", decision.Reason);
        Assert.Equal(6.0, decision.LowerBoundBbPer100);
        Assert.Equal(5.0, decision.MarginBbPer100);
        Assert.Equal(24, decision.Seeds);
    }

    [Fact]
    public void Decide_GivenFailingCriterion_ExpectedDocumentedRejections()
    {
        var criteria = new EvaluationCriteria();
        var policy = new StatisticalPromotionPolicy();

        var belowMargin = policy.Decide(LearningTestSupport.SyntheticReport(lowerBound: 4.9, seeds: 24), criteria);
        Assert.False(belowMargin.Promoted);
        Assert.Equal("lcb-below-margin", belowMargin.Reason);

        var insufficient = policy.Decide(LearningTestSupport.SyntheticReport(lowerBound: 6.0, seeds: 23), criteria);
        Assert.False(insufficient.Promoted);
        Assert.Equal("insufficient-seeds", insufficient.Reason);

        // The gate is strict: exactly at the margin is not a promotion.
        var exactlyAtMargin = policy.Decide(LearningTestSupport.SyntheticReport(lowerBound: 5.0, seeds: 24), criteria);
        Assert.False(exactlyAtMargin.Promoted);
        Assert.Equal("lcb-below-margin", exactlyAtMargin.Reason);
        Assert.Equal(5.0, exactlyAtMargin.MarginBbPer100);
    }

    [Fact]
    public void AlwaysPromotePolicy_Decide_ExpectedPromotedRegardlessOfReport()
    {
        var criteria = new EvaluationCriteria();
        var decision = new AlwaysPromotePolicy()
            .Decide(LearningTestSupport.SyntheticReport(lowerBound: -12.0, seeds: 4), criteria);

        Assert.True(decision.Promoted);
        Assert.Equal("always-promote-control", decision.Reason);
        Assert.Equal(-12.0, decision.LowerBoundBbPer100);
        Assert.Equal(4, decision.Seeds);
    }

    [Fact]
    public void Verify_GivenRederivedCriterion_ExpectedControlsRejectedAndPassesAccepted()
    {
        var criteria = new EvaluationCriteria();

        // An always-promote control over a report that fails the margin must not verify.
        var failing = LearningTestSupport.SyntheticReport(lowerBound: 4.9, seeds: 24);
        var alwaysPromote = new AlwaysPromotePolicy().Decide(failing, criteria);
        Assert.True(alwaysPromote.Promoted);
        Assert.False(PromotionVerifier.Verify(alwaysPromote, failing, criteria, out var lowBoundReason));
        Assert.Equal("verifier:lcb-below-margin", lowBoundReason);

        var tooFewSeeds = LearningTestSupport.SyntheticReport(lowerBound: 9.0, seeds: 23);
        var seedControl = new AlwaysPromotePolicy().Decide(tooFewSeeds, criteria);
        Assert.False(PromotionVerifier.Verify(seedControl, tooFewSeeds, criteria, out var seedReason));
        Assert.Equal("verifier:insufficient-seeds", seedReason);

        // A promoted decision that genuinely satisfies both conditions verifies.
        var passing = LearningTestSupport.SyntheticReport(lowerBound: 6.0, seeds: 24);
        var honest = new StatisticalPromotionPolicy().Decide(passing, criteria);
        Assert.True(honest.Promoted);
        Assert.True(PromotionVerifier.Verify(honest, passing, criteria, out var verifiedReason));
        Assert.Equal("verified", verifiedReason);

        // An unjustified rejection (the report does clear the gate) is also caught.
        var unjustified = new StatisticalPromotionPolicy().Decide(passing, criteria) with { Promoted = false, Reason = "rejected-on-purpose" };
        Assert.False(PromotionVerifier.Verify(unjustified, passing, criteria, out var rejectionReason));
        Assert.Equal("verifier:unjustified-rejection", rejectionReason);

        // A justified rejection verifies true.
        var justified = new StatisticalPromotionPolicy().Decide(failing, criteria);
        Assert.False(justified.Promoted);
        Assert.True(PromotionVerifier.Verify(justified, failing, criteria, out var justifiedReason));
        Assert.Equal("rejection-justified", justifiedReason);
    }
}
