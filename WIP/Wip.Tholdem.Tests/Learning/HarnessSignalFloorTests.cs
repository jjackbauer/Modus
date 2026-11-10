using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// F1 positive-evidence signal floor: the policy-level filter is inclusive at the configured floor,
/// the default 0 keeps a rate-0 signal eligible (today's behaviour), the fallback is the unchanged
/// legacy proposer, and the option reaches the policy only through the existing construction site.
/// </summary>
public sealed class HarnessSignalFloorTests
{
    /// <summary>The seed of the recorded three-iteration pinned arm.</summary>
    private const ulong PinnedSeed = 20_261_111UL;

    private const string DefaultChampionId = "tholdem-strategy-v1-f100e793";

    private const string PinnedFirstChallengerId = "tholdem-strategy-v2-d24dcf6d";

    private static readonly string[] PinnedChallengers =
    [
        "tholdem-strategy-v2-d24dcf6d",
        "tholdem-strategy-v2-c34333fe",
        "tholdem-strategy-v2-86f42856",
    ];

    [Fact]
    public void Policy_GivenZeroRateSignalAndDefaultFloor_ExpectedSignalEdit()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 0, 10, 0.0, FailureSignalDirections.High),
        ]);
        var policy = new SignatureDrivenRefinementPolicy();

        var proposal = policy.Propose(new RefinementContext(incumbent, analysis, signatures, Iteration: 1));

        Assert.Equal(0.0, policy.MinimumSignalRate, 12);
        Assert.Equal(RefinementRationaleCodes.FoldToRaise, proposal.RationaleCode);
        Assert.Equal(incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step, proposal.Genome.CallThreshold, 12);
        Assert.Equal(incumbent.RaiseThreshold, proposal.Genome.RaiseThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, proposal.Genome.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, proposal.Genome.SizingMultiplier, 12);
        Assert.Equal(incumbent.PromptText, proposal.Genome.PromptText);
        Assert.NotEqual(new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1), proposal.Genome);
    }

    [Fact]
    public void Policy_GivenZeroRateSignalAndPositiveFloor_ExpectedLegacyFallback()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 0, 10, 0.0, FailureSignalDirections.High),
        ]);
        var policy = new SignatureDrivenRefinementPolicy(minimumSignalRate: 0.10);

        var proposal = policy.Propose(new RefinementContext(incumbent, analysis, signatures, Iteration: 1));

        var expected = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);
        Assert.Equal(0.10, policy.MinimumSignalRate, 12);
        Assert.Equal(expected, proposal.Genome);
        Assert.Equal(RefinementRationaleCodes.LegacySessionDriven, proposal.RationaleCode);
        Assert.Equal(incumbent.RaiseThreshold - DeterministicStrategyProposer.Step, proposal.Genome.RaiseThreshold, 12);
        Assert.NotEqual(
            incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step,
            proposal.Genome.CallThreshold,
            12);
    }

    [Fact]
    public void Policy_GivenSignalAboveFloor_ExpectedSignalEdit()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 2, 10, 0.20, FailureSignalDirections.High),
        ]);
        var policy = new SignatureDrivenRefinementPolicy(minimumSignalRate: 0.10);

        var proposal = policy.Propose(new RefinementContext(incumbent, analysis, signatures, Iteration: 1));

        Assert.Equal(RefinementRationaleCodes.FoldToRaise, proposal.RationaleCode);
        Assert.Equal(incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step, proposal.Genome.CallThreshold, 12);
        Assert.Equal(incumbent.RaiseThreshold, proposal.Genome.RaiseThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, proposal.Genome.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, proposal.Genome.SizingMultiplier, 12);
    }

    [Fact]
    public void Policy_GivenSignalRateExactlyAtFloor_ExpectedSignalEdit()
    {
        var incumbent = new StrategyGenome();
        var analysis = LosingAnalysis();
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 1, 10, 0.10, FailureSignalDirections.High),
        ]);

        var proposal = new SignatureDrivenRefinementPolicy(minimumSignalRate: 0.10)
            .Propose(new RefinementContext(incumbent, analysis, signatures, Iteration: 1));

        Assert.Equal(RefinementRationaleCodes.FoldToRaise, proposal.RationaleCode);
        Assert.Equal(incumbent.CallThreshold - SignatureDrivenRefinementPolicy.Step, proposal.Genome.CallThreshold, 12);
    }

    [Fact]
    public void Policy_GivenFloorAboveOneOrBelowZero_ExpectedStableRefusal()
    {
        var belowZero = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignatureDrivenRefinementPolicy(minimumSignalRate: -0.0001));
        Assert.Equal("minimumSignalRate", belowZero.ParamName);

        var aboveOne = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignatureDrivenRefinementPolicy(minimumSignalRate: 1.0001));
        Assert.Equal("minimumSignalRate", aboveOne.ParamName);

        var notANumber = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignatureDrivenRefinementPolicy(minimumSignalRate: double.NaN));
        Assert.Equal("minimumSignalRate", notANumber.ParamName);

        var infinite = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignatureDrivenRefinementPolicy(minimumSignalRate: double.PositiveInfinity));
        Assert.Equal("minimumSignalRate", infinite.ParamName);
    }

    [Fact]
    public void Run_GivenDefaultFloor_ExpectedPinnedChallengersUnchanged()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("floor-pinned-default");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: PinnedSeed,
                    Iterations: 3,
                    TrainingHands: 24,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true));

            Assert.Null(run.Bootstrap);
            Assert.Equal(0.0, run.Options.SignalRateFloor, 12);
            Assert.Equal(
                new[] { DefaultChampionId, DefaultChampionId, DefaultChampionId },
                run.Iterations.Select(static iteration => iteration.ChampionId).ToArray());
            Assert.Equal(PinnedChallengers, run.Iterations.Select(static iteration => iteration.ChallengerId).ToArray());
            Assert.All(run.Iterations, static iteration => Assert.Equal(TrainingLoop.RejectedOutcome, iteration.Outcome));
            Assert.All(run.Iterations, static iteration => Assert.NotNull(iteration.Report));
            Assert.Equal(DefaultChampionId, run.FinalChampionId);

            var firstChallenger = TrainingLoop.ReadPersistedStrategy(dataDirectory, PinnedFirstChallengerId);
            Assert.Equal(PinnedFirstChallengerId, firstChallenger.Id);
            var champion = TrainingLoop.ReadPersistedStrategy(dataDirectory, DefaultChampionId);
            Assert.Equal(DefaultChampionId, firstChallenger.ParentId);
            Assert.Equal(2, firstChallenger.Version);
            Assert.Equal(champion.Genome.BluffFrequency + SignatureDrivenRefinementPolicy.Step, firstChallenger.Genome.BluffFrequency, 12);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSignalFloorOption_ExpectedOptionReachesPolicy()
    {
        // Probe-recorded for this seed/iteration (24 hands): the only signal with Sample >= 10 is
        // aggression-by-street (count 9, sample 35, rate 0.2571428571428571); every other signal has
        // Sample < 10. Floor 1.0 therefore excludes every usable signal, so the run must fall through
        // to the legacy proposer and produce exactly the legacy challenger. Under the default floor 0
        // the same seed produces the recorded signal-edit challenger d24dcf6d; because the default
        // challenger differs from the legacy challenger, equality in the floored run is evidence that
        // the non-default option reached the policy.
        var legacyDirectory = AgentTestSupport.CreateTempDirectory("floor-run-legacy");
        var defaultDirectory = AgentTestSupport.CreateTempDirectory("floor-run-default");
        var floorDirectory = AgentTestSupport.CreateTempDirectory("floor-run-floor");
        try
        {
            var options = new TrainingOptions(
                Seed: PinnedSeed,
                Iterations: 1,
                TrainingHands: 24,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2);

            var legacy = TrainingLoop.Run(options with { DataDirectory = legacyDirectory });
            var defaultRefined = TrainingLoop.Run(options with
            {
                DataDirectory = defaultDirectory,
                HarnessRefinement = true,
            });
            var floored = TrainingLoop.Run(options with
            {
                DataDirectory = floorDirectory,
                HarnessRefinement = true,
                SignalRateFloor = 1.0,
            });

            var legacyIteration = Assert.Single(legacy.Iterations);
            var defaultIteration = Assert.Single(defaultRefined.Iterations);
            var flooredIteration = Assert.Single(floored.Iterations);

            Assert.Equal(1.0, floored.Options.SignalRateFloor, 12);
            Assert.Equal(PinnedFirstChallengerId, defaultIteration.ChallengerId);
            Assert.NotEqual(defaultIteration.ChallengerId, legacyIteration.ChallengerId);
            Assert.Equal(legacyIteration.ChallengerId, flooredIteration.ChallengerId);
            Assert.Equal(legacyIteration.ChampionId, flooredIteration.ChampionId);
            Assert.Equal(DefaultChampionId, flooredIteration.ChampionId);

            var legacyChallenger = TrainingLoop.ReadPersistedStrategy(legacyDirectory, legacyIteration.ChallengerId);
            var flooredChallenger = TrainingLoop.ReadPersistedStrategy(floorDirectory, flooredIteration.ChallengerId);
            Assert.Equal(legacyChallenger.Id, flooredChallenger.Id);
            Assert.Equal(2, flooredChallenger.Version);
            Assert.Equal(flooredIteration.ChampionId, flooredChallenger.ParentId);
            Assert.Equal(legacyChallenger.ToCanonicalJson(), flooredChallenger.ToCanonicalJson());

            Assert.NotNull(flooredIteration.Report);
            Assert.Equal(legacyIteration.Report!.ToCanonicalJson(), flooredIteration.Report!.ToCanonicalJson());
            var evaluationPath = Path.Combine(
                floorDirectory,
                "evaluations",
                flooredIteration.ChallengerId + "-vs-" + flooredIteration.ChampionId + ".json");
            Assert.True(File.Exists(evaluationPath), evaluationPath);
            Assert.Equal(flooredIteration.Report!.ToCanonicalJson(), File.ReadAllText(evaluationPath));
        }
        finally
        {
            Directory.Delete(legacyDirectory, recursive: true);
            Directory.Delete(defaultDirectory, recursive: true);
            Directory.Delete(floorDirectory, recursive: true);
        }
    }

    private static SessionAnalysis LosingAnalysis() =>
        new(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);
}