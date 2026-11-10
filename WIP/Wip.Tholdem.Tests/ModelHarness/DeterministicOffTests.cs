using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Learning.Refinement;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// WP-5 isolation: with the harness refinement flag off and no refinement policy supplied, two runs
/// of the same seed persist byte-identical artifacts, write no transcript and create no harness
/// memory directory — the live path cannot touch the deterministic arm.
/// </summary>
public sealed class DeterministicOffTests
{
    private const ulong Seed = 1_099_511_627_776UL;

    private const string DefaultChampionArtifactSha256 =
        "6675d7975b69eafd7cd64f334e70ad5adbfa09fbcc32176a6098aa9649124c14";

    [Fact]
    public void Default_GivenLlmModeOff_ExpectedDeterministicArtifactsUnchanged()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-deterministic-off-a");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-deterministic-off-b");
        try
        {
            var options = new TrainingOptions(
                Seed: Seed,
                Iterations: 2,
                TrainingHands: 120,
                EvaluationSeeds: 24,
                EvaluationHandsPerMatch: 10);

            var firstRun = TrainingLoop.Run(options with { DataDirectory = firstDirectory });
            var secondRun = TrainingLoop.Run(options with { DataDirectory = secondDirectory });

            Assert.Equal(2, firstRun.Iterations.Count);
            Assert.Equal(2, secondRun.Iterations.Count);
            Assert.False(options.HarnessRefinement);
            Assert.Null(firstRun.Bootstrap);

            var first = ReadArtifacts(firstDirectory);
            var second = ReadArtifacts(secondDirectory);
            Assert.Equal(
                first.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray(),
                second.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray());
            Assert.NotEmpty(first);
            foreach (var artifact in first)
            {
                Assert.Equal(artifact.Value, second[artifact.Key]);
            }

            foreach (var directory in new[] { firstDirectory, secondDirectory })
            {
                Assert.False(
                    File.Exists(Path.Combine(directory, "llm-transcripts.jsonl")),
                    "A transcript file was written with the refinement flag off: " + directory);
                Assert.False(
                    Directory.Exists(Path.Combine(directory, HarnessMemoryStore.DirectoryName)),
                    "A harness memory directory was created with the refinement flag off: " + directory);
            }

            var championArtifact = Path.Combine(
                firstDirectory,
                "strategies",
                "tholdem-strategy-v1-f100e793.json");
            Assert.True(File.Exists(championArtifact), "The deterministic champion artifact is missing: " + championArtifact);
            Assert.Equal(
                DefaultChampionArtifactSha256,
                AgentTestSupport.Sha256Hex(File.ReadAllBytes(championArtifact)));

            using var pointer = System.Text.Json.JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(firstDirectory, TrainingLoop.ChampionFileName)));
            Assert.Equal(
                firstRun.FinalChampionId,
                pointer.RootElement.GetProperty("strategyId").GetString());
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    private static Dictionary<string, byte[]> ReadArtifacts(string directory) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                static path => path,
                static path => File.ReadAllBytes(path),
                StringComparer.Ordinal)
            .ToDictionary(
                pair => Path.GetRelativePath(directory, pair.Key).Replace('\\', '/'),
                static pair => pair.Value,
                StringComparer.Ordinal);
}
