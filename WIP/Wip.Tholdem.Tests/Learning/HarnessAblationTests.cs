using System.Text;
using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// Runtime behaviour of the bounded ablation runner (R-11). Every scenario executes real training
/// loops in per-configuration scratch directories and asserts persisted bytes: the canonical
/// <c>tholdem.ablation.v1</c> report, both caller-root pointer files, and the ledger challenger
/// choices of each configuration.
/// </summary>
public sealed class HarnessAblationTests
{
    /// <summary>The pinned seed whose first-iteration signature is <c>aggression-by-street-low</c>.</summary>
    private const ulong AblationSeed = 20_261_111UL;

    private const int AblationIterations = 1;

    private const int TrainingHands = 24;

    private const int EvaluationSeeds = 2;

    private const int EvaluationHandsPerMatch = 2;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Run_GivenSixConfigurations_ExpectedCanonicalReportAndNoPointerWrite()
    {
        var root = AgentTestSupport.CreateTempDirectory("tholdem-ablation-six");
        try
        {
            var championBytes = Utf8.GetBytes("{\"strategyId\":\"sentinel-champion\",\"iteration\":7}");
            var proposerBytes = Utf8.GetBytes("{\"proposerId\":\"sentinel-proposer\",\"generation\":3}");
            File.WriteAllBytes(Path.Combine(root, TrainingLoop.ChampionFileName), championBytes);
            Directory.CreateDirectory(Path.Combine(root, "meta"));
            File.WriteAllBytes(Path.Combine(root, "meta", "active-proposer.json"), proposerBytes);

            var report = HarnessAblationRunner.Run(Options(root));

            // The caller's pointers are byte-identical and the runner wrote nothing else at the root.
            Assert.Equal(championBytes, File.ReadAllBytes(Path.Combine(root, TrainingLoop.ChampionFileName)));
            Assert.Equal(proposerBytes, File.ReadAllBytes(Path.Combine(root, "meta", "active-proposer.json")));
            Assert.Equal(
                new[] { "ablation", "active-champion.json", "meta" },
                Directory.GetFileSystemEntries(root)
                    .Select(static path => Path.GetFileName(path)!)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray());

            var reportPath = ReportPath(root);
            Assert.True(File.Exists(reportPath), $"The runner must emit {reportPath}.");
            var bytes = File.ReadAllBytes(reportPath);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "report.json must be UTF-8 without a BOM.");
            var text = Utf8.GetString(bytes);
            Assert.DoesNotContain("\r", text);
            Assert.DoesNotContain("\n", text);
            Assert.StartsWith(
                "{\"schema\":\"" + HarnessAblationReport.Schema
                    + "\",\"seed\":20261111,\"iterations\":1,\"configurations\":[",
                text,
                StringComparison.Ordinal);
            Assert.Equal(report.ToCanonicalJson(), text);

            using var document = JsonDocument.Parse(bytes);
            var rootElement = document.RootElement;
            Assert.Equal(
                new[] { "schema", "seed", "iterations", "configurations" },
                rootElement.EnumerateObject().Select(static property => property.Name).ToArray());
            Assert.Equal(HarnessAblationReport.Schema, rootElement.GetProperty("schema").GetString());
            Assert.Equal(AblationSeed, rootElement.GetProperty("seed").GetUInt64());
            Assert.Equal(AblationIterations, rootElement.GetProperty("iterations").GetInt32());

            var rows = rootElement.GetProperty("configurations").EnumerateArray().ToArray();
            Assert.Equal(6, rows.Length);
            Assert.Equal(
                new[] { "none", "signatures", "memory", "lineage", "skills", "all" },
                rows.Select(static row => row.GetProperty("config").GetString()).ToArray());
            foreach (var row in rows)
            {
                Assert.Equal(
                    new[]
                    {
                        "config",
                        "finalChampionId",
                        "iterations",
                        "verifiedPromotions",
                        "meanBbPer100",
                        "zeroEffectIterations",
                        "inertProposals",
                        "memoryEntries",
                        "memoryContentHash",
                        "note",
                    },
                    row.EnumerateObject().Select(static property => property.Name).ToArray());
                Assert.Equal(AblationIterations, row.GetProperty("iterations").GetInt32());
                Assert.Equal(0, row.GetProperty("verifiedPromotions").GetInt32());
                Assert.Equal(JsonValueKind.Number, row.GetProperty("meanBbPer100").ValueKind);
                Assert.Equal(JsonValueKind.String, row.GetProperty("finalChampionId").ValueKind);
            }

            var none = Row(rootElement, "none");
            Assert.Equal(0, none.GetProperty("memoryEntries").GetInt32());
            Assert.Equal(JsonValueKind.Null, none.GetProperty("memoryContentHash").ValueKind);

            var signatures = Row(rootElement, "signatures");
            Assert.True(signatures.GetProperty("memoryEntries").GetInt32() > 0);
            Assert.Equal(JsonValueKind.String, signatures.GetProperty("memoryContentHash").ValueKind);

            Assert.Contains(
                "write-only",
                Row(rootElement, "memory").GetProperty("note").GetString()!,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "read-only",
                Row(rootElement, "lineage").GetProperty("note").GetString()!,
                StringComparison.OrdinalIgnoreCase);

            // Every configuration ran a real loop in its own scratch directory; no proposer pointer
            // was ever created outside the caller root's pre-existing sentinel.
            foreach (var name in new[] { "none", "signatures", "memory", "lineage", "skills", "all" })
            {
                var dataDirectory = Path.Combine(root, HarnessAblationRunner.AblationDirectoryName, name, "data");
                Assert.True(File.Exists(Path.Combine(dataDirectory, TrainingLoop.ChampionFileName)));
                Assert.True(File.Exists(Path.Combine(dataDirectory, TrainingLoop.LedgerFileName)));
                Assert.False(File.Exists(Path.Combine(dataDirectory, "meta", "active-proposer.json")));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSameOptionsTwice_ExpectedIdenticalReports()
    {
        var firstRoot = AgentTestSupport.CreateTempDirectory("tholdem-ablation-repeat-a");
        var secondRoot = AgentTestSupport.CreateTempDirectory("tholdem-ablation-repeat-b");
        try
        {
            var first = HarnessAblationRunner.Run(Options(firstRoot));
            var second = HarnessAblationRunner.Run(Options(secondRoot));

            var firstBytes = File.ReadAllBytes(ReportPath(firstRoot));
            var secondBytes = File.ReadAllBytes(ReportPath(secondRoot));
            Assert.Equal(firstBytes, secondBytes);
            Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
            Assert.Equal(6, first.Configurations.Count);
            Assert.Equal(6, second.Configurations.Count);
            Assert.Equal(
                first.Configurations.Select(
                    static row => row.Config + "|" + row.FinalChampionId + "|" + row.MemoryContentHash),
                second.Configurations.Select(
                    static row => row.Config + "|" + row.FinalChampionId + "|" + row.MemoryContentHash));
        }
        finally
        {
            Directory.Delete(firstRoot, recursive: true);
            Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenOptionAboveIterationBound_ExpectedStableRefusal()
    {
        var root = AgentTestSupport.CreateTempDirectory("tholdem-ablation-iteration-bound");
        try
        {
            var refusal = Assert.Throws<HarnessAblationException>(
                () => HarnessAblationRunner.Run(
                    Options(root) with { Iterations = HarnessAblationRunner.MaximumIterations + 1 }));

            Assert.Equal(HarnessAblationException.InvalidOptionCode, refusal.Code);
            Assert.Contains("Iterations", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, HarnessAblationRunner.AblationDirectoryName)));
            Assert.False(File.Exists(ReportPath(root)));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenOtherOutOfBoundOptions_ExpectedStableRefusal()
    {
        var root = AgentTestSupport.CreateTempDirectory("tholdem-ablation-other-bounds");
        try
        {
            var valid = Options(root);
            var cases = new (string Option, HarnessAblationOptions Invalid)[]
            {
                ("Iterations", valid with { Iterations = 0 }),
                ("TrainingHands", valid with { TrainingHands = 0 }),
                ("TrainingHands", valid with { TrainingHands = HarnessAblationRunner.MaximumTrainingHands + 1 }),
                ("EvaluationSeeds", valid with { EvaluationSeeds = 0 }),
                ("EvaluationSeeds", valid with { EvaluationSeeds = HarnessAblationRunner.MaximumEvaluationSeeds + 1 }),
                ("EvaluationHandsPerMatch", valid with { EvaluationHandsPerMatch = 0 }),
                (
                    "EvaluationHandsPerMatch",
                    valid with
                    {
                        EvaluationHandsPerMatch = HarnessAblationRunner.MaximumEvaluationHandsPerMatch + 1,
                    }),
                ("DataDirectory", valid with { DataDirectory = " " }),
                (
                    "WallClockCap",
                    valid with { WallClockCap = HarnessAblationRunner.DefaultWallClockCap + TimeSpan.FromTicks(1) }),
                ("WallClockCap", valid with { WallClockCap = TimeSpan.FromTicks(-1) }),
            };

            foreach (var (option, invalid) in cases)
            {
                var refusal = Assert.Throws<HarnessAblationException>(() => HarnessAblationRunner.Run(invalid));
                Assert.Equal(HarnessAblationException.InvalidOptionCode, refusal.Code);
                Assert.Contains(option, refusal.Message, StringComparison.OrdinalIgnoreCase);
            }

            Assert.False(Directory.Exists(Path.Combine(root, HarnessAblationRunner.AblationDirectoryName)));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenExhaustedWallClockBudget_ExpectedStableRefusal()
    {
        var root = AgentTestSupport.CreateTempDirectory("tholdem-ablation-wall-clock");
        try
        {
            var refusal = Assert.Throws<HarnessAblationException>(
                () => HarnessAblationRunner.Run(Options(root) with { WallClockCap = TimeSpan.Zero }));

            Assert.Equal(HarnessAblationException.WallClockExceededCode, refusal.Code);
            Assert.False(Directory.Exists(Path.Combine(root, HarnessAblationRunner.AblationDirectoryName)));
            Assert.False(File.Exists(ReportPath(root)));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenMemoryAndLineageConfigs_ExpectedSameChallengersAsSignatures()
    {
        var root = AgentTestSupport.CreateTempDirectory("tholdem-ablation-no-difference");
        try
        {
            var report = HarnessAblationRunner.Run(Options(root));

            var legacy = ChallengerIds(root, "none");
            var signatures = ChallengerIds(root, "signatures");
            var memory = ChallengerIds(root, "memory");
            var lineage = ChallengerIds(root, "lineage");
            var skills = ChallengerIds(root, "skills");
            var all = ChallengerIds(root, "all");

            // Signature-driven refinement is behaviourally active for this pinned shape, so the
            // memory/lineage equality below is a real finding rather than a vacuous comparison.
            Assert.Single(legacy);
            Assert.Single(signatures);
            Assert.NotEqual(legacy, signatures);

            // Memory is write-only and lineage is read-only: neither can change a challenger.
            Assert.Equal(signatures, memory);
            Assert.Equal(signatures, lineage);
            Assert.Equal(skills, all);
            Assert.NotEqual(signatures, skills);

            var rows = report.Configurations.ToDictionary(static row => row.Config, StringComparer.Ordinal);
            Assert.Equal(rows["signatures"].FinalChampionId, rows["memory"].FinalChampionId);
            Assert.Equal(rows["signatures"].FinalChampionId, rows["lineage"].FinalChampionId);
            Assert.Equal(rows["signatures"].MemoryContentHash, rows["memory"].MemoryContentHash);
            Assert.Equal(rows["signatures"].MemoryContentHash, rows["lineage"].MemoryContentHash);
            foreach (var name in new[] { "signatures", "memory", "lineage" })
            {
                Assert.True(rows[name].MemoryEntries > 0, $"The {name} configuration must write memory entries.");
                Assert.NotNull(rows[name].MemoryContentHash);
            }

            Assert.Contains("write-only", rows["memory"].Note, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("read-only", rows["lineage"].Note, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HarnessAblationOptions Options(string root) =>
        new(
            Seed: AblationSeed,
            Iterations: AblationIterations,
            TrainingHands: TrainingHands,
            EvaluationSeeds: EvaluationSeeds,
            EvaluationHandsPerMatch: EvaluationHandsPerMatch,
            DataDirectory: root);

    private static string ReportPath(string root) =>
        Path.Combine(root, HarnessAblationRunner.AblationDirectoryName, HarnessAblationRunner.ReportFileName);

    private static JsonElement Row(JsonElement rootElement, string config) =>
        rootElement.GetProperty("configurations")
            .EnumerateArray()
            .Single(row => string.Equals(row.GetProperty("config").GetString(), config, StringComparison.Ordinal));

    private static string[] ChallengerIds(string root, string config)
    {
        var ledgerPath = Path.Combine(
            root,
            HarnessAblationRunner.AblationDirectoryName,
            config,
            "data",
            TrainingLoop.LedgerFileName);
        Assert.True(File.Exists(ledgerPath), $"The {config} configuration must persist {ledgerPath}.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(ledgerPath));
        return document.RootElement
            .EnumerateArray()
            .Select(static entry => entry.GetProperty("challengerId").GetString()!)
            .ToArray();
    }
}
