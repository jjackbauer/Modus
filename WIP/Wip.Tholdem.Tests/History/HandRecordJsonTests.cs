using System.Text.Json;
using System.Text.Json.Nodes;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.History;

public sealed class HandRecordJsonTests
{
    [Fact]
    public void Parse_GivenPlayedHandsFromDifferentSeeds_ExpectedRoundTripAndMatchingHash()
    {
        var seeds = new ulong[] { 11UL, 22UL, 33UL };
        foreach (var seed in seeds)
        {
            var directory = AgentTestSupport.CreateTempDirectory("history-roundtrip");
            try
            {
                MatchSimulator.Run(
                    new TightAggressiveAgent(name: "roundtrip-seat-0"),
                    new RandomLegalAgent(name: "roundtrip-seat-1"),
                    new SimulationOptions(seed, 1, HistoryDirectory: directory));

                var json = File.ReadAllText(Path.Combine(directory, "hand-00001.json"));
                var parsed = HandRecordJson.Parse(json);

                Assert.Equal(json, parsed.ToCanonicalJson());
                Assert.Equal(parsed.Hash, AgentTestSupport.Sha256Hex(File.ReadAllBytes(Path.Combine(directory, "hand-00001.json"))));
                Assert.Equal(AgentTestSupport.ParseRecord(json).Seed, parsed.Seed);
                Assert.NotEqual(0UL, parsed.Seed);
                Assert.Equal(1, parsed.HandNumber);
                Assert.Equal(2, parsed.HoleCardsSeat0.Count);
                Assert.Equal(2, parsed.HoleCardsSeat1.Count);
                Assert.Equal(2, parsed.FinalStacks.Count);
                Assert.True(parsed.Actions.Count > 0);
                Assert.Equal(parsed.Actions.Count, parsed.ToCanonicalJson().Split("\"kind\":").Length - 1);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Parse_GivenCanonicalJsonWithWhitespace_ExpectedSameRecord()
    {
        var directory = AgentTestSupport.CreateTempDirectory("history-whitespace");
        try
        {
            MatchSimulator.Run(
                new TightAggressiveAgent(name: "whitespace-seat-0"),
                new RandomLegalAgent(name: "whitespace-seat-1"),
                new SimulationOptions(44UL, 1, HistoryDirectory: directory));
            var canonical = File.ReadAllText(Path.Combine(directory, "hand-00001.json"));
            var indented = JsonNode.Parse(canonical)!
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            Assert.NotEqual(canonical, indented);
            Assert.Equal(canonical, HandRecordJson.Parse(indented).ToCanonicalJson());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Parse_GivenMalformedInputs_ExpectedFormatExceptionWithReason()
    {
        var directory = AgentTestSupport.CreateTempDirectory("history-malformed");
        try
        {
            MatchSimulator.Run(
                new TightAggressiveAgent(name: "malformed-seat-0"),
                new RandomLegalAgent(name: "malformed-seat-1"),
                new SimulationOptions(55UL, 1, HistoryDirectory: directory));
            var canonical = File.ReadAllText(Path.Combine(directory, "hand-00001.json"));

            AssertFormat("not json at all", "JSON");
            AssertFormat("{}", "handNumber");

            var missing = (JsonObject)JsonNode.Parse(canonical)!.DeepClone();
            missing.Remove("finalStacks");
            AssertFormat(missing.ToJsonString(), "finalStacks");

            var badCard = (JsonObject)JsonNode.Parse(canonical)!.DeepClone();
            badCard["holeCardsSeat0"]!.AsArray()[0] = "Zz";
            AssertFormat(badCard.ToJsonString(), "Zz");

            var badWinners = (JsonObject)JsonNode.Parse(canonical)!.DeepClone();
            badWinners["winners"] = 7;
            AssertFormat(badWinners.ToJsonString(), "winners");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertFormat(string json, string expectedReason)
    {
        var exception = Assert.Throws<FormatException>(() => { _ = HandRecordJson.Parse(json); });
        Assert.Contains(expectedReason, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
