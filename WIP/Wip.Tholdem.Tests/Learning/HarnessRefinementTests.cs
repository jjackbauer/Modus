using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Runtime behaviour for the opt-in refinement slice: a real training match is written through the
/// history directory, read back through <see cref="HandRecordJson"/>, reduced to exact failure
/// signatures, and turned into a signature-driven genome. Legacy mode is proven by running the real
/// loop twice into two fresh directories and comparing the hash of every persisted JSON file.
/// </summary>
public sealed class HarnessRefinementTests
{
    [Fact]
    public void Capture_GivenTrainingRun_ExpectedAllHandsAndScratchRemoved()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-capture");
        try
        {
            const int Hands = 23;
            const ulong Seed = 20_261_108UL;
            var trainingSeed = SeedDerivation.Derive(
                Seed,
                TrainingLoop.TrainingSeedHandBase + 1,
                0,
                0,
                TrainingLoop.TrainingSeedStream);

            var captured = TrainingTrajectoryCapture.Capture(
                dataDirectory,
                iteration: 1,
                new SimulationOptions(trainingSeed, Hands),
                new StrategyAgent(new StrategyGenome(), "champion"),
                new TightAggressiveAgent(name: "baseline"));

            Assert.Equal(Hands, captured.Report.Hands);
            Assert.Equal(Hands, captured.Hands.Count);
            Assert.Equal(
                Enumerable.Range(1, Hands).ToArray(),
                captured.Hands.Select(static hand => hand.HandNumber).ToArray());
            Assert.All(captured.Hands, static hand => Assert.Equal(2, hand.HoleCards[0].Count));

            var scratch = TrainingTrajectoryCapture.ScratchDirectory(dataDirectory, 1);
            Assert.False(Directory.Exists(scratch), $"The trajectory scratch directory must be removed: {scratch}");

            // The same capture path must be exercised by the real loop, not only by the helper.
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 1,
                    TrainingHands: Hands,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true));
            Assert.Single(run.Iterations);
            Assert.False(Directory.Exists(scratch), $"The loop must remove its trajectory scratch: {scratch}");

            var harnessDirectory = Path.Combine(dataDirectory, "harness");
            if (Directory.Exists(harnessDirectory))
            {
                // memory.json is a persisted product artifact, not trajectory scratch; the intent of
                // this assertion is that no captured hand file survives the scratch cleanup.
                Assert.Empty(Directory.GetFiles(harnessDirectory, "hand-*.json", SearchOption.AllDirectories));
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Extract_GivenSyntheticHands_ExpectedNamedSignals()
    {
        var signatures = FailureSignatureExtractor.Extract(SyntheticHands()).Signals;

        Assert.Equal(
            new[]
            {
                FailureSignalNames.FoldToRaise,
                FailureSignalNames.CallWithWeakHand,
                FailureSignalNames.MissedValue,
                FailureSignalNames.AggressionByStreet,
                FailureSignalNames.ShowdownLoss,
                FailureSignalNames.StackOffLoss,
            },
            signatures.Select(static signal => signal.Name).ToArray());

        // Hand 1 folds immediately to a raise; hands 2 and 6 respond to a raise without folding.
        var foldToRaise = Signal(signatures, FailureSignalNames.FoldToRaise);
        Assert.Equal(1, foldToRaise.Count);
        Assert.Equal(3, foldToRaise.Sample);
        Assert.Equal(1.0 / 3.0, foldToRaise.Rate, 12);
        Assert.Equal(FailureSignalDirections.Low, foldToRaise.Direction);

        // Hands 2 and 6 call; hand 2's final category is HighCard and it lost, hand 6 is trips.
        var weakCall = Signal(signatures, FailureSignalNames.CallWithWeakHand);
        Assert.Equal(1, weakCall.Count);
        Assert.Equal(2, weakCall.Sample);
        Assert.Equal(0.5, weakCall.Rate, 12);
        Assert.Equal(FailureSignalDirections.High, weakCall.Direction);

        // Hands 3 and 6 both finish at trips-or-better and neither bets or raises postflop.
        var missedValue = Signal(signatures, FailureSignalNames.MissedValue);
        Assert.Equal(2, missedValue.Count);
        Assert.Equal(2, missedValue.Sample);
        Assert.Equal(1.0, missedValue.Rate, 12);
        Assert.Equal(FailureSignalDirections.High, missedValue.Direction);

        // Postflop seat-0 actions: hand 2 call, hand 3 three checks, hand 4 two bets, hand 6 one call.
        var aggression = Signal(signatures, FailureSignalNames.AggressionByStreet);
        Assert.Equal(2, aggression.Count);
        Assert.Equal(7, aggression.Sample);
        Assert.Equal(2.0 / 7.0, aggression.Rate, 12);
        Assert.Equal(FailureSignalDirections.Low, aggression.Direction);

        // Showdowns: hands 2, 3, 5 and 6; seat 0 loses hands 2, 5 and 6.
        var showdown = Signal(signatures, FailureSignalNames.ShowdownLoss);
        Assert.Equal(3, showdown.Count);
        Assert.Equal(4, showdown.Sample);
        Assert.Equal(0.75, showdown.Rate, 12);
        Assert.Equal(FailureSignalDirections.High, showdown.Direction);

        // Only hand 5 puts seat 0 all-in, and that hand busts without a win.
        var stackOff = Signal(signatures, FailureSignalNames.StackOffLoss);
        Assert.Equal(1, stackOff.Count);
        Assert.Equal(1, stackOff.Sample);
        Assert.Equal(1.0, stackOff.Rate, 12);
        Assert.Equal(FailureSignalDirections.High, stackOff.Direction);
    }

    [Fact]
    public void Extract_GivenHands_ExpectedNoSeedCardOrNameInCanonicalJson()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-anonymous");
        try
        {
            const ulong Seed = 9_876_543_210_123_456_789UL;
            var captured = TrainingTrajectoryCapture.Capture(
                dataDirectory,
                iteration: 1,
                new SimulationOptions(Seed, 9),
                new StrategyAgent(new StrategyGenome(), "champion"),
                new TightAggressiveAgent(name: "baseline"));

            var signatures = FailureSignatureExtractor.Extract(captured.Hands);
            var json = signatures.ToCanonicalJson();

            Assert.Equal(json, FailureSignatureExtractor.Extract(captured.Hands).ToCanonicalJson());
            Assert.DoesNotContain(Seed.ToString(CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
            Assert.DoesNotContain("champion", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("baseline", json, StringComparison.OrdinalIgnoreCase);

            foreach (var hand in captured.Hands)
            {
                Assert.DoesNotContain(
                    hand.Seed.ToString(CultureInfo.InvariantCulture),
                    json,
                    StringComparison.Ordinal);
                foreach (var card in hand.HoleCards[0].Concat(hand.HoleCards[1]).Concat(hand.Board))
                {
                    Assert.DoesNotContain(card.ToString(), json, StringComparison.Ordinal);
                }
            }

            using var document = JsonDocument.Parse(json);
            var signalElements = document.RootElement.GetProperty("signals").EnumerateArray().ToArray();
            Assert.Equal(6, signalElements.Length);
            foreach (var element in signalElements)
            {
                Assert.Equal(
                    new[] { "name", "count", "sample", "rate", "direction" },
                    element.EnumerateObject().Select(static property => property.Name).ToArray());
                Assert.Contains(
                    element.GetProperty("direction").GetString(),
                    new[] { FailureSignalDirections.High, FailureSignalDirections.Low, FailureSignalDirections.Neutral });
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Policy_GivenFoldToRaiseSignal_ExpectedCallThresholdLoweredByStep()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.42,
            BbPer100: -3.5,
            AggressionFactor: 1.2,
            ShowdownRate: 0.4,
            Vpip: 0.0);
        var signatures = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 10, 10, 1.0, FailureSignalDirections.High),
            new FailureSignal(FailureSignalNames.MissedValue, 9, 10, 0.9, FailureSignalDirections.High),
        ]);

        var proposal = new SignatureDrivenRefinementPolicy().Propose(
            new RefinementContext(incumbent, analysis, signatures, Iteration: 1));

        Assert.Equal(incumbent.CallThreshold - 0.08, proposal.Genome.CallThreshold, 12);
        Assert.Equal(incumbent.RaiseThreshold, proposal.Genome.RaiseThreshold, 12);
        Assert.Equal(incumbent.BluffFrequency, proposal.Genome.BluffFrequency, 12);
        Assert.Equal(incumbent.SizingMultiplier, proposal.Genome.SizingMultiplier, 12);
        Assert.Equal(incumbent.PromptText, proposal.Genome.PromptText);
        Assert.Equal(RefinementRationaleCodes.FoldToRaise, proposal.RationaleCode);
        Assert.Equal(0.45, incumbent.CallThreshold, 12);
    }

    [Fact]
    public void Policy_GivenNoSignals_ExpectedLegacySessionDrivenProposal()
    {
        var incumbent = new StrategyGenome();
        var analysis = new SessionAnalysis(
            Hands: 120,
            WinRate: 0.58,
            BbPer100: 14.5,
            AggressionFactor: 1.8,
            ShowdownRate: 0.3,
            Vpip: 0.0);

        var expected = new DeterministicStrategyProposer().Propose(incumbent, analysis, iteration: 1);
        var proposal = new SignatureDrivenRefinementPolicy().Propose(
            new RefinementContext(incumbent, analysis, SessionSignatures.Empty, Iteration: 1));

        Assert.Equal(expected, proposal.Genome);
        Assert.Equal(RefinementRationaleCodes.LegacySessionDriven, proposal.RationaleCode);

        // A signal below the sample floor is also no usable signal.
        var belowFloor = new SessionSignatures(
        [
            new FailureSignal(FailureSignalNames.FoldToRaise, 9, 9, 1.0, FailureSignalDirections.High),
        ]);
        Assert.Equal(
            expected,
            new SignatureDrivenRefinementPolicy()
                .Propose(new RefinementContext(incumbent, analysis, belowFloor, Iteration: 1)).Genome);
    }

    [Fact]
    public void Run_GivenHarnessRefinement_ExpectedSignatureDrivenChallenger()
    {
        var legacyDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-legacy");
        var refinementDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-on");
        try
        {
            var options = new TrainingOptions(
                Seed: 20_261_109UL,
                Iterations: 1,
                TrainingHands: 120,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2);

            var legacyRun = TrainingLoop.Run(options with { DataDirectory = legacyDirectory });
            var refinementRun = TrainingLoop.Run(
                options with { DataDirectory = refinementDirectory, HarnessRefinement = true });

            var legacyIteration = Assert.Single(legacyRun.Iterations);
            var refinementIteration = Assert.Single(refinementRun.Iterations);
            Assert.Equal(legacyIteration.ChampionId, refinementIteration.ChampionId);

            var champion = TrainingLoop.ReadPersistedStrategy(legacyDirectory, legacyIteration.ChampionId);
            var legacyChallenger = TrainingLoop.ReadPersistedStrategy(legacyDirectory, legacyIteration.ChallengerId);
            var refinementChallenger = TrainingLoop.ReadPersistedStrategy(
                refinementDirectory,
                refinementIteration.ChallengerId);

            Assert.False(
                refinementChallenger.Genome.BehaviorEquals(champion.Genome),
                "The signature-driven challenger must differ behaviourally from the champion.");
            Assert.NotEqual(legacyIteration.ChallengerId, refinementIteration.ChallengerId);
            Assert.False(
                refinementChallenger.Genome.BehaviorEquals(legacyChallenger.Genome),
                "The signature-driven challenger must differ behaviourally from the legacy proposer's challenger.");

            var deltas = ChangedDeltas(champion.Genome, refinementChallenger.Genome);
            var delta = Assert.Single(deltas);
            Assert.True(
                Math.Abs(Math.Abs(delta) - SignatureDrivenRefinementPolicy.Step) < 1e-12
                || Math.Abs(Math.Abs(delta) - SignatureDrivenRefinementPolicy.OutcomeStep) < 1e-12,
                $"A signature nudge must move exactly one parameter by 0.08 or 0.06; observed {delta}.");

            Assert.False(Directory.Exists(TrainingTrajectoryCapture.ScratchDirectory(refinementDirectory, 1)));
            Assert.False(Directory.Exists(Path.Combine(legacyDirectory, "harness")));
        }
        finally
        {
            Directory.Delete(legacyDirectory, recursive: true);
            Directory.Delete(refinementDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenLegacyMode_ExpectedSameArtifactsAsSecondLegacyRun()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-equivalence-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-equivalence-b");
        try
        {
            var options = new TrainingOptions(
                Seed: 1_099_511_627_776UL,
                Iterations: 2,
                TrainingHands: 12,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2);

            var first = TrainingLoop.Run(options with { DataDirectory = firstDirectory });
            var second = TrainingLoop.Run(options with { DataDirectory = secondDirectory });

            Assert.Equal(2, first.Iterations.Count);
            Assert.Equal(2, second.Iterations.Count);
            Assert.Equal(first.FinalChampionId, second.FinalChampionId);

            var firstHashes = JsonFileHashes(firstDirectory);
            var secondHashes = JsonFileHashes(secondDirectory);
            Assert.Equal(7, firstHashes.Count);
            Assert.Equal(firstHashes.Keys.ToArray(), secondHashes.Keys.ToArray());
            foreach (var pair in firstHashes)
            {
                Assert.Equal(pair.Value, secondHashes[pair.Key]);
            }

            Assert.False(Directory.Exists(Path.Combine(firstDirectory, "harness")));
            Assert.False(Directory.Exists(Path.Combine(secondDirectory, "harness")));
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenLegacyMode_ExpectedNoTrajectoryScratchArtifacts()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-refinement-legacy-scratch");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: 20_261_110UL,
                    Iterations: 1,
                    TrainingHands: 8,
                    EvaluationSeeds: 1,
                    EvaluationHandsPerMatch: 1,
                    DataDirectory: dataDirectory));

            Assert.Single(run.Iterations);
            Assert.False(Directory.Exists(Path.Combine(dataDirectory, "harness")));
            Assert.Empty(Directory.GetFiles(dataDirectory, "hand-*.json", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static FailureSignal Signal(IReadOnlyList<FailureSignal> signals, string name) =>
        signals.Single(signal => signal.Name == name);

    private static double[] ChangedDeltas(StrategyGenome from, StrategyGenome to)
    {
        var deltas = new List<double>(4);
        if (from.RaiseThreshold != to.RaiseThreshold)
        {
            deltas.Add(to.RaiseThreshold - from.RaiseThreshold);
        }

        if (from.CallThreshold != to.CallThreshold)
        {
            deltas.Add(to.CallThreshold - from.CallThreshold);
        }

        if (from.BluffFrequency != to.BluffFrequency)
        {
            deltas.Add(to.BluffFrequency - from.BluffFrequency);
        }

        if (from.SizingMultiplier != to.SizingMultiplier)
        {
            deltas.Add(to.SizingMultiplier - from.SizingMultiplier);
        }

        return deltas.ToArray();
    }

    private static SortedDictionary<string, string> JsonFileHashes(string root)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            hashes[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }

        return hashes;
    }

    private static IReadOnlyList<HandRecord> SyntheticHands() =>
    [
        SyntheticHand(
            1,
            "As Kh",
            string.Empty,
            [
                (0, 1, "Raise", 30L, 40L),
                (0, 0, "Fold", 0L, 40L),
            ],
            showdown: false,
            winners: [1],
            finalStacks: [990L, 1010L]),
        SyntheticHand(
            2,
            "2c 7d",
            "As Kd 9h 4s 3c",
            [
                (0, 1, "Raise", 30L, 40L),
                (0, 0, "Call", 20L, 60L),
                (1, 1, "Bet", 40L, 100L),
                (1, 0, "Call", 40L, 140L),
            ],
            showdown: true,
            winners: [1],
            finalStacks: [900L, 1100L]),
        SyntheticHand(
            3,
            "Ah Ad",
            "Ac Kd 9h 4s 3c",
            [
                (1, 0, "Check", 0L, 60L),
                (1, 1, "Check", 0L, 60L),
                (2, 0, "Check", 0L, 60L),
                (2, 1, "Check", 0L, 60L),
                (3, 0, "Check", 0L, 60L),
                (3, 1, "Check", 0L, 60L),
            ],
            showdown: true,
            winners: [0],
            finalStacks: [1100L, 900L]),
        SyntheticHand(
            4,
            "Ks Qs",
            "Js Ts 2d 7c 3h",
            [
                (1, 0, "Bet", 50L, 100L),
                (1, 1, "Call", 50L, 150L),
                (2, 0, "Bet", 100L, 250L),
                (2, 1, "Fold", 0L, 250L),
            ],
            showdown: false,
            winners: [0],
            finalStacks: [1150L, 850L]),
        SyntheticHand(
            5,
            "7s 7d",
            "Ah Kc Qd Js 2h",
            [
                (0, 0, "AllIn", 1000L, 1000L),
                (0, 1, "Call", 990L, 1990L),
            ],
            showdown: true,
            winners: [1],
            finalStacks: [0L, 1990L]),
        SyntheticHand(
            6,
            "Kh Kd",
            "Kc 8h 5s 2d 3c",
            [
                (0, 1, "Raise", 30L, 40L),
                (0, 0, "Call", 20L, 60L),
                (1, 1, "Bet", 40L, 100L),
                (1, 0, "Call", 40L, 140L),
            ],
            showdown: true,
            winners: [1],
            finalStacks: [850L, 1150L]),
    ];

    private static HandRecord SyntheticHand(
        int handNumber,
        string holeCardsSeat0,
        string board,
        (int Street, int Seat, string Kind, long Amount, long PotAfter)[] actions,
        bool showdown,
        int[] winners,
        long[] finalStacks)
    {
        var actionRecords = new HandActionRecord[actions.Length];
        for (var index = 0; index < actions.Length; index++)
        {
            actionRecords[index] = new HandActionRecord(
                actions[index].Street,
                actions[index].Seat,
                actions[index].Kind,
                actions[index].Amount,
                actions[index].PotAfter);
        }

        return new HandRecord(
            HandNumber: handNumber,
            Seed: 1000UL + (ulong)handNumber,
            SmallBlind: 5,
            BigBlind: 10,
            StartingStack: 1000,
            ButtonSeat: 1,
            HoleCardsSeat0: TestCards.Parse(holeCardsSeat0),
            HoleCardsSeat1: TestCards.Parse("2h 3h"),
            Board: TestCards.Parse(board),
            Actions: actionRecords,
            Showdown: showdown,
            Winners: winners,
            PotAwarded: 0,
            FinalStacks: finalStacks);
    }
}
