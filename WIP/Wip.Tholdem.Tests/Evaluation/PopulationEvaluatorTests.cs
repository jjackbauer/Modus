using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Evaluation;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Evaluation;

/// <summary>
/// Round-two paired four-seat evaluator contract: exact self-comparison, byte-identical reruns, a
/// known dominated control with a positive lower bound, the stable refusal codes, and one reported
/// focal-seat unit per block under focal rotations.
/// </summary>
public sealed class PopulationEvaluatorTests
{
    private const string NitPolicyHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string CallAnyPolicyHash =
        "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    private const string FoldOnlyPolicyHash =
        "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    [Fact]
    public void Evaluate_GivenPolicyVersusItself_ReportsExactlyZero()
    {
        var plan = Plan(blocks: 6, handsPerSeatMatch: 4, compositions: new[] { "primary-mixed" });

        var report = Evaluate(
            plan,
            FrozenOpponentPool.SelectionV1,
            Nit("nit-challenger"),
            "nit-challenger",
            NitPolicyHash,
            Nit("nit-incumbent"),
            "nit-incumbent",
            NitPolicyHash);

        Assert.Equal(PopulationMatchEvaluator.EvaluatorVersion, report.EvaluatorVersion);
        Assert.Equal("tholdem.population-report.v2", PopulationEvaluationReport.Schema);
        var canonical = report.ToCanonicalJson();
        Assert.Contains("\"schema\":\"tholdem.population-report.v2\"", canonical);
        AssertTopLevelKeyOrder(canonical);
        Assert.Equal(0.0, report.Estimate.MeanBbPer100);
        Assert.Equal(0.0, report.Estimate.LowerBoundBbPer100);
        Assert.Equal(0.0, report.Estimate.UpperBoundBbPer100);
        Assert.Equal(6, report.Units.Count);
        Assert.All(report.Units, unit => Assert.Equal(0.0, unit.MeanBbPer100));
        Assert.All(report.Units, unit => Assert.Matches("^[0-9a-f]{16}$", unit.SeedFingerprint));
        Assert.All(report.PerComposition, composition => Assert.Equal(0.0, composition.Estimate.MeanBbPer100));
        Assert.All(report.PerSeat, seat => Assert.Equal(0.0, seat.Estimate.MeanBbPer100));
        Assert.Equal(report.ChallengerNetChips, report.IncumbentNetChips);
        Assert.All(
            report.PerComposition,
            composition => Assert.Equal(
                composition.ChallengerNetChips,
                composition.IncumbentNetChips));
        Assert.Equal(0.0, report.Tail.WorstBlockBbPer100);
        Assert.Equal(0.0, report.Tail.P05BlockBbPer100);
        Assert.Equal(0.0, report.Tail.WorstHandBbPer100);
    }

    [Fact]
    public void Evaluate_GivenSameInputs_IsByteIdentical()
    {
        var plan = Plan(blocks: 6, handsPerSeatMatch: 4, compositions: new[] { "primary-mixed" });

        var first = Evaluate(
            plan,
            FrozenOpponentPool.SelectionV1,
            Nit("determinism-a"),
            "determinism-a",
            NitPolicyHash,
            Nit("determinism-b"),
            "determinism-b",
            NitPolicyHash);
        var second = Evaluate(
            plan,
            FrozenOpponentPool.SelectionV1,
            Nit("determinism-a"),
            "determinism-a",
            NitPolicyHash,
            Nit("determinism-b"),
            "determinism-b",
            NitPolicyHash);

        Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
        Assert.Equal(first.ReportHash, second.ReportHash);
        Assert.Equal(StrategyArtifact.ComputeHash(first.ToCanonicalJson()), first.ReportHash);
        Assert.Matches("^[0-9a-f]{64}$", first.ReportHash);
    }

    [Fact]
    public void Evaluate_GivenKnownDominatedControl_ReportsPositiveLowerBound()
    {
        var plan = Plan(blocks: 96, handsPerSeatMatch: 20, compositions: new[] { "three-tag" });

        var report = Evaluate(
            plan,
            FrozenOpponentPool.SelectionV1,
            new FoldOnlyAgent("fold-only-challenger"),
            "fold-only-challenger",
            FoldOnlyPolicyHash,
            new CallAnyAgent("call-any-incumbent"),
            "call-any-incumbent",
            CallAnyPolicyHash);

        Console.WriteLine(
            $"dominated-control mean={report.Estimate.MeanBbPer100:R} "
            + $"lower={report.Estimate.LowerBoundBbPer100:R} "
            + $"upper={report.Estimate.UpperBoundBbPer100:R} "
            + $"units={report.Units.Count} blocks={report.SeedBlocks}");

        Assert.True(
            report.Estimate.MeanBbPer100 > 0.0,
            $"observed mean {report.Estimate.MeanBbPer100:R}");
        Assert.True(
            report.Estimate.LowerBoundBbPer100 > 0.0,
            $"observed lower bound {report.Estimate.LowerBoundBbPer100:R}");
    }

    [Fact]
    public void Evaluate_GivenPoolMismatch_Throws()
    {
        var plan = Plan(blocks: 6, handsPerSeatMatch: 4, compositions: new[] { "primary-mixed" });

        var exception = Assert.Throws<ArgumentException>(() => Evaluate(
            plan,
            FrozenOpponentPool.HoldoutV1,
            Nit("pool-mismatch-a"),
            "pool-mismatch-a",
            NitPolicyHash,
            Nit("pool-mismatch-b"),
            "pool-mismatch-b",
            NitPolicyHash));

        Assert.Contains("pool-mismatch", exception.Message);
    }

    [Fact]
    public void Evaluate_GivenCompositionMismatch_Throws()
    {
        var plan = Plan(blocks: 6, handsPerSeatMatch: 4, compositions: new[] { "missing-composition" });

        var exception = Assert.Throws<ArgumentException>(() => Evaluate(
            plan,
            FrozenOpponentPool.SelectionV1,
            Nit("composition-mismatch-a"),
            "composition-mismatch-a",
            NitPolicyHash,
            Nit("composition-mismatch-b"),
            "composition-mismatch-b",
            NitPolicyHash));

        Assert.Contains("composition-mismatch", exception.Message);
    }

    [Fact]
    public void Evaluate_GivenFocalRotations_ReportsEveryFocalSeat()
    {
        const int blocks = 6;
        const int hands = 4;
        var compositions = new[] { "primary-mixed", "three-tag" };
        var plan = Plan(blocks, hands, compositions);

        var report = Evaluate(
            plan,
            FrozenOpponentPool.SelectionV1,
            Nit("rotation-a"),
            "rotation-a",
            NitPolicyHash,
            Nit("rotation-b"),
            "rotation-b",
            NitPolicyHash);

        Assert.Equal("FocalRotations", report.SeatPermutationMode);
        Assert.Equal(4, report.RotationsPerBlock);
        Assert.Equal(blocks * compositions.Length, report.Units.Count);
        Assert.Equal(4, report.PerSeat.Count);
        Assert.All(report.PerSeat, seat => Assert.Equal(blocks, seat.Estimate.UnitCount));
        Assert.All(report.PerSeat, seat => Assert.Equal(blocks * compositions.Length * hands, seat.Hands));
        Assert.Equal(Enumerable.Range(0, 4), report.PerSeat.Select(seat => seat.Seat));
        Assert.Equal(blocks, report.Estimate.UnitCount);
        Assert.Equal(compositions.Length, report.PerComposition.Count);
        Assert.All(report.PerComposition, composition => Assert.Equal(blocks, composition.Estimate.UnitCount));
    }

    private static void AssertTopLevelKeyOrder(string canonical)
    {
        var keys = new[]
        {
            "schema", "challengerId", "incumbentId", "challengerPolicyHash",
            "incumbentPolicyHash", "planHash", "gameConfigHash", "opponentPoolId",
            "opponentPoolHash", "evaluatorVersion", "seedBlocks", "handsPerSeatMatch",
            "seatPermutationMode", "rotationsPerBlock", "estimate", "units",
            "perComposition", "perSeat", "tail", "challengerNetChips", "incumbentNetChips",
        };

        var position = -1;
        foreach (var key in keys)
        {
            var index = canonical.IndexOf("\"" + key + "\":", position + 1, StringComparison.Ordinal);
            Assert.True(index > position, $"top-level key '{key}' is missing or out of order");
            position = index;
        }
    }

    private static PopulationEvaluationReport Evaluate(
        EvaluationPlanV2 plan,
        FrozenOpponentPool pool,
        IPokerAgent challenger,
        string challengerId,
        string challengerPolicyHash,
        IPokerAgent incumbent,
        string incumbentId,
        string incumbentPolicyHash,
        SeatPermutationMode seatMode = SeatPermutationMode.FocalRotations)
        => PopulationMatchEvaluator.Evaluate(
            new PopulationEvaluationRequest(plan, pool, seatMode),
            challenger,
            challengerId,
            challengerPolicyHash,
            incumbent,
            incumbentId,
            incumbentPolicyHash);

    private static StrategyAgent Nit(string name) =>
        new(new StrategyGenome(0.90, 0.85, 0.00, 1.0), name);

    private static EvaluationPlanV2 Plan(
        int blocks,
        int handsPerSeatMatch,
        IReadOnlyList<string> compositions)
        => EvaluationPlanV2.Create(
            GameConfiguration.FourSeatCash(),
            FrozenOpponentPool.SelectionPoolId,
            compositions,
            new[]
            {
                new SeedDomain("training", SeedPurpose.Training, 101UL, 4),
                new SeedDomain("selection", SeedPurpose.Selection, 202UL, 4),
                new SeedDomain("holdout", SeedPurpose.Holdout, 500UL, blocks),
                new SeedDomain("audit", SeedPurpose.Audit, 404UL, 4),
            },
            handsPerSeatMatch,
            2.0,
            0.95,
            5,
            2.0);

    /// <summary>Test-only control: fold to every bet, check when checked to.</summary>
    private sealed class FoldOnlyAgent : IPokerAgent
    {
        public FoldOnlyAgent(string name) => Name = name;

        public string Name { get; }

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.LegalActionKinds.Contains(ActionKind.Fold))
            {
                return ValueTask.FromResult(PokerAction.Fold());
            }

            if (observation.LegalActionKinds.Contains(ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }

            return ValueTask.FromResult(PokerAction.Call());
        }
    }

    /// <summary>Test-only control: call every bet, check when checked to, never raise.</summary>
    private sealed class CallAnyAgent : IPokerAgent
    {
        public CallAnyAgent(string name) => Name = name;

        public string Name { get; }

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.LegalActionKinds.Contains(ActionKind.Call))
            {
                return ValueTask.FromResult(PokerAction.Call());
            }

            if (observation.LegalActionKinds.Contains(ActionKind.Check))
            {
                return ValueTask.FromResult(PokerAction.Check());
            }

            if (observation.LegalActionKinds.Contains(ActionKind.AllIn))
            {
                return ValueTask.FromResult(PokerAction.AllIn());
            }

            return ValueTask.FromResult(PokerAction.Fold());
        }
    }
}
