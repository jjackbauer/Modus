using System.Text.Json;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

public sealed class TrainingLoopTests
{
    [Fact]
    public void Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-always");
        try
        {
            var options = new TrainingOptions(
                Seed: 20260101UL,
                Iterations: 1,
                TrainingHands: 12,
                EvaluationSeeds: 4,
                EvaluationHandsPerMatch: 4,
                DataDirectory: dataDirectory);

            var run = TrainingLoop.Run(options, new AlwaysPromotePolicy());

            Assert.Single(run.Iterations);
            var iteration = run.Iterations[0];
            Assert.False(iteration.Verified);
            Assert.Equal("verification-failed", iteration.Outcome);
            Assert.False(iteration.Decision.Promoted);
            Assert.Equal(iteration.ChampionId, run.FinalChampionId);

            // A fresh-directory pointer records the version-1 champion actually used,
            // and a refused promotion must never move it.
            var pointerPath = Path.Combine(dataDirectory, "active-champion.json");
            Assert.True(File.Exists(pointerPath), "A fresh data directory must persist its champion pointer.");
            using (var pointer = JsonDocument.Parse(File.ReadAllBytes(pointerPath)))
            {
                Assert.Equal(iteration.ChampionId, pointer.RootElement.GetProperty("strategyId").GetString());
                Assert.Equal(0, pointer.RootElement.GetProperty("iteration").GetInt32());
            }

            // The policy did say "promote"; only the verifier stopped it.
            Assert.StartsWith("verifier:", iteration.Decision.Reason);
            Assert.Equal(1, iteration.Iteration);

            using var ledger = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "ledger.json")));
            var entry = Assert.Single(ledger.RootElement.EnumerateArray().ToArray());
            Assert.False(entry.GetProperty("promoted").GetBoolean());
            Assert.Equal("verification-failed", entry.GetProperty("outcome").GetString());
            Assert.Equal(iteration.ChallengerId, entry.GetProperty("challengerId").GetString());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void MatchEvaluator_GivenChallengerIdenticalToIncumbent_ExpectedNonPositiveLowerBoundAndNoPromotion()
    {
        var genome = new StrategyGenome();
        var report = MatchEvaluator.Evaluate(
            new StrategyAgent(genome, "challenger"),
            "identical-challenger",
            new StrategyAgent(genome, "incumbent"),
            "identical-incumbent",
            baseSeed: 20260101UL,
            seeds: 24,
            handsPerMatch: 4,
            startingStack: 1000,
            bigBlind: 10);

        Assert.Equal(24, report.Seeds);
        Assert.True(
            report.LowerBoundBbPer100 <= 0.0,
            $"An identical challenger must not show a positive lower bound; observed {report.LowerBoundBbPer100} (mean {report.MeanBbPer100}).");

        var decision = new StatisticalPromotionPolicy().Decide(report, new EvaluationCriteria());
        Assert.False(decision.Promoted);
        Assert.Equal("lcb-below-margin", decision.Reason);
    }

    [Fact]
    public void Run_GivenSameOptionsIntoTwoDirectories_ExpectedIdenticalArtifactsReportsLedgerAndChampion()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-repro-first");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-repro-second");
        var thirdDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-repro-third");
        try
        {
            var options = new TrainingOptions(
                Seed: 314159UL,
                Iterations: 2,
                TrainingHands: 20,
                EvaluationSeeds: 6,
                EvaluationHandsPerMatch: 4);

            var first = TrainingLoop.Run(options with { DataDirectory = firstDirectory });
            var second = TrainingLoop.Run(options with { DataDirectory = secondDirectory });
            var differentSeed = TrainingLoop.Run(options with { Seed = 314161UL, DataDirectory = thirdDirectory });

            Assert.Equal(2, first.Iterations.Count);
            Assert.Equal(2, second.Iterations.Count);
            Assert.Equal(first.FinalChampionId, second.FinalChampionId);
            Assert.Equal(first.LedgerJson, second.LedgerJson);

            for (var index = 0; index < first.Iterations.Count; index++)
            {
                Assert.Equal(first.Iterations[index].ChampionId, second.Iterations[index].ChampionId);
                Assert.Equal(first.Iterations[index].ChallengerId, second.Iterations[index].ChallengerId);
                Assert.Equal(first.Iterations[index].Report.ReportHash, second.Iterations[index].Report.ReportHash);
                Assert.Equal(first.Iterations[index].Report.ToCanonicalJson(), second.Iterations[index].Report.ToCanonicalJson());
                Assert.Equal(first.Iterations[index].Outcome, second.Iterations[index].Outcome);
                Assert.Equal(first.Iterations[index].Verified, second.Iterations[index].Verified);
            }

            var firstStrategyFiles = StrategyFileNames(firstDirectory);
            Assert.Equal(firstStrategyFiles, StrategyFileNames(secondDirectory));
            foreach (var name in firstStrategyFiles)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(firstDirectory, "strategies", name)),
                    File.ReadAllBytes(Path.Combine(secondDirectory, "strategies", name)));
            }

            var firstEvaluationFiles = EvaluationFileNames(firstDirectory);
            Assert.Equal(firstEvaluationFiles, EvaluationFileNames(secondDirectory));
            foreach (var name in firstEvaluationFiles)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(firstDirectory, "evaluations", name)),
                    File.ReadAllBytes(Path.Combine(secondDirectory, "evaluations", name)));
            }

            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory, "ledger.json")),
                File.ReadAllBytes(Path.Combine(secondDirectory, "ledger.json")));

            // A different seed must change the proposal stream, never only the evaluation numbers.
            var firstIds = first.Iterations
                .SelectMany(static iteration => new[] { iteration.ChampionId, iteration.ChallengerId })
                .Append(first.FinalChampionId)
                .ToArray();
            var otherIds = differentSeed.Iterations
                .SelectMany(static iteration => new[] { iteration.ChampionId, iteration.ChallengerId })
                .Append(differentSeed.FinalChampionId)
                .ToArray();
            Assert.False(
                firstIds.SequenceEqual(otherIds, StringComparer.Ordinal),
                "A different training seed must change at least one artifact id: " + string.Join(",", firstIds));
            Assert.False(first.LedgerJson.SequenceEqual(differentSeed.LedgerJson, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
            Directory.Delete(thirdDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenTrainingOptions_ExpectedPersistedStrategyEvaluationLedgerAndChampionSchema()
    {
        var rejectedDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-rejected");
        var promotedDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-promoted");
        try
        {
            var rejectedOptions = new TrainingOptions(
                Seed: 271828UL,
                Iterations: 2,
                TrainingHands: 12,
                EvaluationSeeds: 4,
                EvaluationHandsPerMatch: 4,
                DataDirectory: rejectedDirectory);
            var rejected = TrainingLoop.Run(rejectedOptions);

            Assert.Equal(2, rejected.Iterations.Count);
            Assert.All(rejected.Iterations, static iteration => Assert.Equal("rejected", iteration.Outcome));
            Assert.Equal(rejected.Iterations[0].ChampionId, rejected.FinalChampionId);

            // A rejected challenger never moves the pointer, which names the version-1
            // champion the loop actually used.
            var rejectedPointerPath = Path.Combine(rejectedDirectory, "active-champion.json");
            Assert.True(File.Exists(rejectedPointerPath));
            using (var pointer = JsonDocument.Parse(File.ReadAllBytes(rejectedPointerPath)))
            {
                Assert.Equal(rejected.Iterations[0].ChampionId, pointer.RootElement.GetProperty("strategyId").GetString());
            }
            Assert.Equal(3, StrategyFileNames(rejectedDirectory).Length);
            Assert.Equal(2, EvaluationFileNames(rejectedDirectory).Length);
            AssertPersistedArtifact(rejectedDirectory, rejected.Iterations[0].ChampionId, version: 1, createdAtIteration: 0);
            AssertPersistedArtifact(rejectedDirectory, rejected.Iterations[0].ChallengerId, version: 2, createdAtIteration: 1);
            AssertPersistedReport(
                rejectedDirectory,
                rejected.Iterations[0].ChallengerId,
                rejected.Iterations[0].ChampionId,
                rejected.Iterations[0].Report);

            var rejectedLedger = ReadLedger(rejectedDirectory);
            Assert.Equal(2, rejectedLedger.Length);
            for (var index = 0; index < rejectedLedger.Length; index++)
            {
                var entry = rejectedLedger[index];
                Assert.Equal(index + 1, entry.GetProperty("iteration").GetInt32());
                Assert.Equal(rejected.Iterations[index].ChampionId, entry.GetProperty("championId").GetString());
                Assert.Equal(rejected.Iterations[index].ChallengerId, entry.GetProperty("challengerId").GetString());
                Assert.False(entry.GetProperty("promoted").GetBoolean());
                Assert.Equal(rejected.Iterations[index].Decision.Reason, entry.GetProperty("reason").GetString());
                Assert.Equal(rejected.Iterations[index].Report.MeanBbPer100, entry.GetProperty("meanBbPer100").GetDouble(), 9);
                Assert.Equal(rejected.Iterations[index].Report.LowerBoundBbPer100, entry.GetProperty("lowerBoundBbPer100").GetDouble(), 9);
                Assert.Equal(5.0, entry.GetProperty("marginBbPer100").GetDouble(), 9);
                Assert.Equal(4, entry.GetProperty("seeds").GetInt32());
                Assert.True(entry.GetProperty("verified").GetBoolean());
                Assert.Equal("rejected", entry.GetProperty("outcome").GetString());
                Assert.Equal(
                    rejected.Iterations[index].Report.ChallengerWins / (double)(2 * rejected.Iterations[index].Report.Seeds),
                    entry.GetProperty("winRate").GetDouble(),
                    9);
            }

            // A permissive criterion (negative margin, tiny sample floor) makes the same pipeline
            // promote, which is the only case that writes the champion pointer.
            var promotedOptions = new TrainingOptions(
                Seed: 271828UL,
                Iterations: 1,
                TrainingHands: 12,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 4,
                Criteria: new EvaluationCriteria(MarginBbPer100: -10_000.0, MinSeeds: 2),
                DataDirectory: promotedDirectory);
            var promoted = TrainingLoop.Run(promotedOptions);

            var promotedIteration = Assert.Single(promoted.Iterations);
            Assert.True(promotedIteration.Verified);
            Assert.True(promotedIteration.Decision.Promoted);
            Assert.Equal("promoted", promotedIteration.Outcome);
            Assert.Equal(promotedIteration.ChallengerId, promoted.FinalChampionId);

            using var champion = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(promotedDirectory, "active-champion.json")));
            Assert.Equal(promotedIteration.ChallengerId, champion.RootElement.GetProperty("strategyId").GetString());
            Assert.Equal(1, champion.RootElement.GetProperty("iteration").GetInt32());

            var promotedLedger = ReadLedger(promotedDirectory);
            var promotedEntry = Assert.Single(promotedLedger);
            Assert.True(promotedEntry.GetProperty("promoted").GetBoolean());
            Assert.Equal("promoted", promotedEntry.GetProperty("reason").GetString());
        }
        finally
        {
            Directory.Delete(rejectedDirectory, recursive: true);
            Directory.Delete(promotedDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenExistingLedger_ExpectedEntriesFromBothRunsAndDeduplication()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-ledger-accumulation");
        try
        {
            var options = new TrainingOptions(
                Seed: 424242UL,
                Iterations: 1,
                TrainingHands: 8,
                EvaluationSeeds: 2,
                EvaluationHandsPerMatch: 2,
                DataDirectory: dataDirectory);

            var first = TrainingLoop.Run(options);
            var firstChallenger = Assert.Single(first.Iterations).ChallengerId;
            Assert.Single(ReadLedger(dataDirectory));
            Assert.Single(first.LedgerJson);

            var second = TrainingLoop.Run(options with { Seed = 424243UL });
            var secondChallenger = Assert.Single(second.Iterations).ChallengerId;
            Assert.NotEqual(firstChallenger, secondChallenger);

            var accumulated = ReadLedger(dataDirectory);
            Assert.Equal(2, accumulated.Length);
            Assert.Equal(
                new[] { firstChallenger, secondChallenger },
                accumulated.Select(static entry => entry.GetProperty("challengerId").GetString()).ToArray());
            Assert.Equal(2, second.LedgerJson.Count);

            // The same (iteration, championId, challengerId) is replaced, never duplicated.
            var repeat = TrainingLoop.Run(options);
            Assert.Equal(1, Assert.Single(repeat.Iterations).Iteration);
            Assert.Equal(2, ReadLedger(dataDirectory).Length);
            Assert.Equal(2, repeat.LedgerJson.Count);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenCorruptChampionPointer_ExpectedInvalidOperationExceptionNamingPointerAndStrategy()
    {
        var missingDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-pointer-missing");
        var unparsableDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-pointer-unparsable");
        const string StrategyId = "tholdem-strategy-v9-deadbeef";
        try
        {
            var missingPointer = Path.Combine(missingDirectory, "active-champion.json");
            File.WriteAllText(
                missingPointer,
                "{\"strategyId\":\"" + StrategyId + "\",\"iteration\":3}");

            var missing = Assert.Throws<InvalidOperationException>(
                () => TrainingLoop.Run(ProbeOptions(missingDirectory)));
            Assert.Contains(missingPointer, missing.Message, StringComparison.Ordinal);
            Assert.Contains(StrategyId, missing.Message, StringComparison.Ordinal);

            var strategyDirectory = Path.Combine(unparsableDirectory, "strategies");
            Directory.CreateDirectory(strategyDirectory);
            File.WriteAllText(Path.Combine(strategyDirectory, StrategyId + ".json"), "{ this is not an artifact");
            var unparsablePointer = Path.Combine(unparsableDirectory, "active-champion.json");
            File.WriteAllText(
                unparsablePointer,
                "{\"strategyId\":\"" + StrategyId + "\",\"iteration\":3}");

            var unparsable = Assert.Throws<InvalidOperationException>(
                () => TrainingLoop.Run(ProbeOptions(unparsableDirectory)));
            Assert.Contains(unparsablePointer, unparsable.Message, StringComparison.Ordinal);
            Assert.Contains(StrategyId, unparsable.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(missingDirectory, recursive: true);
            Directory.Delete(unparsableDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenFreshDirectory_ExpectedPointerToVersionOneChampionActuallyUsed()
    {
        var dataDirectory = AgentTestSupport.CreateTempDirectory("tholdem-learning-fresh-pointer");
        try
        {
            var run = TrainingLoop.Run(
                new TrainingOptions(
                    Seed: 999UL,
                    Iterations: 1,
                    TrainingHands: 4,
                    EvaluationSeeds: 1,
                    EvaluationHandsPerMatch: 1,
                    DataDirectory: dataDirectory));

            var expectedChampion = StrategyArtifact.Create(new StrategyGenome(), 0, null);
            var pointerPath = Path.Combine(dataDirectory, "active-champion.json");
            Assert.True(File.Exists(pointerPath), $"A fresh run must persist {pointerPath}.");
            using var pointer = JsonDocument.Parse(File.ReadAllBytes(pointerPath));
            var strategyId = pointer.RootElement.GetProperty("strategyId").GetString();
            Assert.Equal(expectedChampion.Id, strategyId);
            Assert.Equal(1, expectedChampion.Version);
            Assert.Equal(run.Iterations[0].ChampionId, strategyId);
            Assert.Equal(0, pointer.RootElement.GetProperty("iteration").GetInt32());
            Assert.True(
                File.Exists(Path.Combine(dataDirectory, "strategies", expectedChampion.Id + ".json")),
                "The pointer must name a persisted version-1 artifact.");
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static TrainingOptions ProbeOptions(string dataDirectory) =>
        new(
            Seed: 1UL,
            Iterations: 0,
            TrainingHands: 1,
            EvaluationSeeds: 1,
            EvaluationHandsPerMatch: 1,
            DataDirectory: dataDirectory);

    private static void AssertPersistedArtifact(string dataDirectory, string id, int version, int createdAtIteration)
    {
        var path = Path.Combine(dataDirectory, "strategies", id + ".json");
        Assert.True(File.Exists(path), $"Missing strategy artifact {path}.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetString());
        Assert.Equal(version, root.GetProperty("version").GetInt32());
        Assert.True(root.GetProperty("parameters").TryGetProperty("raiseThreshold", out _));
        Assert.True(root.GetProperty("parameters").TryGetProperty("callThreshold", out _));
        Assert.True(root.GetProperty("parameters").TryGetProperty("bluffFrequency", out _));
        Assert.True(root.GetProperty("parameters").TryGetProperty("sizingMultiplier", out _));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("promptText").GetString()));
        Assert.Equal(createdAtIteration, root.GetProperty("createdAtIteration").GetInt32());
    }

    private static void AssertPersistedReport(
        string dataDirectory,
        string challengerId,
        string incumbentId,
        EvaluationReport expected)
    {
        var path = Path.Combine(dataDirectory, "evaluations", challengerId + "-vs-" + incumbentId + ".json");
        Assert.True(File.Exists(path), $"Missing evaluation report {path}.");
        var text = File.ReadAllText(path);
        Assert.Equal(expected.ToCanonicalJson(), text);
        Assert.Equal(expected.ReportHash, LearningTestSupport.Sha256Hex(LearningTestSupport.Utf8(text)));
    }

    private static JsonElement[] ReadLedger(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "ledger.json");
        Assert.True(File.Exists(path), $"Missing ledger {path}.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        return document.RootElement.EnumerateArray().Select(static entry => entry.Clone()).ToArray();
    }

    private static string[] StrategyFileNames(string dataDirectory) =>
        Directory.GetFiles(Path.Combine(dataDirectory, "strategies"), "*.json")
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static string[] EvaluationFileNames(string dataDirectory) =>
        Directory.GetFiles(Path.Combine(dataDirectory, "evaluations"), "*.json")
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
}
