using System.Text;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class HistoryIntegrityTests
{
    [Fact]
    public void Run_GivenHistoryDirectory_ExpectedCanonicalBytesManifestHashesAndExactReplay()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-history-integrity");
        try
        {
            var report = MatchSimulator.Run(
                new RandomLegalAgent("integrity-seat-0"),
                new RandomLegalAgent("integrity-seat-1"),
                new SimulationOptions(777UL, 120, HistoryDirectory: directory));

            var manifest = AgentTestSupport.ReadManifest(directory);
            Assert.Equal("tholdem.session.v1", manifest.Schema);
            Assert.Equal(report.SessionId, manifest.SessionId);
            Assert.Equal(777UL, manifest.Seed);
            Assert.Equal(120, manifest.Hands);
            Assert.Equal(1000L, manifest.StartingStack);
            Assert.Equal(5L, manifest.SmallBlind);
            Assert.Equal(10L, manifest.BigBlind);
            Assert.Equal(new[] { "integrity-seat-0", "integrity-seat-1" }, manifest.Agents);
            Assert.Equal(120, manifest.HandHashes.Length);
            Assert.Equal(report.Agents[0].Hands, manifest.Hands);

            var manifestBytes = File.ReadAllBytes(Path.Combine(directory, "session.json"));
            Assert.Equal(report.ManifestHash, AgentTestSupport.Sha256Hex(manifestBytes));
            Assert.Equal((byte)'{', manifestBytes[0]);
            Assert.NotEqual((byte)0xEF, manifestBytes[0]);

            var manifestText = Encoding.UTF8.GetString(manifestBytes);
            Assert.StartsWith("{\"schema\":\"tholdem.session.v1\",\"sessionId\":\"", manifestText);
            Assert.Contains(
                "\"seed\":777,\"hands\":120,\"startingStack\":1000,\"smallBlind\":5,\"bigBlind\":10,\"agents\":[\"integrity-seat-0\",\"integrity-seat-1\"],\"handHashes\":[",
                manifestText);
            Assert.DoesNotContain(" ", manifestText);
            Assert.DoesNotContain("\n", manifestText);

            for (var handNumber = 1; handNumber <= 120; handNumber++)
            {
                var path = Path.Combine(directory, $"hand-{handNumber:D5}.json");
                Assert.True(File.Exists(path), $"Missing hand history file: {path}");

                var bytes = File.ReadAllBytes(path);
                var text = Encoding.UTF8.GetString(bytes);
                var record = AgentTestSupport.ParseRecord(text);
                Assert.Equal(handNumber, record.HandNumber);
                Assert.Equal(record.ToCanonicalJson(), text);
                Assert.Equal(record.Hash, manifest.HandHashes[handNumber - 1]);
                Assert.Equal(AgentTestSupport.Sha256Hex(bytes), manifest.HandHashes[handNumber - 1]);
                Assert.NotEqual((byte)0xEF, bytes[0]);

                var replayed = HeadsUpHandEngine.Replay(record);
                Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
                Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
                Assert.Equal(record.Showdown, replayed.ShowdownReached);
                Assert.Equal(2000L, replayed.Stacks.Sum() + replayed.Pot);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}