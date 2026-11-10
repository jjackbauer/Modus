using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Runtime behaviour of the opt-in bootstrap modes (R-06). Every scenario starts from a real prior
/// data directory produced by a real refinement run (champion pointer, promoted lineage, verified
/// memory file) and asserts persisted bytes: the copied champion artifact and pointer bytes, the
/// frozen memory bytes, the updating memory hash and the deterministic artifact set of two targets.
/// </summary>
public sealed class HarnessBootstrapTests
{
    /// <summary>The seed and shape of the round-4 probe whose promoted chain is the bootstrap source.</summary>
    private const ulong SourceSeed = 20_261_111UL;

    private const int SourceIterations = 2;

    private const int TrainingHands = 24;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Bootstrap_GivenFrozen_ExpectedMemoryBytesUnchangedAndHashesRecorded()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-frozen-source");
        var targetDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-frozen-target");
        try
        {
            _ = CreateSource(sourceDirectory);
            var sourceStore = new HarnessMemoryStore(sourceDirectory);
            var sourceMemoryBytes = File.ReadAllBytes(sourceStore.MemoryPath);
            var sourceMemory = sourceStore.Load();
            Assert.True(sourceMemory.Entries.Count > 0, "The bootstrap source must carry a non-empty memory file.");
            Assert.Equal(SourceIterations, sourceMemory.Entries.Sum(static entry => entry.Count));

            var run = TrainingLoop.Run(
                BootstrapOptions(targetDirectory, sourceDirectory, HarnessBootstrapMode.Frozen, iterations: 2));
            var summary = run.Bootstrap!;
            Assert.Equal(HarnessBootstrapMode.Frozen, summary.Mode);
            Assert.Equal(Path.GetFullPath(sourceDirectory), summary.SourceDirectory);
            Assert.Equal(ReadPointerStrategyId(sourceDirectory), summary.ChampionId);
            Assert.Equal(3, summary.LineageCount);
            Assert.Equal(sourceMemory.ContentHash, summary.MemoryHashBefore);
            Assert.Equal(sourceMemory.ContentHash, summary.MemoryHashAfter);
            Assert.False(summary.MemoryWritable);

            // Frozen means read-only: the target's memory is the byte-verbatim source copy and the
            // source itself is untouched, even though both iterations completed a full evaluation.
            var targetMemoryPath = new HarnessMemoryStore(targetDirectory).MemoryPath;
            Assert.Equal(sourceMemoryBytes, File.ReadAllBytes(targetMemoryPath));
            Assert.Equal(sourceMemoryBytes, File.ReadAllBytes(sourceStore.MemoryPath));
            Assert.Equal(2, run.Iterations.Count);
            Assert.All(run.Iterations, static iteration => Assert.NotNull(iteration.Report));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenUpdating_ExpectedMemoryUpdatedAndHashesDiffer()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-updating-source");
        var targetDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-updating-target");
        try
        {
            _ = CreateSource(sourceDirectory);
            var sourceStore = new HarnessMemoryStore(sourceDirectory);
            var sourceMemory = sourceStore.Load();
            var sourceMemoryBytes = File.ReadAllBytes(sourceStore.MemoryPath);
            var sourceCount = sourceMemory.Entries.Sum(static entry => entry.Count);

            var run = TrainingLoop.Run(
                BootstrapOptions(targetDirectory, sourceDirectory, HarnessBootstrapMode.Updating, iterations: 2));
            var summary = run.Bootstrap!;
            Assert.Equal(HarnessBootstrapMode.Updating, summary.Mode);
            Assert.True(summary.MemoryWritable);
            Assert.Equal(sourceMemory.ContentHash, summary.MemoryHashBefore);
            Assert.NotEqual(summary.MemoryHashBefore, summary.MemoryHashAfter);

            // Each completed evaluation iteration appends exactly one memory record on top of the
            // loaded snapshot, and the recorded after-hash is the reloaded file's content hash.
            var updated = new HarnessMemoryStore(targetDirectory).Load();
            Assert.Equal(sourceCount + 2, updated.Entries.Sum(static entry => entry.Count));
            Assert.Equal(updated.ContentHash, summary.MemoryHashAfter);
            Assert.Equal(sourceMemoryBytes, File.ReadAllBytes(sourceStore.MemoryPath));
            Assert.NotEqual(sourceMemoryBytes, File.ReadAllBytes(new HarnessMemoryStore(targetDirectory).MemoryPath));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenMissingSource_ExpectedStableRefusal()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-missing-source");
        var targetDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-missing-target");
        try
        {
            var missingSource = Path.Combine(targetDirectory, "no-such-source");
            Assert.False(Directory.Exists(missingSource));

            var refusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(targetDirectory, missingSource, HarnessBootstrapMode.Frozen, iterations: 1)));
            Assert.Equal(HarnessBootstrap.SourceMissingCode, refusal.Code);
            Assert.Contains(missingSource, refusal.Message, StringComparison.Ordinal);

            // The refusal happens before the target is prepared: no pointer, ledger or memory appears.
            Assert.False(File.Exists(Path.Combine(targetDirectory, TrainingLoop.ChampionFileName)));
            Assert.False(File.Exists(Path.Combine(targetDirectory, TrainingLoop.LedgerFileName)));
            Assert.False(Directory.Exists(Path.Combine(targetDirectory, HarnessMemoryStore.DirectoryName)));
            Assert.False(Directory.Exists(Path.Combine(targetDirectory, "strategies")));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenMissingSourceChampion_ExpectedStableRefusal()
    {
        var pointerLessSource = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-no-pointer");
        var artifactLessSource = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-no-artifact");
        var firstTarget = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-no-pointer-target");
        var secondTarget = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-no-artifact-target");
        try
        {
            // A source with verified (empty) memory but no champion pointer.
            var pointerRefusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(firstTarget, pointerLessSource, HarnessBootstrapMode.Frozen, iterations: 1)));
            Assert.Equal(HarnessBootstrap.ChampionMissingCode, pointerRefusal.Code);
            Assert.Contains(TrainingLoop.ChampionFileName, pointerRefusal.Message, StringComparison.Ordinal);

            // A source pointer whose content-addressed artifact does not exist.
            const string MissingChampionId = "tholdem-strategy-v9-12345678";
            File.WriteAllText(
                Path.Combine(artifactLessSource, TrainingLoop.ChampionFileName),
                "{\"strategyId\":\"" + MissingChampionId + "\",\"iteration\":0}",
                Utf8);
            var artifactRefusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(secondTarget, artifactLessSource, HarnessBootstrapMode.Frozen, iterations: 1)));
            Assert.Equal(HarnessBootstrap.ChampionMissingCode, artifactRefusal.Code);
            Assert.Contains(MissingChampionId, artifactRefusal.Message, StringComparison.Ordinal);

            Assert.False(File.Exists(Path.Combine(firstTarget, TrainingLoop.ChampionFileName)));
            Assert.False(File.Exists(Path.Combine(secondTarget, TrainingLoop.ChampionFileName)));
            Assert.False(Directory.Exists(Path.Combine(firstTarget, HarnessMemoryStore.DirectoryName)));
            Assert.False(Directory.Exists(Path.Combine(secondTarget, HarnessMemoryStore.DirectoryName)));
        }
        finally
        {
            Directory.Delete(pointerLessSource, recursive: true);
            Directory.Delete(artifactLessSource, recursive: true);
            Directory.Delete(firstTarget, recursive: true);
            Directory.Delete(secondTarget, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenUnreadableSourceLineage_ExpectedStableRefusal()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-lineage-source");
        var targetDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-lineage-target");
        try
        {
            _ = CreateSource(sourceDirectory);
            var sourceChampionId = ReadPointerStrategyId(sourceDirectory);
            var lineage = StrategyLineageReader.Read(sourceDirectory, sourceChampionId);
            Assert.Equal(3, lineage.Count);
            var rootPath = Path.Combine(sourceDirectory, "strategies", lineage[0].Id + ".json");
            Assert.True(File.Exists(rootPath), $"The bootstrap source must contain its root artifact: {rootPath}");
            File.Delete(rootPath);

            var refusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(targetDirectory, sourceDirectory, HarnessBootstrapMode.Updating, iterations: 1)));
            Assert.Equal(HarnessBootstrap.LineageInvalidCode, refusal.Code);
            var lineageFailure = Assert.IsType<StrategyLineageException>(refusal.InnerException);
            Assert.Equal(StrategyLineageReader.MissingParentCode, lineageFailure.Code);

            Assert.False(File.Exists(Path.Combine(targetDirectory, TrainingLoop.ChampionFileName)));
            Assert.False(File.Exists(Path.Combine(targetDirectory, TrainingLoop.LedgerFileName)));
            Assert.False(Directory.Exists(Path.Combine(targetDirectory, HarnessMemoryStore.DirectoryName)));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenTamperedSourceMemory_ExpectedStableRefusal()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-tamper-source");
        var targetDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-tamper-target");
        try
        {
            _ = CreateSource(sourceDirectory);
            var memoryPath = new HarnessMemoryStore(sourceDirectory).MemoryPath;
            var canonical = File.ReadAllText(memoryPath);
            var tampered = canonical.Replace(
                "\"entries\":[",
                "\"entries\":[{\"kind\":\"pattern\",\"key\":\"injected\",\"count\":1,\"firstIteration\":0,\"lastIteration\":0},",
                StringComparison.Ordinal);
            Assert.NotEqual(canonical, tampered);
            File.WriteAllText(memoryPath, tampered, Utf8);
            var tamperedBytes = File.ReadAllBytes(memoryPath);

            var refusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(targetDirectory, sourceDirectory, HarnessBootstrapMode.Frozen, iterations: 1)));
            Assert.Equal(HarnessBootstrap.MemoryInvalidCode, refusal.Code);
            var memoryFailure = Assert.IsType<HarnessMemoryException>(refusal.InnerException);
            Assert.Equal(HarnessMemoryStore.ContentHashMismatchCode, memoryFailure.Code);

            // A refused bootstrap does not rewrite the tampered source and does not touch the target.
            Assert.Equal(tamperedBytes, File.ReadAllBytes(memoryPath));
            Assert.False(File.Exists(Path.Combine(targetDirectory, TrainingLoop.ChampionFileName)));
            Assert.False(File.Exists(Path.Combine(targetDirectory, TrainingLoop.LedgerFileName)));
            Assert.False(Directory.Exists(Path.Combine(targetDirectory, HarnessMemoryStore.DirectoryName)));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenNonFreshTarget_ExpectedStableRefusal()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-stale-source");
        var pointerTarget = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-stale-pointer");
        var ledgerTarget = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-stale-ledger");
        try
        {
            _ = CreateSource(sourceDirectory);

            var sourcePointerBytes = File.ReadAllBytes(
                Path.Combine(sourceDirectory, TrainingLoop.ChampionFileName));
            var stalePointerPath = Path.Combine(pointerTarget, TrainingLoop.ChampionFileName);
            File.WriteAllBytes(stalePointerPath, sourcePointerBytes);
            var pointerRefusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(pointerTarget, sourceDirectory, HarnessBootstrapMode.Frozen, iterations: 1)));
            Assert.Equal(HarnessBootstrap.TargetNotFreshCode, pointerRefusal.Code);
            Assert.Equal(sourcePointerBytes, File.ReadAllBytes(stalePointerPath));
            Assert.False(File.Exists(Path.Combine(pointerTarget, TrainingLoop.LedgerFileName)));
            Assert.False(Directory.Exists(Path.Combine(pointerTarget, HarnessMemoryStore.DirectoryName)));

            var staleLedgerBytes = Utf8.GetBytes("[]");
            var staleLedgerPath = Path.Combine(ledgerTarget, TrainingLoop.LedgerFileName);
            File.WriteAllBytes(staleLedgerPath, staleLedgerBytes);
            var ledgerRefusal = Assert.Throws<HarnessBootstrapException>(
                () => TrainingLoop.Run(BootstrapOptions(ledgerTarget, sourceDirectory, HarnessBootstrapMode.Updating, iterations: 1)));
            Assert.Equal(HarnessBootstrap.TargetNotFreshCode, ledgerRefusal.Code);
            Assert.Equal(staleLedgerBytes, File.ReadAllBytes(staleLedgerPath));
            Assert.False(File.Exists(Path.Combine(ledgerTarget, TrainingLoop.ChampionFileName)));
            Assert.False(Directory.Exists(Path.Combine(ledgerTarget, HarnessMemoryStore.DirectoryName)));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(pointerTarget, recursive: true);
            Directory.Delete(ledgerTarget, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenSameInputsTwoTargets_ExpectedIdenticalArtifacts()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-determinism-source");
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-determinism-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-determinism-b");
        try
        {
            _ = CreateSource(sourceDirectory);
            var sourcePointerBytes = File.ReadAllBytes(
                Path.Combine(sourceDirectory, TrainingLoop.ChampionFileName));

            var first = TrainingLoop.Run(
                BootstrapOptions(firstDirectory, sourceDirectory, HarnessBootstrapMode.Updating, iterations: 2));
            var second = TrainingLoop.Run(
                BootstrapOptions(secondDirectory, sourceDirectory, HarnessBootstrapMode.Updating, iterations: 2));

            var firstFiles = JsonFileHashes(firstDirectory);
            var secondFiles = JsonFileHashes(secondDirectory);
            Assert.Contains("harness/memory.json", firstFiles.Keys);
            Assert.Contains(TrainingLoop.ChampionFileName, firstFiles.Keys);
            Assert.Equal(firstFiles.Keys.ToArray(), secondFiles.Keys.ToArray());
            foreach (var pair in firstFiles)
            {
                Assert.Equal(pair.Value, secondFiles[pair.Key]);
            }

            Assert.Equal(sourcePointerBytes, File.ReadAllBytes(Path.Combine(firstDirectory, TrainingLoop.ChampionFileName)));
            Assert.Equal(sourcePointerBytes, File.ReadAllBytes(Path.Combine(secondDirectory, TrainingLoop.ChampionFileName)));
            Assert.Equal(first.Bootstrap!.MemoryHashBefore, second.Bootstrap!.MemoryHashBefore);
            Assert.Equal(first.Bootstrap.MemoryHashAfter, second.Bootstrap.MemoryHashAfter);
            Assert.Equal(
                new HarnessMemoryStore(firstDirectory).Load().ContentHash,
                first.Bootstrap.MemoryHashAfter);
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public void Bootstrap_GivenSourceChampion_ExpectedTargetStartsFromSameGenomeAndLineageCount()
    {
        var sourceDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-champion-source");
        var targetDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-champion-target");
        try
        {
            var sourceRun = CreateSource(sourceDirectory);
            var sourceChampionId = ReadPointerStrategyId(sourceDirectory);
            Assert.Equal(sourceRun.FinalChampionId, sourceChampionId);
            var sourceChampion = TrainingLoop.ReadPersistedStrategy(sourceDirectory, sourceChampionId);
            var sourceLineage = StrategyLineageReader.Read(sourceDirectory, sourceChampionId);
            Assert.Equal(3, sourceLineage.Count);

            var run = TrainingLoop.Run(
                BootstrapOptions(targetDirectory, sourceDirectory, HarnessBootstrapMode.Frozen, iterations: 1));
            var iteration = Assert.Single(run.Iterations);
            Assert.Equal(sourceChampionId, iteration.ChampionId);

            var targetChampion = TrainingLoop.ReadPersistedStrategy(targetDirectory, sourceChampionId);
            Assert.Equal(sourceChampion.Genome, targetChampion.Genome);
            Assert.Equal(sourceChampion.Hash, targetChampion.Hash);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(sourceDirectory, "strategies", sourceChampionId + ".json")),
                File.ReadAllBytes(Path.Combine(targetDirectory, "strategies", sourceChampionId + ".json")));

            var summary = run.Bootstrap!;
            Assert.Equal(sourceChampionId, summary.ChampionId);
            Assert.Equal(3, summary.LineageCount);
            Assert.Equal(sourceLineage.Count, summary.LineageCount);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(sourceDirectory, TrainingLoop.ChampionFileName)),
                File.ReadAllBytes(Path.Combine(targetDirectory, TrainingLoop.ChampionFileName)));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenRefinementModeWithoutBootstrap_ExpectedPinnedChallengersAndNullBootstrapSummary()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-bootstrap-none-pinned");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: SourceSeed,
                    Iterations: 3,
                    TrainingHands: TrainingHands,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true));

            Assert.Null(run.Bootstrap);
            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v1-f100e793",
                    "tholdem-strategy-v1-f100e793",
                    "tholdem-strategy-v1-f100e793",
                },
                run.Iterations.Select(static iteration => iteration.ChampionId).ToArray());
            Assert.Equal(
                new[]
                {
                    "tholdem-strategy-v2-d24dcf6d",
                    "tholdem-strategy-v2-c34333fe",
                    "tholdem-strategy-v2-86f42856",
                },
                run.Iterations.Select(static iteration => iteration.ChallengerId).ToArray());
            Assert.All(run.Iterations, static iteration => Assert.Equal(TrainingLoop.RejectedOutcome, iteration.Outcome));
            Assert.True(File.Exists(new HarnessMemoryStore(dataDirectory).MemoryPath));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static TrainingRun CreateSource(string sourceDirectory)
    {
        var run = TrainingLoop.Run(
            new TrainingOptions(
                Seed: SourceSeed,
                Iterations: SourceIterations,
                TrainingHands: TrainingHands,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2,
                Criteria: new EvaluationCriteria(MarginBbPer100: -10_000.0, MinSeeds: 2),
                DataDirectory: sourceDirectory,
                HarnessRefinement: true));
        Assert.Equal(SourceIterations, run.Iterations.Count);
        Assert.All(run.Iterations, static iteration => Assert.Equal(TrainingLoop.PromotedOutcome, iteration.Outcome));
        return run;
    }

    private static TrainingOptions BootstrapOptions(
        string targetDirectory,
        string sourceDirectory,
        HarnessBootstrapMode mode,
        int iterations) =>
        new(
            Seed: SourceSeed,
            Iterations: iterations,
            TrainingHands: TrainingHands,
            EvaluationSeeds: 2,
            EvaluationHandsPerMatch: 2,
            // The target runs under a gate that cannot promote, so the pointer copied by the
            // bootstrap stays byte-comparable after the run; promotions are exercised by the source
            // run that builds the loaded lineage.
            Criteria: new EvaluationCriteria(MarginBbPer100: 10_000.0, MinSeeds: 2),
            DataDirectory: targetDirectory,
            HarnessRefinement: true,
            BootstrapMode: mode,
            BootstrapFrom: sourceDirectory);

    private static string ReadPointerStrategyId(string dataDirectory)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(dataDirectory, TrainingLoop.ChampionFileName)));
        return document.RootElement.GetProperty("strategyId").GetString()!;
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
}
