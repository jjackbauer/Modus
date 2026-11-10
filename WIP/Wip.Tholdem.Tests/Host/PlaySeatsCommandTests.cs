using System.Text.Json;
using Xunit;

namespace Wip.Tholdem.Tests.Host;

/// <summary>
/// The multiway <c>play</c> surface: <c>--seats</c> 2–6, one agent name per seat, optional per-seat
/// starting stacks, and the unchanged two-seat oracle. Invalid seat counts, agent lists, names and
/// stacks are usage errors (exit 2) with a specific message.
/// </summary>
public sealed class PlaySeatsCommandTests
{
    private const string FourSeatAgents =
        "random-legal,tight-aggressive,pot-odds-equity,random-legal";

    [Fact]
    public void Run_GivenFourSeatsAgentsSeed42Hands3_ExpectedExitZeroAndFourSeatSession()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-seats4");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seats",
                "4",
                "--agents",
                FourSeatAgents,
                "--seed",
                "42",
                "--hands",
                "3",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);

            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            Assert.Equal(3, Directory.GetFiles(sessionDirectory, "hand-*.json").Length);

            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(sessionDirectory, "session.json")));
            Assert.Equal(4, manifest.RootElement.GetProperty("seats").GetInt32());
            Assert.Equal(
                FourSeatAgents.Split(','),
                manifest.RootElement.GetProperty("agents").EnumerateArray()
                    .Select(static agent => agent.GetString()!)
                    .ToArray());

            for (var hand = 1; hand <= 3; hand++)
            {
                using var record = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(sessionDirectory, $"hand-{hand:D5}.json")));
                Assert.Equal("tholdem.hand.v2", record.RootElement.GetProperty("schema").GetString());
                Assert.Equal(4, record.RootElement.GetProperty("seats").GetInt32());
                Assert.Equal(4, record.RootElement.GetProperty("holeCards").GetArrayLength());
                Assert.Equal(4, record.RootElement.GetProperty("finalStacks").GetArrayLength());
            }

            foreach (var agent in FourSeatAgents.Split(','))
            {
                Assert.Contains(agent, result.Output, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenTwoSeatsAndAgentOptions_ExpectedExistingAgentsStillWork()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-agent-options");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seed",
                "42",
                "--hands",
                "1",
                "--agent0",
                "random-legal",
                "--agent1",
                "random-legal",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            var manifest = AgentTestSupport.ReadManifest(
                Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory)));
            Assert.Equal(new[] { "random-legal", "random-legal" }, manifest.Agents);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenAgentsAndAgentOptions_ExpectedAgentsTakesPrecedence()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-agents-precedence");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seats",
                "2",
                "--agents",
                "random-legal,tight-aggressive",
                "--agent0",
                "tight-aggressive",
                "--agent1",
                "tight-aggressive",
                "--seed",
                "42",
                "--hands",
                "1",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(0, result.ExitCode);
            var manifest = AgentTestSupport.ReadManifest(
                Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory)));
            Assert.Equal(new[] { "random-legal", "tight-aggressive" }, manifest.Agents);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("7")]
    public void Run_GivenSeatsOutsideTwoToSix_ExpectedUsageErrorWithSeatMessage(string seats)
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-bad-seats");
        try
        {
            var result = HostCliTestSupport.Run(
                "play", "--seats", seats, "--seed", "42", "--hands", "1",
                "--allow-weak-seed", "--data-dir", dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("--seats", result.Error, StringComparison.Ordinal);
            Assert.Contains("between 2 and 6", result.Error, StringComparison.Ordinal);
            Assert.Contains("observed " + seats, result.Error, StringComparison.Ordinal);
            Assert.Empty(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenAgentsCountMismatch_ExpectedUsageErrorWithCountMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-bad-agent-count");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seats",
                "4",
                "--agents",
                "random-legal,tight-aggressive,pot-odds-equity",
                "--seed",
                "42",
                "--hands",
                "1",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("--agents", result.Error, StringComparison.Ordinal);
            Assert.Contains("exactly 4 agents", result.Error, StringComparison.Ordinal);
            Assert.Contains("observed 3", result.Error, StringComparison.Ordinal);
            Assert.Empty(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenUnknownAgentName_ExpectedUsageErrorWithNameMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-bad-agent-name");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seats",
                "4",
                "--agents",
                "random-legal,tight-aggressive,cheater,random-legal",
                "--seed",
                "42",
                "--hands",
                "1",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("--agents", result.Error, StringComparison.Ordinal);
            Assert.Contains(
                "random-legal, tight-aggressive or pot-odds-equity",
                result.Error,
                StringComparison.Ordinal);
            Assert.Contains("'cheater'", result.Error, StringComparison.Ordinal);
            Assert.Empty(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenStartingStacksCountMismatch_ExpectedUsageErrorWithCountMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-bad-stack-count");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seats",
                "4",
                "--agents",
                FourSeatAgents,
                "--starting-stacks",
                "1000,800,600",
                "--seed",
                "42",
                "--hands",
                "1",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("--starting-stacks", result.Error, StringComparison.Ordinal);
            Assert.Contains("exactly 4 stacks", result.Error, StringComparison.Ordinal);
            Assert.Contains("observed 3", result.Error, StringComparison.Ordinal);
            Assert.Empty(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenStartingStackBelowBigBlind_ExpectedUsageErrorWithValueMessage()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-bad-stack-value");
        try
        {
            var result = HostCliTestSupport.Run(
                "play",
                "--seats",
                "4",
                "--agents",
                FourSeatAgents,
                "--starting-stacks",
                "1000,800,5,400",
                "--seed",
                "42",
                "--hands",
                "1",
                "--allow-weak-seed",
                "--data-dir",
                dataDirectory);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(string.Empty, result.Output);
            Assert.Contains("--starting-stacks", result.Error, StringComparison.Ordinal);
            Assert.Contains("at least 10", result.Error, StringComparison.Ordinal);
            Assert.Contains("observed 5", result.Error, StringComparison.Ordinal);
            Assert.Empty(HostCliTestSupport.SessionDirectories(dataDirectory));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenTwoSeatOracle_ExpectedUnchangedSessionIdAndManifestHash()
    {
        var dataDirectory = HostCliTestSupport.CreateDataDirectory("host-play-oracle");
        try
        {
            var result = HostCliTestSupport.Run(
                "play", "--seed", "42", "--hands", "20", "--allow-weak-seed", "--data-dir", dataDirectory);

            Assert.Equal(0, result.ExitCode);
            var sessionDirectory = Assert.Single(HostCliTestSupport.SessionDirectories(dataDirectory));
            var manifestPath = Path.Combine(sessionDirectory, "session.json");
            Assert.Equal(
                "87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618",
                AgentTestSupport.Sha256Hex(File.ReadAllBytes(manifestPath)));
            Assert.Contains("session: tholdem-06795fbeacfe528b", result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618",
                result.Output,
                StringComparison.Ordinal);
            Assert.Equal(20, Directory.GetFiles(sessionDirectory, "hand-*.json").Length);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
