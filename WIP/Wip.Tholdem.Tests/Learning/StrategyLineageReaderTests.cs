using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Runtime behaviour of the strategy-lineage reader: it walks real content-addressed artifacts from a
/// champion to its root, joins the hash-chained ledger for outcomes, and refuses missing parents,
/// cycles, tampered artifacts and tampered ledgers before returning any partial list.
/// </summary>
public sealed class StrategyLineageReaderTests
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Lineage_GivenChampionChain_ExpectedEntriesWithOutcomes()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-lineage-chain");
        try
        {
            const ulong Seed = 31_415_926UL;
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 2,
                    TrainingHands: 12,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    Criteria: new EvaluationCriteria(MarginBbPer100: -10_000.0, MinSeeds: 2),
                    DataDirectory: dataDirectory,
                    HarnessRefinement: true));

            Assert.Equal(2, run.Iterations.Count);
            Assert.All(run.Iterations, static iteration => Assert.Equal("promoted", iteration.Outcome));

            var entries = StrategyLineageReader.Read(dataDirectory, run.FinalChampionId);
            Assert.Equal(3, entries.Count);
            Assert.Equal(new[] { 1, 2, 3 }, entries.Select(static entry => entry.Version).ToArray());

            var root = entries[0];
            var second = entries[1];
            var third = entries[2];
            var expectedRootId = run.Iterations[0].ChampionId;
            var expectedSecondId = run.Iterations[0].ChallengerId;
            var expectedThirdId = run.Iterations[1].ChallengerId;

            Assert.Equal(expectedRootId, root.Id);
            Assert.Null(root.ParentId);
            Assert.Equal(0, root.CreatedAtIteration);
            Assert.Null(root.Outcome);
            Assert.Null(root.Reason);
            Assert.Null(root.MeanBbPer100);
            Assert.Null(root.LowerBoundBbPer100);
            Assert.Null(root.Seeds);

            Assert.Equal(expectedSecondId, second.Id);
            Assert.Equal(expectedRootId, second.ParentId);
            Assert.Equal(1, second.CreatedAtIteration);
            Assert.Equal("promoted", second.Outcome);
            Assert.Equal("promoted", second.Reason);
            Assert.Equal(run.Iterations[0].Report!.MeanBbPer100, second.MeanBbPer100!.Value, 12);
            Assert.Equal(run.Iterations[0].Report!.LowerBoundBbPer100, second.LowerBoundBbPer100!.Value, 12);
            Assert.Equal(run.Iterations[0].Report!.Seeds, second.Seeds);

            Assert.Equal(expectedThirdId, third.Id);
            Assert.Equal(expectedSecondId, third.ParentId);
            Assert.Equal(2, third.CreatedAtIteration);
            Assert.Equal("promoted", third.Outcome);
            Assert.Equal("promoted", third.Reason);
            Assert.Equal(run.Iterations[1].Report!.MeanBbPer100, third.MeanBbPer100!.Value, 12);
            Assert.Equal(run.Iterations[1].Report!.LowerBoundBbPer100, third.LowerBoundBbPer100!.Value, 12);
            Assert.Equal(run.Iterations[1].Report!.Seeds, third.Seeds);
            Assert.Equal(run.FinalChampionId, third.Id);

            // The outcomes are the persisted ledger rows, re-read independently here.
            using var ledger = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "ledger.json")));
            var rows = ledger.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal(expectedSecondId, rows[0].GetProperty("challengerId").GetString());
            Assert.Equal(expectedRootId, rows[0].GetProperty("championId").GetString());
            Assert.Equal(rows[0].GetProperty("outcome").GetString(), second.Outcome);
            Assert.Equal(rows[0].GetProperty("reason").GetString(), second.Reason);
            Assert.Equal(rows[0].GetProperty("meanBbPer100").GetDouble(), second.MeanBbPer100!.Value, 12);
            Assert.Equal(rows[0].GetProperty("lowerBoundBbPer100").GetDouble(), second.LowerBoundBbPer100!.Value, 12);
            Assert.Equal(rows[0].GetProperty("seeds").GetInt32(), second.Seeds);
            Assert.Equal(expectedThirdId, rows[1].GetProperty("challengerId").GetString());
            Assert.Equal(rows[1].GetProperty("outcome").GetString(), third.Outcome);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Lineage_GivenMissingParent_ExpectedStableRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-lineage-missing-parent");
        try
        {
            var parent = StrategyArtifact.Create(new StrategyGenome(), 0, parent: null);
            var child = StrategyArtifact.Create(
                new StrategyGenome(CallThreshold: 0.53),
                1,
                parent);
            WriteArtifact(dataDirectory, parent);
            WriteArtifact(dataDirectory, child);
            var parentPath = Path.Combine(dataDirectory, "strategies", parent.Id + ".json");
            File.Delete(parentPath);

            var refusal = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, child.Id));
            Assert.Equal(StrategyLineageReader.MissingParentCode, refusal.Code);
            Assert.Contains(parent.Id, refusal.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(parentPath));

            var missingChampion = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, "tholdem-strategy-v1-deadbeef"));
            Assert.Equal(StrategyLineageReader.MissingParentCode, missingChampion.Code);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Lineage_GivenCycle_ExpectedCycleRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-lineage-cycle");
        try
        {
            const string FirstId = "tholdem-strategy-v1-cycleaaa";
            const string SecondId = "tholdem-strategy-v2-cyclebbb";
            WriteRawArtifact(dataDirectory, FirstId, version: 1, parentId: SecondId, createdAtIteration: 0);
            WriteRawArtifact(dataDirectory, SecondId, version: 2, parentId: FirstId, createdAtIteration: 1);

            var fromFirst = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, FirstId));
            Assert.Equal(StrategyLineageReader.CycleCode, fromFirst.Code);
            Assert.Contains(FirstId, fromFirst.Message, StringComparison.Ordinal);

            var fromSecond = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, SecondId));
            Assert.Equal(StrategyLineageReader.CycleCode, fromSecond.Code);

            const string SelfId = "tholdem-strategy-v3-cycleself";
            WriteRawArtifact(dataDirectory, SelfId, version: 3, parentId: SelfId, createdAtIteration: 2);
            var selfCycle = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, SelfId));
            Assert.Equal(StrategyLineageReader.CycleCode, selfCycle.Code);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Lineage_GivenTamperedLedger_ExpectedStableRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-lineage-tampered-ledger");
        try
        {
            const ulong Seed = 6_161_803UL;
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: Seed,
                    Iterations: 1,
                    TrainingHands: 12,
                    EvaluationSeeds: 2,
                    EvaluationHandsPerMatch: 2,
                    DataDirectory: dataDirectory));

            var healthy = StrategyLineageReader.Read(dataDirectory, run.FinalChampionId);
            var root = Assert.Single(healthy);
            Assert.Equal(run.FinalChampionId, root.Id);
            Assert.Null(root.Outcome);

            var ledgerPath = Path.Combine(dataDirectory, "ledger.json");
            var ledgerText = File.ReadAllText(ledgerPath);
            var tampered = ledgerText.Replace("\"verified\":true", "\"verified\":false", StringComparison.Ordinal);
            Assert.NotEqual(ledgerText, tampered);
            File.WriteAllText(ledgerPath, tampered, Utf8);

            var refusal = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, run.FinalChampionId));
            Assert.Equal(StrategyLineageReader.LedgerIntegrityCode, refusal.Code);
            Assert.Contains("ledger", refusal.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Lineage_GivenTamperedArtifact_ExpectedStableRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-lineage-tampered-artifact");
        try
        {
            var parent = StrategyArtifact.Create(new StrategyGenome(), 0, parent: null);
            var child = StrategyArtifact.Create(
                new StrategyGenome(SizingMultiplier: 1.1),
                1,
                parent);
            WriteArtifact(dataDirectory, parent);
            WriteArtifact(dataDirectory, child);

            var childPath = Path.Combine(dataDirectory, "strategies", child.Id + ".json");
            var canonical = File.ReadAllText(childPath);
            var tampered = canonical.Replace("\"version\":2", "\"version\":3", StringComparison.Ordinal);
            Assert.NotEqual(canonical, tampered);
            File.WriteAllText(childPath, tampered, Utf8);

            var refusal = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, child.Id));
            Assert.Equal(StrategyLineageReader.ArtifactHashMismatchCode, refusal.Code);
            Assert.Contains(child.Id, refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Lineage_GivenChainAboveDepthCap_ExpectedDepthRefusal()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-lineage-depth");
        try
        {
            Assert.Equal(64, StrategyLineageReader.MaximumDepth);
            var artifact = StrategyArtifact.Create(new StrategyGenome(), 0, parent: null);
            WriteArtifact(dataDirectory, artifact);
            for (var version = 2; version <= StrategyLineageReader.MaximumDepth; version++)
            {
                artifact = StrategyArtifact.Create(new StrategyGenome(), version - 1, artifact);
                WriteArtifact(dataDirectory, artifact);
            }

            var atCap = StrategyLineageReader.Read(dataDirectory, artifact.Id);
            Assert.Equal(StrategyLineageReader.MaximumDepth, atCap.Count);
            Assert.Equal(1, atCap[0].Version);
            Assert.Equal(StrategyLineageReader.MaximumDepth, atCap[^1].Version);

            var overCap = StrategyArtifact.Create(new StrategyGenome(), StrategyLineageReader.MaximumDepth, artifact);
            WriteArtifact(dataDirectory, overCap);
            var refusal = Assert.Throws<StrategyLineageException>(
                () => StrategyLineageReader.Read(dataDirectory, overCap.Id));
            Assert.Equal(StrategyLineageReader.DepthExceededCode, refusal.Code);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static void WriteArtifact(string dataDirectory, StrategyArtifact artifact)
    {
        var strategyDirectory = Path.Combine(dataDirectory, "strategies");
        Directory.CreateDirectory(strategyDirectory);
        File.WriteAllText(
            Path.Combine(strategyDirectory, artifact.Id + ".json"),
            artifact.ToCanonicalJson(),
            Utf8);
    }

    private static void WriteRawArtifact(
        string dataDirectory,
        string id,
        int version,
        string? parentId,
        int createdAtIteration)
    {
        var artifact = new StrategyArtifact(id, version, parentId, createdAtIteration, new StrategyGenome());
        WriteArtifact(dataDirectory, artifact);
    }
}
