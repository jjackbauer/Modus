using Wip.Tholdem.Agents.Evaluation;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// The independent promotion verifier: with fresh recomputation delegates it proves the claimed
/// reports are the ones the plans require, then proves the supplied decision is exactly the one the
/// gate reaches. It never accepts a decision the reports do not justify.
/// </summary>
public sealed class PopulationVerifierTests
{
    private const string ChallengerPolicyHash =
        "1111111111111111111111111111111111111111111111111111111111111111";

    private const string IncumbentPolicyHash =
        "2222222222222222222222222222222222222222222222222222222222222222";

    private const string OpponentPoolHash =
        "3333333333333333333333333333333333333333333333333333333333333333";

    private static readonly GameConfiguration Game = GameConfiguration.FourSeatCash();

    private static readonly SeedDomain Training =
        new("training-4", SeedPurpose.Training, 0x1001UL, 4);

    private static readonly SeedDomain Selection =
        new("selection-4", SeedPurpose.Selection, 0x1002UL, 4);

    private static readonly SeedDomain Holdout =
        new("holdout-24", SeedPurpose.Holdout, 0x1003UL, 24);

    private static readonly SeedDomain Audit =
        new("audit-4", SeedPurpose.Audit, 0x1004UL, 4);

    [Fact]
    public void Verify_GivenRecomputedReportsAgree_ReturnsVerified()
    {
        var plan = CreatePlan("pool-a", "primary-mixed");
        var anchorPlan = CreateAnchorPlan();
        var primary = Report(plan, Cycle(19.0));
        var anchor = Report(anchorPlan, Cycle(9.0));
        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.VerifiedImprovement, decision.Verdict);

        var accepted = PopulationPromotionVerifier.Verify(
            decision,
            primary,
            anchor,
            plan,
            anchorPlan,
            () => primary,
            () => anchor,
            out var reason);

        Assert.True(accepted, $"the verifier refused an honest verified decision: {reason}");
        Assert.Equal(PopulationPromotionVerifier.VerifiedReason, reason);
    }

    [Fact]
    public void Verify_GivenAlwaysPromoteControl_ReturnsDecisionMismatch()
    {
        var plan = CreatePlan("pool-a", "primary-mixed");
        var anchorPlan = CreateAnchorPlan();
        var primary = Report(plan, Zeros());
        var anchor = Report(anchorPlan, Zeros());
        var gateDecision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.Rejected, gateDecision.Verdict);

        // The control experiment's always-promote arm: the gate rejected the reports, but the
        // supplied decision claims a verified improvement.
        var forced = gateDecision with
        {
            Verdict = PromotionVerdict.VerifiedImprovement,
            Reason = "forced",
        };

        var accepted = PopulationPromotionVerifier.Verify(
            forced,
            primary,
            anchor,
            plan,
            anchorPlan,
            () => primary,
            () => anchor,
            out var reason);

        Assert.False(accepted);
        Assert.Equal(PopulationPromotionVerifier.DecisionMismatchReason, reason);
    }

    [Fact]
    public void Verify_GivenTamperedReportHash_ReturnsReportMismatch()
    {
        var plan = CreatePlan("pool-a", "primary-mixed");
        var anchorPlan = CreateAnchorPlan();
        var primary = Report(plan, Cycle(19.0));
        var anchor = Report(anchorPlan, Cycle(9.0));
        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);
        var tamperedPrimary = Report(plan, Zeros());

        Assert.NotEqual(primary.ReportHash, tamperedPrimary.ReportHash);

        var accepted = PopulationPromotionVerifier.Verify(
            decision,
            primary,
            anchor,
            plan,
            anchorPlan,
            () => tamperedPrimary,
            () => anchor,
            out var reason);

        Assert.False(accepted);
        Assert.Equal(PopulationPromotionVerifier.ReportMismatchReason, reason);
    }

    [Fact]
    public void Verify_GivenRecomputationThrows_ReturnsRecomputationFailed()
    {
        var plan = CreatePlan("pool-a", "primary-mixed");
        var anchorPlan = CreateAnchorPlan();
        var primary = Report(plan, Cycle(19.0));
        var anchor = Report(anchorPlan, Cycle(9.0));
        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        var accepted = PopulationPromotionVerifier.Verify(
            decision,
            primary,
            anchor,
            plan,
            anchorPlan,
            () => throw new InvalidOperationException("recomputation failed"),
            () => anchor,
            out var reason);

        Assert.False(accepted);
        Assert.Equal(PopulationPromotionVerifier.RecomputationFailedReason, reason);
    }

    private static EvaluationPlanV2 CreatePlan(string opponentPoolId, string composition) =>
        EvaluationPlanV2.Create(
            Game,
            opponentPoolId,
            new[] { composition },
            new[] { Training, Selection, Holdout, Audit },
            handsPerSeatMatch: 20,
            minimumPracticalEffectBbPer100: 5.0,
            confidenceLevel: 0.95,
            plannedCandidates: 1,
            anchorRegressionToleranceBbPer100: 5.0);

    private static EvaluationPlanV2 CreateAnchorPlan() =>
        CreatePlan("pool-anchor", "three-tag");

    private static PopulationEvaluationReport Report(
        EvaluationPlanV2 plan,
        IReadOnlyList<double> blockMeans)
    {
        var compositionId = plan.Compositions[0];
        var units = new EvaluationUnit[blockMeans.Count];
        for (var block = 0; block < blockMeans.Count; block++)
        {
            units[block] = new EvaluationUnit(
                compositionId,
                block,
                "0000000000000000",
                20,
                blockMeans[block]);
        }

        // The gate never reads the stored estimate; zero everywhere proves that.
        var estimate = new PopulationEstimate(
            0.0,
            0.0,
            0.0,
            0.95,
            "test",
            units.Length,
            0.0,
            0.0,
            0.0);

        return new PopulationEvaluationReport(
            ChallengerId: "challenger",
            IncumbentId: "incumbent",
            ChallengerPolicyHash: ChallengerPolicyHash,
            IncumbentPolicyHash: IncumbentPolicyHash,
            PlanHash: plan.PlanHash,
            GameConfigHash: plan.Game.ConfigHash,
            OpponentPoolId: plan.OpponentPoolId,
            OpponentPoolHash: OpponentPoolHash,
            EvaluatorVersion: PopulationMatchEvaluator.EvaluatorVersion,
            SeedBlocks: plan.SeedBlocks,
            HandsPerSeatMatch: plan.HandsPerSeatMatch,
            SeatPermutationMode: SeatPermutationMode.FocalRotations.ToString(),
            RotationsPerBlock: 4,
            Estimate: estimate,
            Units: units,
            PerComposition: new[]
            {
                new CompositionEstimate(compositionId, estimate, 0, 0),
            },
            PerSeat: Enumerable.Range(0, 4)
                .Select(seat => new SeatEstimate(seat, estimate, 20))
                .ToArray(),
            Tail: new TailRisk(0.0, 0.0, 0.0),
            ChallengerNetChips: 0,
            IncumbentNetChips: 0);
    }

    private static double[] Zeros(int count = 24) => new double[count];

    private static double[] Cycle(double first, int count = 24) =>
        Enumerable.Range(0, count).Select(block => first + (block % 3)).ToArray();
}
