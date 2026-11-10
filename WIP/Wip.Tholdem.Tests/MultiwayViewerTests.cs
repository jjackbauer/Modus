using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests;

/// <summary>
/// End-to-end multiway coverage over the real Kestrel surface: a 2–6 seat live match, the contracted
/// validation errors, live/simulator hash equality, and N-seat recorded-session detail plus replay
/// table. The two-seat live contract stays covered by <see cref="TholdemWebViewerTests"/>.
/// </summary>
public sealed class MultiwayViewerTests
{
    private static readonly string[] FourSeatAgents =
        ["random-legal", "tight-aggressive", "pot-odds-equity", "random-legal"];

    private static readonly string[] SixSeatAgents =
    [
        "random-legal", "tight-aggressive", "pot-odds-equity",
        "random-legal", "tight-aggressive", "pot-odds-equity",
    ];

    [Fact]
    public async Task LiveMatch_GivenFourSeats_ExpectedFourSeatSnapshotsWithOwnHoleCardsAndLegacyFields()
    {
        await ViewerTestSupport.WithServerAsync("multiway-live-4", async server =>
        {
            var started = await StartLiveAsync(server.Client, 42, 3, 0, FourSeatAgents);
            var id = started.GetProperty("id").GetString()!;
            Assert.Equal(4, started.GetProperty("seats").GetInt32());
            Assert.Equal(
                FourSeatAgents,
                ViewerTestSupport.StringArray(started, "agents"));

            // The configured seat count is part of every snapshot, even before the first state exists.
            var early = await ViewerTestSupport.GetJsonAsync(server.Client, $"/api/live/{id}");
            Assert.Equal(4, early.GetProperty("seats").GetArrayLength());

            var completed = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the four-seat live match to complete");

            Assert.Equal(4, completed.GetProperty("seats").GetArrayLength());
            var seats = completed.GetProperty("seats").EnumerateArray().ToArray();
            for (var index = 0; index < 4; index++)
            {
                var seat = seats[index];
                Assert.Equal(index, seat.GetProperty("seat").GetInt32());
                Assert.Equal(FourSeatAgents[index], seat.GetProperty("agent").GetString());
                Assert.True(seat.GetProperty("stack").GetInt64() >= 0);
                Assert.True(
                    seat.TryGetProperty("folded", out _)
                    && seat.TryGetProperty("allIn", out _)
                    && seat.TryGetProperty("handName", out _)
                    && seat.TryGetProperty("winner", out _)
                    && seat.TryGetProperty("dealer", out _),
                    "Every spectator seat must expose folded/allIn/handName/winner/dealer.");
                var holeCards = ViewerTestSupport.StringArray(seat, "holeCards");
                Assert.Equal(2, holeCards.Length);
                Assert.All(holeCards, card => Assert.Matches("^[2-9TJQKA][cdhs]$", card));
            }

            // The two-seat legacy fields remain present and describe seats 0 and 1 of this table.
            Assert.Equal(4, ViewerTestSupport.LongArray(completed, "stacks").Length);
            Assert.Equal(4000L, ViewerTestSupport.LongArray(completed, "stacks").Sum());
            Assert.Equal(
                ViewerTestSupport.StringArray(seats[0], "holeCards"),
                ViewerTestSupport.StringArray(completed, "holeCardsSeat0"));
            Assert.Equal(
                ViewerTestSupport.StringArray(seats[1], "holeCards"),
                ViewerTestSupport.StringArray(completed, "holeCardsSeat1"));
            Assert.Equal(4, ViewerTestSupport.StringArray(completed, "handNames").Length);
            Assert.InRange(completed.GetProperty("buttonSeat").GetInt32(), 0, 3);
            Assert.Equal(
                1,
                seats.Count(seat => seat.GetProperty("dealer").GetBoolean()));
            Assert.All(
                ViewerTestSupport.IntArray(completed, "winners"),
                winner => Assert.InRange(winner, 0, 3));

            var directory = Path.Combine(server.DataDirectory, "simulator-four-seat");
            MatchSimulator.Run(
                FourAgents(),
                new SimulationOptions(42UL, 3, HistoryDirectory: directory));
            using var lastRecord = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(directory, "hand-00003.json")));
            var recordedHoleCards = lastRecord.RootElement.GetProperty("holeCards").EnumerateArray().ToArray();
            for (var index = 0; index < 4; index++)
            {
                Assert.Equal(
                    StringArray(recordedHoleCards[index]),
                    ViewerTestSupport.StringArray(seats[index], "holeCards"));
            }
        });
    }

    [Fact]
    public async Task LiveMatch_GivenSixSeats_ExpectedSixSeatSnapshotWithEveryOwnHoleCards()
    {
        await ViewerTestSupport.WithServerAsync("multiway-live-6", async server =>
        {
            var started = await StartLiveAsync(server.Client, 7, 2, 0, SixSeatAgents);
            var id = started.GetProperty("id").GetString()!;
            Assert.Equal(6, started.GetProperty("seats").GetInt32());

            var completed = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the six-seat live match to complete");

            var seats = completed.GetProperty("seats").EnumerateArray().ToArray();
            Assert.Equal(6, seats.Length);
            Assert.Equal(6, ViewerTestSupport.LongArray(completed, "stacks").Length);
            Assert.Equal(6000L, ViewerTestSupport.LongArray(completed, "stacks").Sum());
            Assert.Equal(6, ViewerTestSupport.StringArray(completed, "handNames").Length);
            Assert.InRange(completed.GetProperty("buttonSeat").GetInt32(), 0, 5);
            for (var index = 0; index < 6; index++)
            {
                Assert.Equal(index, seats[index].GetProperty("seat").GetInt32());
                Assert.Equal(SixSeatAgents[index], seats[index].GetProperty("agent").GetString());
                var holeCards = ViewerTestSupport.StringArray(seats[index], "holeCards");
                Assert.Equal(2, holeCards.Length);
                Assert.All(holeCards, card => Assert.Matches("^[2-9TJQKA][cdhs]$", card));
            }
        });
    }

    [Fact]
    public async Task LiveMatchCreation_GivenInvalidSeatsAgentsOrStacks_ExpectedContracted400Errors()
    {
        await ViewerTestSupport.WithServerAsync("multiway-live-invalid", async server =>
        {
            var cases = new (Dictionary<string, object?> Body, string Error)[]
            {
                (Body(seats: 1, agents: ["random-legal"]), "invalid-seats"),
                (Body(seats: 7, agents: SixSeatAgents.Append("random-legal").ToArray()), "invalid-seats"),
                (Body(seats: 4, agents: ["random-legal", "tight-aggressive", "pot-odds-equity"]), "invalid-agents"),
                (
                    Body(
                        seats: 4,
                        agents: ["random-legal", "tight-aggressive", "pot-odds-equity"],
                        startingStacks: [1000, 1000, 1000, 1000]),
                    "invalid-agents"),
                (
                    Body(seats: 4, seat0Agent: "random-legal", seat1Agent: "random-legal", agents: null),
                    "invalid-agents"),
                (
                    Body(
                        seats: 4,
                        agents: FourSeatAgents,
                        startingStacks: [1000, 800, 600]),
                    "invalid-starting-stacks"),
                (
                    Body(
                        seats: 4,
                        agents: FourSeatAgents,
                        startingStacks: [1000, 800, 5, 400]),
                    "invalid-starting-stacks"),
                (Body(seats: 4, agents: ["random-legal", "tight-aggressive", "cheater", "random-legal"]), "unknown-agent"),
            };

            foreach (var (body, expectedError) in cases)
            {
                var payload = await ViewerTestSupport.PostJsonAsync(
                    server.Client, "/api/live", body, HttpStatusCode.BadRequest);
                Assert.Equal(expectedError, payload.GetProperty("error").GetString());
            }

            var status = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/status");
            Assert.Equal(0, status.GetProperty("liveMatches").GetInt32());
        });
    }

    [Fact]
    public async Task LiveMatchHashes_GivenFourSeats_ExpectedDeterministicMatchAndSimulatorHashEquality()
    {
        await ViewerTestSupport.WithServerAsync("multiway-live-hashes", async server =>
        {
            var historyDirectory = Path.Combine(server.DataDirectory, "simulator-history");
            MatchSimulator.Run(
                FourAgents(),
                new SimulationOptions(42UL, 3, HistoryDirectory: historyDirectory));

            var first = await RunLiveAsync(server.Client, 42, 3, FourSeatAgents);
            var second = await RunLiveAsync(server.Client, 42, 3, FourSeatAgents);

            var firstHashes = ViewerTestSupport.StringArray(first, "handHashes");
            var secondHashes = ViewerTestSupport.StringArray(second, "handHashes");
            Assert.Equal(3, firstHashes.Length);
            Assert.Equal(firstHashes, secondHashes);
            for (var hand = 1; hand <= 3; hand++)
            {
                var historyFile = Path.Combine(historyDirectory, $"hand-{hand:D5}.json");
                Assert.True(File.Exists(historyFile), $"Missing simulator history file {historyFile}.");
                Assert.Equal(
                    AgentTestSupport.Sha256Hex(File.ReadAllBytes(historyFile)),
                    firstHashes[hand - 1]);
            }
        });
    }

    [Fact]
    public async Task RecordedSession_GivenFourSeatHistory_ExpectedNSeatDetailPotLayersAndReplayTable()
    {
        await ViewerTestSupport.WithServerAsync("multiway-replay-4", async server =>
        {
            var report = MatchSimulator.Run(
                FourAgents(),
                new SimulationOptions(
                    4242UL,
                    3,
                    StartingStacks: [1000, 800, 600, 400]));
            var sessionDirectory = Path.Combine(server.DataDirectory, "sessions", report.SessionId);
            MatchSimulator.Run(
                FourAgents(),
                new SimulationOptions(
                    4242UL,
                    3,
                    StartingStacks: [1000, 800, 600, 400],
                    HistoryDirectory: sessionDirectory));

            var list = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/sessions");
            var summary = Assert.Single(list.GetProperty("sessions").EnumerateArray());
            Assert.Equal(4, summary.GetProperty("seats").GetInt32());
            Assert.Equal(FourSeatAgents, ViewerTestSupport.StringArray(summary, "agents"));
            Assert.Equal(
                new long[] { 1000L, 800L, 600L, 400L },
                ViewerTestSupport.LongArray(summary, "startingStacks"));

            var detail = await ViewerTestSupport.GetJsonAsync(
                server.Client,
                "/api/sessions/" + Uri.EscapeDataString(report.SessionId));
            Assert.Equal(4, detail.GetProperty("seats").GetInt32());
            Assert.Equal(FourSeatAgents, ViewerTestSupport.StringArray(detail, "agents"));
            Assert.Equal(
                new long[] { 1000L, 800L, 600L, 400L },
                ViewerTestSupport.LongArray(detail, "startingStacks"));

            var summaries = detail.GetProperty("hands").EnumerateArray().ToArray();
            Assert.Equal(3, summaries.Length);
            using var firstRecord = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(sessionDirectory, "hand-00001.json")));
            var firstRoot = firstRecord.RootElement;
            var recordedHoleCards = firstRoot.GetProperty("holeCards").EnumerateArray().ToArray();
            var recordedPots = firstRoot.GetProperty("pots").EnumerateArray().ToArray();

            foreach (var hand in summaries)
            {
                Assert.Equal(4, hand.GetProperty("holeCards").GetArrayLength());
                Assert.Equal(4, hand.GetProperty("finalStacks").GetArrayLength());
                Assert.Equal(4, hand.GetProperty("handNames").GetArrayLength());
                var pots = hand.GetProperty("pots").EnumerateArray().ToArray();
                Assert.NotEmpty(pots);
                Assert.Equal(
                    hand.GetProperty("potAwarded").GetInt64(),
                    pots.Sum(pot => pot.GetProperty("amount").GetInt64()));
            }

            var firstSummary = summaries.Single(
                hand => hand.GetProperty("handNumber").GetInt32() == 1);
            Assert.Equal(4, firstSummary.GetProperty("holeCards").GetArrayLength());
            Assert.Equal(
                recordedPots.Select(pot => pot.GetProperty("amount").GetInt64()).ToArray(),
                firstSummary.GetProperty("pots").EnumerateArray()
                    .Select(pot => pot.GetProperty("amount").GetInt64()).ToArray());
            var summaryHoleCards = firstSummary.GetProperty("holeCards").EnumerateArray().ToArray();
            for (var seat = 0; seat < 4; seat++)
            {
                Assert.Equal(StringArray(recordedHoleCards[seat]), StringArray(summaryHoleCards[seat]));
            }

            var html = await GetTextAsync(
                server.Client,
                $"/viewer/table?session={Uri.EscapeDataString(report.SessionId)}&hand=1");
            Assert.Equal(4, Regex.Matches(html, "<section class=\"replay-seat").Count);
            for (var seat = 0; seat < 4; seat++)
            {
                Assert.Contains($"data-seat=\"{seat}\"", html);
                Assert.Contains($"data-seat-agent=\"{FourSeatAgents[seat]}\"", html);
                Assert.Contains($"data-hand-name=\"{firstSummary.GetProperty("handNames")[seat].GetString()}\"", html);
                foreach (var card in StringArray(recordedHoleCards[seat]))
                {
                    Assert.Contains($"data-card=\"{card}\"", html);
                }
            }

            Assert.Contains("data-settlement=\"true\"", html);
            Assert.Contains(
                $"Pot awarded: {firstRoot.GetProperty("potAwarded").GetInt64()}",
                html);
            foreach (var winner in ViewerTestSupport.IntArray(firstSummary, "winners"))
            {
                Assert.Contains($"Seat {winner}", html);
            }
        });
    }

    [Fact]
    public async Task ViewerAssets_GivenMultiwayTable_ExpectedSeatSelectorSnapshotSeatsAndNoExternalUrls()
    {
        var viewerDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Web", "wwwroot", "viewer");
        var indexHtml = await File.ReadAllTextAsync(Path.Combine(viewerDirectory, "index.html"));
        var seatsSelector = Regex.Match(indexHtml, "<select id=\"seats\"[^>]*>(?<body>.*?)</select>", RegexOptions.Singleline);
        Assert.True(seatsSelector.Success, "The live form must keep its #seats selector.");
        foreach (var seats in new[] { "2", "3", "4", "5", "6" })
        {
            Assert.Contains($"value=\"{seats}\"", seatsSelector.Groups["body"].Value, StringComparison.Ordinal);
        }

        var script = await File.ReadAllTextAsync(Path.Combine(viewerDirectory, "app.js"));
        Assert.Contains("snapshot.seats", script, StringComparison.Ordinal);
        Assert.Contains("holeCardsSeat0", script, StringComparison.Ordinal);
        Assert.Contains("holeCardsSeat1", script, StringComparison.Ordinal);
        Assert.Contains("function liveSeats(snapshot)", script, StringComparison.Ordinal);
        Assert.Contains("Array.isArray(snapshot.seats)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);

        foreach (var asset in new[] { "app.js", "viewer.css", "index.html" })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(viewerDirectory, asset));
            foreach (var token in new[] { "http://", "https://", "//cdn" })
            {
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IPokerAgent[] FourAgents() =>
        [new RandomLegalAgent(), new TightAggressiveAgent(), new PotOddsEquityAgent(), new RandomLegalAgent()];

    private static string[] StringArray(JsonElement array) =>
        array.EnumerateArray().Select(static item => item.GetString()!).ToArray();

    private static Dictionary<string, object?> Body(
        int? seats = 2,
        string[]? agents = null,
        long[]? startingStacks = null,
        string? seat0Agent = null,
        string? seat1Agent = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["seed"] = 42UL,
            ["hands"] = 1,
            ["delayMs"] = 0,
            ["allowWeakSeed"] = true,
        };
        if (seats is not null)
        {
            body["seats"] = seats.Value;
        }

        if (agents is not null)
        {
            body["agents"] = agents;
        }

        if (startingStacks is not null)
        {
            body["startingStacks"] = startingStacks;
        }

        if (seat0Agent is not null)
        {
            body["seat0Agent"] = seat0Agent;
        }

        if (seat1Agent is not null)
        {
            body["seat1Agent"] = seat1Agent;
        }

        return body;
    }

    private static async Task<JsonElement> StartLiveAsync(
        HttpClient client,
        ulong seed,
        int hands,
        int delayMs,
        string[] agents,
        long[]? startingStacks = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["seed"] = seed,
            ["hands"] = hands,
            ["delayMs"] = delayMs,
            ["seats"] = agents.Length,
            ["agents"] = agents,
            ["allowWeakSeed"] = true,
        };
        if (startingStacks is not null)
        {
            body["startingStacks"] = startingStacks;
        }

        return await ViewerTestSupport.PostJsonAsync(
            client, "/api/live", body, HttpStatusCode.Created);
    }

    private static async Task<JsonElement> RunLiveAsync(
        HttpClient client,
        ulong seed,
        int hands,
        string[] agents)
    {
        var started = await StartLiveAsync(client, seed, hands, 0, agents);
        var id = started.GetProperty("id").GetString()!;
        return await ViewerTestSupport.WaitForSnapshotAsync(
            client,
            id,
            root => root.GetProperty("status").GetString() == "complete",
            $"the {agents.Length}-seat seed {seed} live match to complete");
    }

    private static async Task<string> GetTextAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
