using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Round-two multiway simulator contract: the N-agent overload, per-seat starting stacks,
/// byte-identical sessions, zero-sum chips, per-seat metrics and the seat-aware manifest. Every
/// agent is a real agent implementation; the simulator itself is never stubbed.
/// </summary>
public sealed class MultiwaySimulationTests
{
    private const ulong OracleSeed = 42UL;

    private const string OracleSessionId = "tholdem-06795fbeacfe528b";

    private const string OracleManifestHash =
        "87f90413e929d99fff9c833f9768cda3da22e4939e2d3224438d8d477538e618";

    private const string OracleHand1Hash =
        "709c0816b59b18c7514198901d51ac0e0af050f02b27795a4c27521908324fc5";

    private const string OracleHand20Hash =
        "bef6917b919453b13f041dc8a56e2cfc7d91bc9c26a48fe4798c630a706a411f";

    private static IPokerAgent[] RandomAgents(int seats, string prefix)
        => Enumerable.Range(0, seats)
            .Select(seat => (IPokerAgent)new RandomLegalAgent($"{prefix}-seat-{seat}"))
            .ToArray();

    [Fact]
    public void Run_GivenTwoAgentsSeed42_ExpectedUnchangedSessionIdManifestHashAndHandBytes()
    {
        var directory = AgentTestSupport.CreateTempDirectory("round2-two-seat-oracle");
        try
        {
            var report = MatchSimulator.Run(
                new TightAggressiveAgent(),
                new RandomLegalAgent(),
                new SimulationOptions(OracleSeed, 20, HistoryDirectory: directory));

            Assert.Equal(OracleSessionId, report.SessionId);
            Assert.Equal(OracleManifestHash, report.ManifestHash);
            Assert.Equal(2, report.Agents.Count);
            Assert.Equal(new[] { "tight-aggressive", "random-legal" }, report.Agents.Select(agent => agent.AgentName));

            var manifestBytes = File.ReadAllBytes(Path.Combine(directory, "session.json"));
            Assert.Equal(OracleManifestHash, AgentTestSupport.Sha256Hex(manifestBytes));
            Assert.Equal(OracleHand1Hash, FileHash(directory, "hand-00001.json"));
            Assert.Equal(OracleHand20Hash, FileHash(directory, "hand-00020.json"));

            var manifestText = Encoding.UTF8.GetString(manifestBytes);
            Assert.DoesNotContain("\"seats\"", manifestText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"startingStacks\"", manifestText, StringComparison.Ordinal);

            // A pre-multiway manifest has no "seats" member; it still parses as a two-seat session.
            var manifest = SessionManifest.Parse(manifestText);
            Assert.Equal(2, manifest.Seats);
            Assert.Equal(new long[] { 1000, 1000 }, manifest.StartingStacks.ToArray());
            Assert.Equal(new[] { "tight-aggressive", "random-legal" }, manifest.Agents.ToArray());
            Assert.Equal(20, manifest.HandHashes.Count);
            Assert.Equal(OracleHand1Hash, manifest.HandHashes[0]);
            Assert.Equal(OracleHand20Hash, manifest.HandHashes[19]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Run_GivenMultiwayAgentsAndSameSeed_ExpectedByteIdenticalFilesAndManifestHash(int seats)
    {
        var first = AgentTestSupport.CreateTempDirectory($"round2-determinism-{seats}-first");
        var second = AgentTestSupport.CreateTempDirectory($"round2-determinism-{seats}-second");
        var different = AgentTestSupport.CreateTempDirectory($"round2-determinism-{seats}-different");
        try
        {
            var agents = RandomAgents(seats, "determinism");
            var firstReport = MatchSimulator.Run(
                agents,
                new SimulationOptions(424242UL, 12, HistoryDirectory: first));
            var secondReport = MatchSimulator.Run(
                agents,
                new SimulationOptions(424242UL, 12, HistoryDirectory: second));
            var differentReport = MatchSimulator.Run(
                agents,
                new SimulationOptions(424243UL, 12, HistoryDirectory: different));

            var firstFiles = SessionFiles(first);
            Assert.Equal(13, firstFiles.Length);
            Assert.Equal(firstFiles, SessionFiles(second));
            Assert.Equal(firstFiles, SessionFiles(different));
            Assert.Equal(firstReport.SessionId, secondReport.SessionId);
            Assert.Equal(firstReport.ManifestHash, secondReport.ManifestHash);
            Assert.Equal(firstReport.CanonicalJson, secondReport.CanonicalJson);
            Assert.NotEqual(firstReport.SessionId, differentReport.SessionId);
            Assert.NotEqual(firstReport.ManifestHash, differentReport.ManifestHash);

            foreach (var name in firstFiles)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(first, name)),
                    File.ReadAllBytes(Path.Combine(second, name)));
            }

            Assert.Equal(
                firstReport.ManifestHash,
                AgentTestSupport.Sha256Hex(File.ReadAllBytes(Path.Combine(first, "session.json"))));

            var firstHand = ReadRecord(first, 1);
            var differentHand = ReadRecord(different, 1);
            Assert.Equal(seats, firstHand.SeatCount);
            Assert.Equal(seats, firstReport.Agents.Count);
            Assert.NotEqual(firstHand.Hash, differentHand.Hash);

            // Hand n's button seat is (n - 1) % seats; every recorded hand must agree.
            for (var handNumber = 1; handNumber <= 12; handNumber++)
            {
                Assert.Equal((handNumber - 1) % seats, ReadRecord(first, handNumber).ButtonSeat);
            }
        }
        finally
        {
            Delete(first, second, different);
        }
    }

    [Fact]
    public void Run_GivenSixSeatsOneHundredHandsWithPerSeatStacks_ExpectedZeroSumPerSeatAndPerHandStackTotals()
    {
        var directory = AgentTestSupport.CreateTempDirectory("round2-zero-sum-six-seats");
        try
        {
            var startingStacks = new long[] { 1000, 900, 800, 700, 600, 500 };
            var agents = RandomAgents(6, "zero-sum");
            var report = MatchSimulator.Run(
                agents,
                new SimulationOptions(
                    24680UL,
                    100,
                    StartingStacks: startingStacks,
                    HistoryDirectory: directory));

            Assert.Equal(6, report.Agents.Count);
            Assert.Equal(100, report.Agents[0].Hands);
            Assert.Equal(0L, report.Agents.Sum(metrics => metrics.NetChips));

            var records = AgentTestSupport.HandFileNames(directory)
                .Select(name => HandRecordJson.Parse(File.ReadAllText(Path.Combine(directory, name))))
                .ToArray();
            Assert.Equal(100, records.Length);

            foreach (var record in records)
            {
                Assert.Equal(6, record.SeatCount);
                Assert.Equal(startingStacks.ToArray(), record.StartingStacks.ToArray());
                Assert.Equal(startingStacks.Sum(), record.FinalStacks.Sum());
                Assert.Equal(record.StartingStacks.Sum(), record.FinalStacks.Sum());
            }

            for (var seat = 0; seat < 6; seat++)
            {
                var expectedNet = records.Sum(record => record.FinalStacks[seat] - record.StartingStacks[seat]);
                Assert.Equal(expectedNet, report.Agents[seat].NetChips);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenControlledThreeSeatTable_ExpectedLiteralWinsShowdownsAndAggression()
    {
        var directory = AgentTestSupport.CreateTempDirectory("round2-controlled-metrics");
        try
        {
            var agents = new IPokerAgent[]
            {
                new AllInAgent(),
                new CallAgent(),
                new FoldAgent(),
            };

            var report = MatchSimulator.Run(
                agents,
                new SimulationOptions(31415UL, 12, HistoryDirectory: directory));

            Assert.Equal(12, report.Agents[0].Hands);
            Assert.Equal(12, report.Agents[1].Hands);
            Assert.Equal(12, report.Agents[2].Hands);

            // Every seat 0 all-in is called by seat 1; seat 2 folds before the flop of every hand, so
            // exactly seats 0 and 1 reach every showdown and only they can win a hand.
            Assert.Equal(12.0, report.Agents[0].AggressionFactor);
            Assert.Equal(0.0, report.Agents[1].AggressionFactor);
            Assert.Equal(0.0, report.Agents[2].AggressionFactor);
            Assert.Equal(1.0, report.Agents[0].ShowdownRate);
            Assert.Equal(1.0, report.Agents[1].ShowdownRate);
            Assert.Equal(0.0, report.Agents[2].ShowdownRate);

            var records = AgentTestSupport.HandFileNames(directory)
                .Select(name => HandRecordJson.Parse(File.ReadAllText(Path.Combine(directory, name))))
                .ToArray();
            var tiedHands = records.Count(record => record.Winners.Count > 1);
            Assert.Equal(1, tiedHands);
            Assert.Equal(7, report.Agents[0].Wins);
            Assert.Equal(6, report.Agents[1].Wins);
            Assert.Equal(0, report.Agents[2].Wins);
            Assert.Equal(records.Length + tiedHands, report.Agents[0].Wins + report.Agents[1].Wins);
            Assert.Equal(0L, report.Agents.Sum(metrics => metrics.NetChips));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenIllegalActionAtFourSeats_ExpectedExceptionNamingAgentAndHandWithoutFallback()
    {
        var directory = AgentTestSupport.CreateTempDirectory("round2-four-seat-illegal-action");
        try
        {
            var exception = Assert.Throws<IllegalAgentActionException>(() => MatchSimulator.Run(
                new IPokerAgent[]
                {
                    new RandomLegalAgent("four-seat-legal-0"),
                    new RandomLegalAgent("four-seat-legal-1"),
                    new IllegalRaiseAgent(),
                    new RandomLegalAgent("four-seat-legal-3"),
                },
                new SimulationOptions(31UL, 5, HistoryDirectory: directory)));

            Assert.Equal("four-seat-illegal-agent", exception.AgentName);
            Assert.Equal(1, exception.HandNumber);
            Assert.Equal(ActionKind.Raise, exception.Action.Kind);
            Assert.Equal(1L, exception.Action.Amount);
            Assert.Contains("four-seat-illegal-agent", exception.Message, StringComparison.Ordinal);
            Assert.Contains("hand 1", exception.Message, StringComparison.Ordinal);
            Assert.IsType<IllegalActionException>(exception.InnerException);
            Assert.Empty(Directory.GetFiles(directory));
            Assert.False(File.Exists(Path.Combine(directory, "session.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenFourSeatsAndPerSeatStacks_ExpectedV2FilesSeatAwareManifestAndReplay()
    {
        var directory = AgentTestSupport.CreateTempDirectory("round2-four-seat-session");
        try
        {
            var startingStacks = new long[] { 1000, 800, 600, 400 };
            var agents = RandomAgents(4, "session");
            var report = MatchSimulator.Run(
                agents,
                new SimulationOptions(
                    4242UL,
                    5,
                    StartingStacks: startingStacks,
                    HistoryDirectory: directory));

            Assert.Equal(6, SessionFiles(directory).Length);

            var manifestBytes = File.ReadAllBytes(Path.Combine(directory, "session.json"));
            var manifestText = Encoding.UTF8.GetString(manifestBytes);
            Assert.Contains("\"seats\":4", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"startingStacks\":[1000,800,600,400]", manifestText, StringComparison.Ordinal);
            Assert.Equal(report.ManifestHash, AgentTestSupport.Sha256Hex(manifestBytes));

            var manifest = SessionManifest.Parse(manifestText);
            Assert.Equal(4, manifest.Seats);
            Assert.Equal(5, manifest.Hands);
            Assert.Equal(startingStacks.ToArray(), manifest.StartingStacks.ToArray());
            var names = new[] { "session-seat-0", "session-seat-1", "session-seat-2", "session-seat-3" };
            Assert.Equal(names, manifest.Agents.ToArray());

            // The session id is derived over every seat name, not only the first two.
            var material = "4242\n5\n" + string.Join("\n", names);
            var expectedSessionId = "tholdem"
                + "-"
                + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
                    .ToLowerInvariant()[..16];
            Assert.Equal(expectedSessionId, report.SessionId);
            Assert.Equal(expectedSessionId, manifest.SessionId);

            var records = new HandRecord[5];
            for (var handNumber = 1; handNumber <= 5; handNumber++)
            {
                var path = Path.Combine(directory, $"hand-{handNumber:D5}.json");
                var bytes = File.ReadAllBytes(path);
                var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
                var record = HandRecordJson.Parse(text);
                records[handNumber - 1] = record;

                Assert.StartsWith("{\"schema\":\"tholdem.hand.v2\"", text, StringComparison.Ordinal);
                Assert.Equal(handNumber, record.HandNumber);
                Assert.Equal(4, record.SeatCount);
                Assert.Equal(startingStacks.ToArray(), record.StartingStacks.ToArray());
                Assert.Equal(text, record.ToCanonicalJson());
                Assert.Equal(record.Hash, manifest.HandHashes[handNumber - 1]);
                Assert.Equal(AgentTestSupport.Sha256Hex(bytes), manifest.HandHashes[handNumber - 1]);

                var replayed = HoldemHandEngine.Replay(record);
                Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
                Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
                Assert.Equal(startingStacks.Sum(), replayed.Stacks.Sum() + replayed.Pot);
            }

            for (var seat = 0; seat < 4; seat++)
            {
                var expectedNet = records.Sum(record => record.FinalStacks[seat] - record.StartingStacks[seat]);
                Assert.Equal(expectedNet, report.Agents[seat].NetChips);
                Assert.Equal(5, report.Agents[seat].Hands);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_GivenSixSeats_ExpectedEverySeatObservationHidesSeedAndForeignHoleCards()
    {
        var directory = AgentTestSupport.CreateTempDirectory("round2-six-seat-hidden-information");
        try
        {
            const int seats = 6;
            var sessionSeed = 6_000_006UL;
            var probes = Enumerable.Range(0, seats)
                .Select(seat => new ObservationProbeAgent($"observe-seat-{seat}"))
                .ToArray();

            MatchSimulator.Run(
                probes.Select(probe => (IPokerAgent)probe).ToArray(),
                new SimulationOptions(sessionSeed, 1, HistoryDirectory: directory));

            var record = ReadRecord(directory, 1);
            var handSeed = SeedDerivation.Derive(sessionSeed, 1, 0, 0, 0UL);
            Assert.Equal(handSeed, record.Seed);
            Assert.Equal(seats, record.SeatCount);

            var deck = SeededDeck.Create(handSeed);
            for (var seat = 0; seat < seats; seat++)
            {
                var observations = probes[seat].Observations;
                Assert.NotEmpty(observations);
                var allowed = new HashSet<string>(
                    record.HoleCards[seat].Select(card => card.ToString()),
                    StringComparer.Ordinal);
                foreach (var card in record.Board)
                {
                    allowed.Add(card.ToString());
                }

                foreach (var observation in observations)
                {
                    Assert.Equal(seat, observation.Seat);
                    Assert.Equal(seats, observation.SeatCount);
                    Assert.Equal(
                        record.HoleCards[seat].Select(card => card.ToString()).ToArray(),
                        observation.OwnHoleCards.Select(card => card.ToString()).ToArray());

                    var json = observation.ToCanonicalJson();
                    Assert.DoesNotContain(handSeed.ToString(CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
                    Assert.DoesNotContain(sessionSeed.ToString(CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
                    Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("\"deck\"", json, StringComparison.OrdinalIgnoreCase);

                    for (var other = 0; other < seats; other++)
                    {
                        if (other == seat)
                        {
                            continue;
                        }

                        foreach (var card in record.HoleCards[other])
                        {
                            Assert.DoesNotContain("\"" + card + "\"", json, StringComparison.Ordinal);
                        }
                    }

                    foreach (var card in deck)
                    {
                        if (!allowed.Contains(card.ToString()))
                        {
                            Assert.DoesNotContain("\"" + card + "\"", json, StringComparison.Ordinal);
                        }
                    }

                    foreach (var card in observation.OwnHoleCards)
                    {
                        Assert.Contains("\"" + card + "\"", json, StringComparison.Ordinal);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] SessionFiles(string directory)
        => Directory.GetFiles(directory)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static HandRecord ReadRecord(string directory, int handNumber)
        => HandRecordJson.Parse(File.ReadAllText(Path.Combine(directory, $"hand-{handNumber:D5}.json")));

    private static string FileHash(string directory, string name)
        => AgentTestSupport.Sha256Hex(File.ReadAllBytes(Path.Combine(directory, name)));

    private static void Delete(params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Goes all-in whenever possible; only used by the controlled metrics fact.</summary>
    private sealed class AllInAgent : IPokerAgent
    {
        public string Name => "all-in";

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.LegalActionKinds.Contains(ActionKind.AllIn))
            {
                return ValueTask.FromResult(PokerAction.AllIn());
            }

            return ValueTask.FromResult(
                observation.LegalActionKinds.Contains(ActionKind.Check)
                    ? PokerAction.Check()
                    : PokerAction.Call());
        }
    }

    /// <summary>Calls every bet and checks otherwise; only used by the controlled metrics fact.</summary>
    private sealed class CallAgent : IPokerAgent
    {
        public string Name => "always-call";

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.LegalActionKinds.Contains(ActionKind.Call))
            {
                return ValueTask.FromResult(PokerAction.Call());
            }

            return ValueTask.FromResult(
                observation.LegalActionKinds.Contains(ActionKind.Check)
                    ? PokerAction.Check()
                    : PokerAction.Fold());
        }
    }

    /// <summary>Folds whenever folding is legal and checks otherwise; only used by the metrics fact.</summary>
    private sealed class FoldAgent : IPokerAgent
    {
        public string Name => "always-fold";

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                observation.LegalActionKinds.Contains(ActionKind.Fold)
                    ? PokerAction.Fold()
                    : PokerAction.Check());
        }
    }

    private sealed class IllegalRaiseAgent : IPokerAgent
    {
        public string Name => "four-seat-illegal-agent";

        public ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(PokerAction.RaiseTo(1));
    }

    /// <summary>Delegates to a random legal agent while retaining every observation it receives.</summary>
    private sealed class ObservationProbeAgent : IPokerAgent
    {
        private readonly RandomLegalAgent _inner;
        private readonly List<PlayerObservation> _observations = [];

        public ObservationProbeAgent(string name) => _inner = new RandomLegalAgent(name);

        public string Name => _inner.Name;

        public IReadOnlyList<PlayerObservation> Observations => _observations;

        public async ValueTask<PokerAction> ActAsync(
            PlayerObservation observation,
            AgentTurnContext context,
            CancellationToken cancellationToken)
        {
            _observations.Add(observation);
            return await _inner.ActAsync(observation, context, cancellationToken);
        }
    }
}
