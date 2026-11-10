using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class RandomLegalAgentTests
{
    [Fact]
    public void Run_GivenTwoRandomLegalAgents_ExpectedTwoHundredCompletedLegalHandsAndConservation()
    {
        var directory = AgentTestSupport.CreateTempDirectory("tholdem-random-legal");
        try
        {
            var options = new SimulationOptions(1234UL, 200, HistoryDirectory: directory);
            var report = MatchSimulator.Run(
                new RandomLegalAgent("random-legal-seat-0"),
                new RandomLegalAgent("random-legal-seat-1"),
                options);

            Assert.Equal(200, report.Hands);
            Assert.Equal(2, report.Agents.Count);
            Assert.Equal(200, report.Agents[0].Hands);
            Assert.Equal(200, report.Agents[1].Hands);
            Assert.Equal("random-legal-seat-0", report.Agents[0].AgentName);
            Assert.Equal("random-legal-seat-1", report.Agents[1].AgentName);

            var records = AgentTestSupport.ReadAllRecords(directory);
            Assert.Equal(200, records.Length);
            for (var index = 0; index < records.Length; index++)
            {
                var record = records[index];
                Assert.Equal(index + 1, record.HandNumber);
                Assert.Equal(2000L, record.FinalStacks.Sum());
                Assert.Equal(2000L, HeadsUpHandEngine.Replay(record).Stacks.Sum());
            }

            Assert.Equal(201, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}