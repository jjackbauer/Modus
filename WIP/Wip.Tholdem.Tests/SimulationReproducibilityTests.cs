using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class SimulationReproducibilityTests
{
    [Fact]
    public void Run_GivenSameSeedAndAgents_ExpectedByteIdenticalFilesManifestAndReport()
    {
        var first = AgentTestSupport.CreateTempDirectory("tholdem-repro-first");
        var second = AgentTestSupport.CreateTempDirectory("tholdem-repro-second");
        try
        {
            var firstReport = RunInto(first, 1234UL);
            var secondReport = RunInto(second, 1234UL);

            var firstNames = FileNames(first);
            var secondNames = FileNames(second);
            Assert.Equal(121, firstNames.Length);
            Assert.Equal(firstNames, secondNames);

            foreach (var name in firstNames)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(first, name)),
                    File.ReadAllBytes(Path.Combine(second, name)));
            }

            Assert.Equal(firstReport.ManifestHash, secondReport.ManifestHash);
            Assert.Equal(firstReport.CanonicalJson, secondReport.CanonicalJson);
            Assert.Equal(firstReport.SessionId, secondReport.SessionId);
            Assert.Equal(64, firstReport.ManifestHash.Length);
            Assert.Equal(firstReport.ManifestHash, AgentTestSupport.Sha256Hex(
                File.ReadAllBytes(Path.Combine(first, "session.json"))));

            // A real per-hand derivation deals a fresh deck for nearly every hand. A derivation that
            // ignores the hand number collapses the whole session to one deal and fails here.
            Assert.True(
                AgentTestSupport.DistinctDeals(first) >= 100,
                $"Expected at least 100 distinct deals in 120 hands, found {AgentTestSupport.DistinctDeals(first)}.");
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenDifferentSeed_ExpectedDifferentManifestHandHashesAndReport()
    {
        var firstDirectory = AgentTestSupport.CreateTempDirectory("tholdem-seed-first");
        var secondDirectory = AgentTestSupport.CreateTempDirectory("tholdem-seed-second");
        try
        {
            var first = RunInto(firstDirectory, 1234UL);
            var second = RunInto(secondDirectory, 1235UL);

            Assert.NotEqual(first.ManifestHash, second.ManifestHash);
            Assert.NotEqual(first.CanonicalJson, second.CanonicalJson);
            Assert.NotEqual(first.SessionId, second.SessionId);
            Assert.Matches("^[0-9a-f]{64}$", first.ManifestHash);

            var firstManifest = AgentTestSupport.ReadManifest(firstDirectory);
            var secondManifest = AgentTestSupport.ReadManifest(secondDirectory);
            Assert.False(
                firstManifest.HandHashes.SequenceEqual(secondManifest.HandHashes, StringComparer.Ordinal),
                "A different session seed must deal different hands, not only embed a different seed value.");
            Assert.False(
                File.ReadAllBytes(Path.Combine(firstDirectory, "hand-00001.json"))
                    .SequenceEqual(File.ReadAllBytes(Path.Combine(secondDirectory, "hand-00001.json"))),
                "The first hand must differ between session seeds.");
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    private static SimulationReport RunInto(string directory, ulong seed) =>
        MatchSimulator.Run(
            new RandomLegalAgent("repro-seat-0"),
            new RandomLegalAgent("repro-seat-1"),
            new SimulationOptions(seed, 120, HistoryDirectory: directory));

    private static string[] FileNames(string directory) =>
        Directory.GetFiles(directory)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
}