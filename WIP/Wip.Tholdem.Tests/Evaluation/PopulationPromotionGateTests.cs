using Wip.Tholdem.Agents.Evaluation;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// The pre-registered, multiplicity-aware, block-level promotion gate: it validates the report and
/// plan manifests, applies the Bonferroni-adjusted confidence, and recomputes every bound from
/// <see cref="PopulationEvaluationReport.Units"/> instead of trusting the stored estimate.
/// </summary>
public sealed class PopulationPromotionGateTests
{
    private const string ChallengerPolicyHash =
        "1111111111111111111111111111111111111111111111111111111111111111";

    private const string IncumbentPolicyHash =
        "2222222222222222222222222222222222222222222222222222222222222222";

    private const string OpponentPoolHash =
        "3333333333333333333333333333333333333333333333333333333333333333";

    // Deliberately implausible: a gate that trusted the stored estimate instead of recomputing the
    // block means from Units would promote the self-comparison case below.
    private const double StoredEstimateTrap = 12345.0;

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
    public void Decide_GivenSelfComparison_RejectsAsNoImprovement()
    {
        var plan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var anchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var primary = Report(plan, Zeros());
        var anchor = Report(anchorPlan, Zeros());

        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.Rejected, decision.Verdict);
        Assert.Equal(PopulationPromotionGate.NoImprovementReason, decision.Reason);
    }

    [Fact]
    public void Decide_GivenStrongPositiveLowerBound_Verifies()
    {
        var plan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var anchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var primary = Report(plan, Cycle(19.0));
        var anchor = Report(anchorPlan, Cycle(9.0));

        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.VerifiedImprovement, decision.Verdict);
        Assert.Equal(PopulationPromotionGate.VerifiedReason, decision.Reason);
    }

    [Fact]
    public void Decide_GivenLowerBoundBetweenZeroAndMargin_IsInconclusive()
    {
        var plan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var anchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var primary = Report(plan, Cycle(1.0));
        var anchor = Report(anchorPlan, Zeros());

        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.Inconclusive, decision.Verdict);
        Assert.Equal(PopulationPromotionGate.NearThresholdReason, decision.Reason);
    }

    [Fact]
    public void Decide_GivenAnchorRegression_IsUnsafe()
    {
        var plan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var anchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var primary = Report(plan, Cycle(19.0));
        var anchor = Report(anchorPlan, Enumerable.Range(0, 24).Select(block => -11.0 + (block % 3)).ToArray());

        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.Unsafe, decision.Verdict);
        Assert.Equal(PopulationPromotionGate.AnchorRegressionReason, decision.Reason);
    }

    [Fact]
    public void Decide_GivenPlanHashMismatch_IsUnverified()
    {
        var plan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var anchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var primary = Report(plan, Zeros(), planHash: new string('0', 64));
        var anchor = Report(anchorPlan, Zeros());

        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.Unverified, decision.Verdict);
        Assert.Equal(PopulationPromotionGate.ManifestMismatchReason, decision.Reason);
    }

    [Fact]
    public void Decide_GivenUnitCountMismatch_IsUnverified()
    {
        var plan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var anchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var primary = Report(plan, Zeros(23));
        var anchor = Report(anchorPlan, Zeros());

        var decision = PopulationPromotionGate.Decide(primary, anchor, plan, anchorPlan);

        Assert.Equal(PromotionVerdict.Unverified, decision.Verdict);
        Assert.Equal(PopulationPromotionGate.UnitCountMismatchReason, decision.Reason);
    }

    [Fact]
    public void Decide_GivenTwoCandidates_AppliesBonferroni()
    {
        var onePlan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 1);
        var oneAnchorPlan = CreateAnchorPlan(plannedCandidates: 1);
        var twoPlan = CreatePlan("pool-a", "primary-mixed", plannedCandidates: 2);
        var twoAnchorPlan = CreateAnchorPlan(plannedCandidates: 2);
        var units = Cycle(4.0);

        var one = PopulationPromotionGate.Decide(
            Report(onePlan, units),
            Report(oneAnchorPlan, Zeros()),
            onePlan,
            oneAnchorPlan);
        var two = PopulationPromotionGate.Decide(
            Report(twoPlan, units),
            Report(twoAnchorPlan, Zeros()),
            twoPlan,
            twoAnchorPlan);

        Assert.Equal(0.975, two.AdjustedConfidenceLevel);
        Assert.True(
            two.LowerBoundBbPer100 < one.LowerBoundBbPer100,
            $"two-candidate lower bound {two.LowerBoundBbPer100:R} must be below "
            + $"one-candidate lower bound {one.LowerBoundBbPer100:R}");
    }

    private static EvaluationPlanV2 CreatePlan(
        string opponentPoolId,
        string composition,
        int plannedCandidates) =>
        EvaluationPlanV2.Create(
            Game,
            opponentPoolId,
            new[] { composition },
            new[] { Training, Selection, Holdout, Audit },
            handsPerSeatMatch: 20,
            minimumPracticalEffectBbPer100: 5.0,
            confidenceLevel: 0.95,
            plannedCandidates: plannedCandidates,
            anchorRegressionToleranceBbPer100: 5.0);

    private static EvaluationPlanV2 CreateAnchorPlan(int plannedCandidates) =>
        CreatePlan("pool-anchor", "three-tag", plannedCandidates);

    private static PopulationEvaluationReport Report(
        EvaluationPlanV2 plan,
        IReadOnlyList<double> blockMeans,
        string? planHash = null)
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

        var estimate = new PopulationEstimate(
            StoredEstimateTrap,
            StoredEstimateTrap,
            StoredEstimateTrap,
            0.95,
            "test",
            units.Length,
            0.0,
            StoredEstimateTrap,
            StoredEstimateTrap);

        return new PopulationEvaluationReport(
            ChallengerId: "challenger",
            IncumbentId: "incumbent",
            ChallengerPolicyHash: ChallengerPolicyHash,
            IncumbentPolicyHash: IncumbentPolicyHash,
            PlanHash: planHash ?? plan.PlanHash,
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
