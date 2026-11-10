using System.Globalization;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Runtime behaviour of the canonical harness-memory store and of its write-only integration in the
/// training loop. Every assertion round-trips real bytes: exact canonical file text, an independently
/// recomputed SHA-256 over the entries array, deterministic eviction and tamper refusal that leaves
/// the file untouched.
/// </summary>
public sealed class HarnessMemoryTests
{
    /// <summary>The seed and shape of the pre-round-4 probe whose challenger choices are pinned below.</summary>
    private const ulong RefinementSeed = 20_261_111UL;

    private const int Iterations = 3;

    private const int TrainingHands = 24;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Memory_GivenRejectedChallenger_ExpectedFailedApproachEntry()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-rejected");
        try
        {
            var store = new HarnessMemoryStore(dataDirectory);
            store.RecordRejection("missed-value", iteration: 4);

            var reloaded = new HarnessMemoryStore(dataDirectory).Load();
            var entry = Assert.Single(reloaded.Entries);
            Assert.Equal(HarnessMemoryKinds.FailedApproach, entry.Kind);
            Assert.Equal("missed-value", entry.Key);
            Assert.Equal(1, entry.Count);
            Assert.Equal(4, entry.FirstIteration);
            Assert.Equal(4, entry.LastIteration);

            var memoryPath = store.MemoryPath;
            Assert.True(File.Exists(memoryPath), $"The rejected challenger must be persisted at {memoryPath}.");
            var bytes = File.ReadAllBytes(memoryPath);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "memory.json must be UTF-8 without a BOM.");

            Assert.Equal(
                """{"schema":"tholdem.harness-memory.v1","contentHash":"37652bc8369bd190dce3b0f19db2c31746089bb5cfe51c03fcc6fca05df5749e","entries":[{"kind":"failed-approach","key":"missed-value","count":1,"firstIteration":4,"lastIteration":4}]}""",
                Utf8.GetString(bytes));

            using (var document = JsonDocument.Parse(bytes))
            {
                var root = document.RootElement;
                Assert.Equal(
                    new[] { "schema", "contentHash", "entries" },
                    root.EnumerateObject().Select(static property => property.Name).ToArray());
                Assert.Equal("tholdem.harness-memory.v1", root.GetProperty("schema").GetString());
                var entries = root.GetProperty("entries");
                var independentHash = LearningTestSupport.Sha256Hex(Utf8.GetBytes(entries.GetRawText()));
                Assert.Equal(independentHash, root.GetProperty("contentHash").GetString());
                Assert.Equal(independentHash, reloaded.ContentHash);
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Memory_GivenThirdRejectionOfSameKey_ExpectedDeadEnd()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-dead-end");
        try
        {
            var store = new HarnessMemoryStore(dataDirectory);

            var first = store.RecordRejection("fold-to-raise", iteration: 1);
            var firstEntry = Assert.Single(first.Entries);
            Assert.Equal(HarnessMemoryKinds.FailedApproach, firstEntry.Kind);
            Assert.Equal(1, firstEntry.Count);

            var second = store.RecordRejection("fold-to-raise", iteration: 3);
            var secondEntry = Assert.Single(second.Entries);
            Assert.Equal(HarnessMemoryKinds.FailedApproach, secondEntry.Kind);
            Assert.Equal(2, secondEntry.Count);
            Assert.Equal(1, secondEntry.FirstIteration);
            Assert.Equal(3, secondEntry.LastIteration);

            var third = store.RecordRejection("fold-to-raise", iteration: 5);
            var thirdEntry = Assert.Single(third.Entries);
            Assert.Equal(HarnessMemoryKinds.DeadEnd, thirdEntry.Kind);
            Assert.Equal(3, thirdEntry.Count);
            Assert.Equal(1, thirdEntry.FirstIteration);
            Assert.Equal(5, thirdEntry.LastIteration);

            var reloaded = new HarnessMemoryStore(dataDirectory).Load();
            Assert.Equal(third.ToCanonicalJson(), reloaded.ToCanonicalJson());
            Assert.Equal(HarnessMemoryKinds.DeadEnd, Assert.Single(reloaded.Entries).Kind);

            // The dead-end threshold is per key: a different approach starts as a failed approach.
            var fourth = store.RecordRejection("missed-value", iteration: 6);
            Assert.Equal(2, fourth.Entries.Count);
            Assert.Equal(
                HarnessMemoryKinds.FailedApproach,
                fourth.Entries.Single(static entry => entry.Key == "missed-value").Kind);
            Assert.Equal(
                HarnessMemoryKinds.DeadEnd,
                fourth.Entries.Single(static entry => entry.Key == "fold-to-raise").Kind);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Memory_GivenPatternRecording_ExpectedPatternEntryAndDeadEndIsAbsorbing()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-pattern");
        try
        {
            var store = new HarnessMemoryStore(dataDirectory);

            var patterned = store.RecordPattern("missed-value", iteration: 1);
            var entry = Assert.Single(patterned.Entries);
            Assert.Equal(HarnessMemoryKinds.Pattern, entry.Kind);
            Assert.Equal(1, entry.Count);

            var repeated = store.RecordPattern("missed-value", iteration: 2);
            Assert.Equal(HarnessMemoryKinds.Pattern, Assert.Single(repeated.Entries).Kind);
            Assert.Equal(2, Assert.Single(repeated.Entries).Count);

            var rejected = store.RecordRejection("missed-value", iteration: 3);
            Assert.Equal(HarnessMemoryKinds.DeadEnd, Assert.Single(rejected.Entries).Kind);
            Assert.Equal(3, Assert.Single(rejected.Entries).Count);

            // A later accepted approach never demotes an established dead end.
            var afterDeadEnd = store.RecordPattern("missed-value", iteration: 4);
            var finalEntry = Assert.Single(afterDeadEnd.Entries);
            Assert.Equal(HarnessMemoryKinds.DeadEnd, finalEntry.Kind);
            Assert.Equal(4, finalEntry.Count);
            Assert.Equal(1, finalEntry.FirstIteration);
            Assert.Equal(4, finalEntry.LastIteration);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Memory_GivenEntriesAboveCap_ExpectedDeterministicEvictionAndStableHash()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-cap-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-cap-b");
        try
        {
            Assert.Equal(64, HarnessMemoryStore.MaximumEntries);

            var first = RecordCapSequence(firstDirectory);
            var second = RecordCapSequence(secondDirectory);

            Assert.Equal(HarnessMemoryStore.MaximumEntries, first.Entries.Count);
            Assert.Equal(HarnessMemoryStore.MaximumEntries, second.Entries.Count);

            // "key-00" and "key-01" share the lowest last iteration; the ordinal key tie-break evicts
            // "key-00", and the just-added "key-64" survives with the highest last iteration.
            Assert.DoesNotContain(first.Entries, static entry => entry.Key == "key-00");
            Assert.Contains(first.Entries, static entry => entry.Key == "key-01");
            var added = first.Entries.Single(static entry => entry.Key == "key-64");
            Assert.Equal(1, added.Count);
            Assert.Equal(100, added.FirstIteration);
            Assert.Equal(100, added.LastIteration);
            Assert.Equal(
                Enumerable.Range(1, 63).Select(static index => "key-" + index.ToString("D2", CultureInfo.InvariantCulture))
                    .Append("key-64")
                    .OrderBy(static key => key, StringComparer.Ordinal)
                    .ToArray(),
                first.Entries.Select(static entry => entry.Key).ToArray());

            var firstBytes = File.ReadAllBytes(new HarnessMemoryStore(firstDirectory).MemoryPath);
            var secondBytes = File.ReadAllBytes(new HarnessMemoryStore(secondDirectory).MemoryPath);
            Assert.Equal(firstBytes, secondBytes);
            Assert.Equal(first.ContentHash, second.ContentHash);
            Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());

            using (var document = JsonDocument.Parse(firstBytes))
            {
                var entries = document.RootElement.GetProperty("entries");
                Assert.Equal(
                    document.RootElement.GetProperty("contentHash").GetString(),
                    LearningTestSupport.Sha256Hex(Utf8.GetBytes(entries.GetRawText())));
            }

            // Reloading is a pure read: the persisted bytes are unchanged afterwards.
            _ = new HarnessMemoryStore(firstDirectory).Load();
            Assert.Equal(firstBytes, File.ReadAllBytes(new HarnessMemoryStore(firstDirectory).MemoryPath));
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public void Memory_GivenTamperedFile_ExpectedRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-tamper");
        try
        {
            var store = new HarnessMemoryStore(dataDirectory);
            store.RecordRejection("missed-value", iteration: 1);

            var memoryPath = store.MemoryPath;
            var canonical = File.ReadAllText(memoryPath);
            var tampered = canonical.Replace("\"count\":1", "\"count\":2", StringComparison.Ordinal);
            Assert.NotEqual(canonical, tampered);
            File.WriteAllText(memoryPath, tampered, Utf8);
            var tamperedBytes = File.ReadAllBytes(memoryPath);

            var loadRefusal = Assert.Throws<HarnessMemoryException>(
                () => new HarnessMemoryStore(dataDirectory).Load());
            Assert.Equal(HarnessMemoryStore.ContentHashMismatchCode, loadRefusal.Code);
            Assert.Equal(tamperedBytes, File.ReadAllBytes(memoryPath));

            var recordRefusal = Assert.Throws<HarnessMemoryException>(
                () => store.RecordRejection("other-approach", iteration: 2));
            Assert.Equal(HarnessMemoryStore.ContentHashMismatchCode, recordRefusal.Code);
            Assert.Equal(tamperedBytes, File.ReadAllBytes(memoryPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Memory_GivenSameInputsTwoDirectories_ExpectedIdenticalBytes()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-determinism-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-determinism-b");
        var differentDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-determinism-c");
        try
        {
            var first = RecordDeterministicSequence(firstDirectory);
            var second = RecordDeterministicSequence(secondDirectory);

            Assert.Equal(
                File.ReadAllBytes(new HarnessMemoryStore(firstDirectory).MemoryPath),
                File.ReadAllBytes(new HarnessMemoryStore(secondDirectory).MemoryPath));
            Assert.Equal(first.ContentHash, second.ContentHash);
            Assert.Equal(
                new[] { "aggression-by-street-low", "missed-value" },
                first.Entries.Select(static entry => entry.Key).ToArray());
            var deadEnd = first.Entries.Single(static entry => entry.Key == "missed-value");
            Assert.Equal(HarnessMemoryKinds.DeadEnd, deadEnd.Kind);
            Assert.Equal(3, deadEnd.Count);
            Assert.Equal(1, deadEnd.FirstIteration);
            Assert.Equal(4, deadEnd.LastIteration);
            Assert.Equal(
                HarnessMemoryKinds.Pattern,
                first.Entries.Single(static entry => entry.Key == "aggression-by-street-low").Kind);

            var different = new HarnessMemoryStore(differentDirectory);
            different.RecordRejection("fold-to-raise", iteration: 1);
            different.RecordRejection("fold-to-raise", iteration: 2);
            different.RecordRejection("fold-to-raise", iteration: 3);
            Assert.NotEqual(
                File.ReadAllBytes(new HarnessMemoryStore(firstDirectory).MemoryPath),
                File.ReadAllBytes(new HarnessMemoryStore(differentDirectory).MemoryPath));
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
            Directory.Delete(differentDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenRefinementMode_ExpectedMemoryWrittenAndPolicyUnchanged()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-run-a");
        var seededDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-run-b");
        try
        {
            var options = new TrainingOptions(
                Seed: RefinementSeed,
                Iterations: Iterations,
                TrainingHands: TrainingHands,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2,
                HarnessRefinement: true);

            // A dead end recorded before the run must not influence a single proposal: the policy
            // never receives the memory store.
            var seeded = new HarnessMemoryStore(seededDirectory);
            seeded.RecordRejection("aggression-by-street-low", iteration: 1);
            seeded.RecordRejection("aggression-by-street-low", iteration: 2);
            var preSeededDeadEnd = seeded.RecordRejection("aggression-by-street-low", iteration: 3);
            Assert.Equal(HarnessMemoryKinds.DeadEnd, Assert.Single(preSeededDeadEnd.Entries).Kind);

            var first = TrainingLoop.Run(options with { DataDirectory = firstDirectory });
            var second = TrainingLoop.Run(options with { DataDirectory = seededDirectory });

            Assert.Equal(Iterations, first.Iterations.Count);
            Assert.Equal(Iterations, second.Iterations.Count);

            // Exactly the challenger choices recorded from the round-3 loop shape before this round.
            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v1-f100e793",
                    "tholdem-strategy-v1-f100e793",
                    "tholdem-strategy-v1-f100e793",
                },
                first.Iterations.Select(static iteration => iteration.ChampionId).ToArray());
            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v2-d24dcf6d",
                    "tholdem-strategy-v2-c34333fe",
                    "tholdem-strategy-v2-86f42856",
                },
                first.Iterations.Select(static iteration => iteration.ChallengerId).ToArray());
            Assert.Equal(
                first.Iterations.Select(static iteration => iteration.ChampionId + ">" + iteration.ChallengerId).ToArray(),
                second.Iterations.Select(static iteration => iteration.ChampionId + ">" + iteration.ChallengerId).ToArray());
            Assert.All(first.Iterations, static iteration => Assert.Equal("rejected", iteration.Outcome));

            // Replaying the loop's own context with the policy alone must reproduce every challenger:
            // the context carries no memory and no lineage, so the measured arm is unchanged.
            var recordedRationales = new[] { "aggression-by-street-low", "aggression-by-street-low", "fold-to-raise" };
            for (var index = 0; index < first.Iterations.Count; index++)
            {
                var iteration = first.Iterations[index];
                var champion = TrainingLoop.ReadPersistedStrategy(firstDirectory, iteration.ChampionId);
                var challenger = TrainingLoop.ReadPersistedStrategy(firstDirectory, iteration.ChallengerId);
                var trainingSeed = SeedDerivation.Derive(
                    RefinementSeed,
                    TrainingLoop.TrainingSeedHandBase + iteration.Iteration,
                    0,
                    0,
                    TrainingLoop.TrainingSeedStream);
                var capture = TrainingTrajectoryCapture.Capture(
                    firstDirectory,
                    iteration.Iteration,
                    new SimulationOptions(trainingSeed, TrainingHands),
                    new StrategyAgent(champion.Genome, "champion"),
                    new TightAggressiveAgent(name: "baseline"));
                var analysis = StrategyAnalyzer.Analyze(capture.Report);
                var signatures = FailureSignatureExtractor.Extract(capture.Hands);
                var proposal = new SignatureDrivenRefinementPolicy().Propose(
                    new RefinementContext(champion.Genome, analysis, signatures, iteration.Iteration));

                Assert.Equal(challenger.Genome, proposal.Genome);
                Assert.Equal(recordedRationales[index], proposal.RationaleCode);
            }

            // The memory file is byte-for-byte the pinned product of the recorded rationales:
            // iteration 1 and 2 note the same failed approach twice, iteration 3 notes fold-to-raise.
            var memoryPath = new HarnessMemoryStore(firstDirectory).MemoryPath;
            Assert.True(File.Exists(memoryPath), $"Refinement mode must write {memoryPath}.");
            Assert.Equal(
                """{"schema":"tholdem.harness-memory.v1","contentHash":"f7f6174ab2e8de5a2dc153c86cc1ac3d37ec0cf315de63181782252f4c9c127a","entries":[{"kind":"failed-approach","key":"aggression-by-street-low","count":2,"firstIteration":1,"lastIteration":2},{"kind":"failed-approach","key":"fold-to-raise","count":1,"firstIteration":3,"lastIteration":3}]}""",
                File.ReadAllText(memoryPath));

            var memory = new HarnessMemoryStore(firstDirectory).Load();
            Assert.Equal(2, memory.Entries.Count);
            Assert.Equal(
                new[] { "aggression-by-street-low", "fold-to-raise" },
                memory.Entries.Select(static entry => entry.Key).ToArray());
            Assert.Equal(2, memory.Entries[0].Count);
            Assert.Equal(1, memory.Entries[1].Count);

            // The pre-seeded dead end persisted and was incremented by the run, and the loop still
            // chose exactly the same challengers.
            var seededMemory = new HarnessMemoryStore(seededDirectory).Load();
            var seededEntry = seededMemory.Entries.Single(
                static entry => entry.Key == "aggression-by-street-low");
            Assert.Equal(HarnessMemoryKinds.DeadEnd, seededEntry.Kind);
            Assert.Equal(5, seededEntry.Count);
            Assert.Contains(seededMemory.Entries, static entry => entry.Key == "fold-to-raise");
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(seededDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenPromotedRefinementMode_ExpectedPatternEntries()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-promoted");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: RefinementSeed,
                    Iterations: Iterations,
                    TrainingHands: TrainingHands,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    Criteria: new EvaluationCriteria(MarginBbPer100: -10_000.0, MinSeeds: 2),
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true));

            Assert.All(run.Iterations, static iteration => Assert.Equal("promoted", iteration.Outcome));
            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v2-d24dcf6d",
                    "tholdem-strategy-v3-56423da7",
                    "tholdem-strategy-v4-eaa7bf28",
                },
                run.Iterations.Select(static iteration => iteration.ChallengerId).ToArray());
            Assert.Equal("tholdem-strategy-v4-eaa7bf28", run.FinalChampionId);

            var memoryPath = new HarnessMemoryStore(dataDirectory).MemoryPath;
            Assert.Equal(
                """{"schema":"tholdem.harness-memory.v1","contentHash":"c27aeb2ec0c22c78dc8cbc27f4bcaf1ad65e79260996f485fbdea13ebbddb316","entries":[{"kind":"pattern","key":"aggression-by-street-low","count":2,"firstIteration":1,"lastIteration":2},{"kind":"pattern","key":"fold-to-raise","count":1,"firstIteration":3,"lastIteration":3}]}""",
                File.ReadAllText(memoryPath));

            var memory = new HarnessMemoryStore(dataDirectory).Load();
            Assert.Equal(2, memory.Entries.Count);
            Assert.All(memory.Entries, static entry => Assert.Equal(HarnessMemoryKinds.Pattern, entry.Kind));
            Assert.Equal(
                2,
                memory.Entries.Single(static entry => entry.Key == "aggression-by-street-low").Count);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenInertProposal_ExpectedNoMemoryEntry()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-inert");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: RefinementSeed,
                    Iterations: 1,
                    TrainingHands: TrainingHands,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true),
                proposalGuard: new AlwaysRefusingGuard());

            var iteration = Assert.Single(run.Iterations);
            Assert.Equal(TrainingLoop.InertProposalOutcome, iteration.Outcome);
            Assert.Null(iteration.Report);
            Assert.False(File.Exists(new HarnessMemoryStore(dataDirectory).MemoryPath));
            Assert.Empty(Directory.GetFiles(Path.Combine(dataDirectory, "evaluations"), "*.json"));
            Assert.True(File.Exists(Path.Combine(dataDirectory, "strategies", iteration.ChallengerId + ".json")));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenLegacyMode_ExpectedNoMemoryFile()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-legacy-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-memory-legacy-b");
        try
        {
            var options = new TrainingOptions(
                Seed: RefinementSeed,
                Iterations: 1,
                TrainingHands: 12,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2);

            var first = TrainingLoop.Run(options with { DataDirectory = firstDirectory });
            var second = TrainingLoop.Run(options with { DataDirectory = secondDirectory });

            Assert.Single(first.Iterations);
            Assert.Equal(
                new HarnessMemoryStore(firstDirectory).MemoryPath,
                Path.Combine(firstDirectory, "harness", "memory.json"));
            Assert.False(File.Exists(new HarnessMemoryStore(firstDirectory).MemoryPath));
            Assert.False(File.Exists(new HarnessMemoryStore(secondDirectory).MemoryPath));
            Assert.False(Directory.Exists(Path.Combine(firstDirectory, "harness")));
            Assert.False(Directory.Exists(Path.Combine(secondDirectory, "harness")));

            var firstFiles = JsonFileNames(firstDirectory);
            Assert.Equal(5, firstFiles.Length);
            Assert.Equal(firstFiles, JsonFileNames(secondDirectory));
            foreach (var name in firstFiles)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(firstDirectory, name)),
                    File.ReadAllBytes(Path.Combine(secondDirectory, name)));
            }
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    private static HarnessMemorySnapshot RecordCapSequence(string dataDirectory)
    {
        var store = new HarnessMemoryStore(dataDirectory);
        var snapshot = HarnessMemorySnapshot.Empty;
        for (var index = 0; index < HarnessMemoryStore.MaximumEntries; index++)
        {
            var key = "key-" + index.ToString("D2", CultureInfo.InvariantCulture);
            var iteration = index <= 1 ? 1 : index;
            snapshot = store.RecordRejection(key, iteration);
        }

        Assert.Equal(HarnessMemoryStore.MaximumEntries, snapshot.Entries.Count);
        return store.RecordRejection("key-64", iteration: 100);
    }

    private static HarnessMemorySnapshot RecordDeterministicSequence(string dataDirectory)
    {
        var store = new HarnessMemoryStore(dataDirectory);
        store.RecordRejection("missed-value", iteration: 1);
        store.RecordRejection("missed-value", iteration: 2);
        store.RecordPattern("aggression-by-street-low", iteration: 3);
        return store.RecordRejection("missed-value", iteration: 4);
    }

    private static string[] JsonFileNames(string root) =>
        Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private sealed class AlwaysRefusingGuard : IProposalGuard
    {
        public bool IsAcceptable(StrategyGenome incumbent, StrategyGenome candidate, out string reason)
        {
            reason = TrainingLoop.InertProposalOutcome;
            return false;
        }
    }
}
