using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Xunit;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Drives the real Kestrel HTTP surface of <c>Wip.Tholdem.Web</c>: static viewer, live seeded match
/// feed, recorded-session replay, strategy/ledger views, offline assets and the spectator boundary.
/// </summary>
public sealed class TholdemWebViewerTests
{
    private static readonly string[] StableViewerIds =
    [
        "status",
        "live-form",
        "seed",
        "hands",
        "delay",
        "seat0-agent",
        "seat1-agent",
        "start-live",
        "pause-live",
        "resume-live",
        "stop-live",
        "live-table",
        "board",
        "pot",
        "action-log",
        "sessions",
        "replay-panel",
        "replay-hand",
        "replay-scrubber",
        "strategies",
        "strategy-detail",
        "ledger",
    ];

    [Fact]
    public async Task Viewer_GivenStaticHost_ExpectedRedirectShellStableIdsAndAssets()
    {
        await ViewerTestSupport.WithServerAsync("static", async server =>
        {
            using (var noRedirect = server.CreateClient(allowAutoRedirect: false))
            using (var response = await noRedirect.GetAsync("/"))
            {
                Assert.Equal(HttpStatusCode.Found, response.StatusCode);
                Assert.Equal("/viewer/index.html", response.Headers.Location?.OriginalString);
            }

            using var page = await server.Client.GetAsync("/viewer/index.html");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
            var html = await page.Content.ReadAsStringAsync();
            Assert.All(StableViewerIds, id => Assert.Contains($"id=\"{id}\"", html));
            Assert.Contains("\"app.js\"", html);
            Assert.Contains("\"viewer.css\"", html);
        });
    }

    [Fact]
    public async Task LiveMatch_GivenSeed42SixHands_ExpectedSpectatorCompletionHashesAndBoardGrowth()
    {
        await ViewerTestSupport.WithServerAsync("live", async server =>
        {
            // Contracted delay-0 match: creation response, completion, spectator snapshot, hashes.
            var started = await ViewerTestSupport.StartLiveAsync(
                server.Client, 42, 6, 0, "random-legal", "tight-aggressive");
            var id = started.GetProperty("id").GetString()!;
            Assert.Matches("^[0-9a-f]{16}$", id);
            Assert.Equal(42UL, started.GetProperty("seed").GetUInt64());
            Assert.Equal(6, started.GetProperty("hands").GetInt32());
            Assert.Equal(0, started.GetProperty("delayMs").GetInt32());
            Assert.Equal("random-legal", started.GetProperty("seat0Agent").GetString());
            Assert.Equal("tight-aggressive", started.GetProperty("seat1Agent").GetString());

            var snapshot = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the delay-0 live match to complete");

            Assert.True(snapshot.GetProperty("isComplete").GetBoolean());
            Assert.Equal(6, snapshot.GetProperty("handNumber").GetInt32());
            Assert.Equal(2, snapshot.GetProperty("stacks").GetArrayLength());
            Assert.Equal(2, snapshot.GetProperty("holeCardsSeat0").GetArrayLength());
            Assert.Equal(2, snapshot.GetProperty("holeCardsSeat1").GetArrayLength());
            foreach (var card in ViewerTestSupport.StringArray(snapshot, "holeCardsSeat0")
                         .Concat(ViewerTestSupport.StringArray(snapshot, "holeCardsSeat1")))
            {
                Assert.Matches("^[2-9TJQKA][cdhs]$", card);
            }

            Assert.Equal(6, snapshot.GetProperty("handHashes").GetArrayLength());
            foreach (var hash in ViewerTestSupport.StringArray(snapshot, "handHashes"))
            {
                Assert.Matches("^[0-9a-f]{64}$", hash);
            }

            var actions = snapshot.GetProperty("actions");
            Assert.True(actions.GetArrayLength() > 0, "The spectator action log must not be empty.");
            var firstAction = actions[0];
            Assert.All(
                new[] { "handNumber", "street", "seat", "kind", "amount", "potAfter" },
                property => Assert.True(firstAction.TryGetProperty(property, out _), $"Missing action field {property}."));
            Assert.Contains(firstAction.GetProperty("street").GetString(), new[] { "preflop", "flop", "turn", "river" });
            Assert.Contains(firstAction.GetProperty("kind").GetString(), new[] { "Fold", "Check", "Call", "Bet", "Raise", "AllIn" });

            // Paced match (delay only paces progress): observe the board grow 0 -> 3 -> 4 -> 5 for one hand.
            // The paced fixture uses seed 1: with the current observation-derived agent randomness, a
            // six-hand random-legal vs tight-aggressive match at that seed contains a hand played on
            // all four streets, so every intermediate board is observable.
            var paced = await ViewerTestSupport.StartLiveAsync(
                server.Client, 1, 6, 40, "random-legal", "tight-aggressive");
            var pacedId = paced.GetProperty("id").GetString()!;
            var observations = new List<(int Hand, int Board)>();
            var actionCounts = new List<int>();
            await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                pacedId,
                root =>
                {
                    var hand = root.GetProperty("handNumber").GetInt32();
                    if (hand > 0)
                    {
                        observations.Add((hand, root.GetProperty("board").GetArrayLength()));
                    }

                    actionCounts.Add(root.GetProperty("actions").GetArrayLength());
                    return root.GetProperty("status").GetString() == "complete";
                },
                "the paced live match to complete");

            Assert.All(observations, observation => Assert.True(observation.Board is 0 or 3 or 4 or 5));
            Assert.True(
                actionCounts.Distinct().Count() >= 3,
                "The action log must grow while the match is running; observed counts: "
                + string.Join(",", actionCounts.Distinct()));
            for (var index = 1; index < actionCounts.Count; index++)
            {
                Assert.True(actionCounts[index] >= actionCounts[index - 1], "The action log must never shrink.");
            }

            var growthByHand = observations
                .GroupBy(observation => observation.Hand)
                .ToDictionary(group => group.Key, group => group.Select(observation => observation.Board).ToArray());
            Assert.Contains(
                growthByHand.Values,
                sequence => sequence.Contains(0) && sequence.Contains(3) && sequence.Contains(4) && sequence.Contains(5));
            foreach (var sequence in growthByHand.Values)
            {
                for (var index = 1; index < sequence.Length; index++)
                {
                    Assert.True(sequence[index] >= sequence[index - 1], "The board must never shrink within a hand.");
                }
            }
        });
    }

    [Fact]
    public async Task LiveMatchHashes_GivenSameSeedAgentsAndHands_ExpectedMatchSimulatorHistoryBytes()
    {
        await ViewerTestSupport.WithServerAsync("live-hash", async server =>
        {
            var historyDirectory = Path.Combine(server.DataDirectory, "simulator-history");
            MatchSimulator.Run(
                new RandomLegalAgent(),
                new TightAggressiveAgent(),
                new SimulationOptions(42UL, 6, HistoryDirectory: historyDirectory));

            var started = await ViewerTestSupport.StartLiveAsync(
                server.Client, 42, 6, 0, "random-legal", "tight-aggressive");
            var id = started.GetProperty("id").GetString()!;
            var snapshot = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the live cross-check match to complete");

            var liveHashes = ViewerTestSupport.StringArray(snapshot, "handHashes");
            Assert.Equal(6, liveHashes.Length);
            Assert.Equal(7, Directory.GetFiles(historyDirectory).Length);

            for (var hand = 1; hand <= 6; hand++)
            {
                var historyFile = Path.Combine(historyDirectory, $"hand-{hand:D5}.json");
                Assert.True(File.Exists(historyFile), $"Missing simulator history file {historyFile}.");
                var simulatorHash = AgentTestSupport.Sha256Hex(File.ReadAllBytes(historyFile));
                Assert.Equal(simulatorHash, liveHashes[hand - 1]);
            }
        });
    }

    [Fact]
    public async Task LiveMatchHashes_GivenSameInputs_ExpectedIdenticalAcrossRunsAndDifferentSeedDiverges()
    {
        await ViewerTestSupport.WithServerAsync("live-determinism", async server =>
        {
            var first = await RunLiveAsync(server.Client, 42, 6, "random-legal", "tight-aggressive");
            var second = await RunLiveAsync(server.Client, 42, 6, "random-legal", "tight-aggressive");
            var differentSeed = await RunLiveAsync(server.Client, 43, 6, "random-legal", "tight-aggressive");

            Assert.Equal(6, first.Hashes.Length);
            Assert.Equal(first.Hashes, second.Hashes);
            Assert.Equal(first.FinalHoleCards, second.FinalHoleCards);
            Assert.Equal(6, differentSeed.Hashes.Length);
            Assert.NotEqual(first.Hashes, differentSeed.Hashes);
            // The hand-seed derivation must actually deal different cards for a different session seed;
            // a constant deal would leave this fingerprint unchanged even though hashes embed the seed.
            Assert.NotEqual(first.FinalHoleCards, differentSeed.FinalHoleCards);
        });
    }

    [Fact]
    public async Task LiveMatchControls_GivenDeletePauseResumeAndInvalidRequests_ExpectedDocumentedStatuses()
    {
        await ViewerTestSupport.WithServerAsync("controls", async server =>
        {
            // DELETE stops a match and the snapshot freezes.
            var deleted = await ViewerTestSupport.StartLiveAsync(
                server.Client, 7, 12, 20, "random-legal", "tight-aggressive");
            var deleteId = deleted.GetProperty("id").GetString()!;
            await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                deleteId,
                root => root.GetProperty("handNumber").GetInt32() >= 1
                    && root.GetProperty("actions").GetArrayLength() >= 1,
                "the deleted match to start playing");

            using (var response = await server.Client.DeleteAsync($"/api/live/{deleteId}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            await Task.Delay(150);
            var stoppedFirst = await ViewerTestSupport.GetJsonAsync(server.Client, $"/api/live/{deleteId}");
            await Task.Delay(150);
            var stoppedSecond = await ViewerTestSupport.GetJsonAsync(server.Client, $"/api/live/{deleteId}");
            Assert.Equal("stopped", stoppedFirst.GetProperty("status").GetString());
            Assert.Equal("stopped", stoppedSecond.GetProperty("status").GetString());
            Assert.Equal(
                stoppedFirst.GetProperty("handNumber").GetInt32(),
                stoppedSecond.GetProperty("handNumber").GetInt32());
            Assert.Equal(
                stoppedFirst.GetProperty("actions").GetArrayLength(),
                stoppedSecond.GetProperty("actions").GetArrayLength());
            Assert.Equal(
                stoppedFirst.GetProperty("board").GetArrayLength(),
                stoppedSecond.GetProperty("board").GetArrayLength());

            // Pause freezes the snapshot; resume lets the match complete.
            var paused = await ViewerTestSupport.StartLiveAsync(
                server.Client, 8, 12, 20, "random-legal", "tight-aggressive");
            var pauseId = paused.GetProperty("id").GetString()!;
            await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                pauseId,
                root => root.GetProperty("handNumber").GetInt32() >= 1
                    && root.GetProperty("actions").GetArrayLength() >= 1,
                "the paused match to start playing");

            using (var response = await server.Client.PostAsync($"/api/live/{pauseId}/pause", null))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            await Task.Delay(150);
            var frozenFirst = await ViewerTestSupport.GetJsonAsync(server.Client, $"/api/live/{pauseId}");
            await Task.Delay(150);
            var frozenSecond = await ViewerTestSupport.GetJsonAsync(server.Client, $"/api/live/{pauseId}");
            Assert.Equal("paused", frozenFirst.GetProperty("status").GetString());
            Assert.Equal("paused", frozenSecond.GetProperty("status").GetString());
            Assert.Equal(
                frozenFirst.GetProperty("handNumber").GetInt32(),
                frozenSecond.GetProperty("handNumber").GetInt32());
            Assert.Equal(
                frozenFirst.GetProperty("actions").GetArrayLength(),
                frozenSecond.GetProperty("actions").GetArrayLength());

            using (var response = await server.Client.PostAsync($"/api/live/{pauseId}/resume", null))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            var completed = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                pauseId,
                root => root.GetProperty("status").GetString() == "complete",
                "the resumed match to complete");
            Assert.Equal(12, completed.GetProperty("handHashes").GetArrayLength());

            using (var response = await server.Client.GetAsync("/api/live/0000000000000000"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using (var response = await server.Client.DeleteAsync("/api/live/0000000000000000"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using (var response = await server.Client.PostAsync("/api/live/0000000000000000/pause", null))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using (var response = await server.Client.PostAsJsonAsync(
                       "/api/live",
                       new { seed = 1, hands = 6, delayMs = 0, seat0Agent = "cheater", seat1Agent = "random-legal", allowWeakSeed = true }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            using (var response = await server.Client.PostAsJsonAsync(
                       "/api/live",
                       new { seed = 1, hands = 0, delayMs = 0, seat0Agent = "random-legal", seat1Agent = "random-legal", allowWeakSeed = true }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            using (var response = await server.Client.PostAsJsonAsync(
                       "/api/live",
                       new { seed = 1, hands = 501, delayMs = 0, seat0Agent = "random-legal", seat1Agent = "random-legal", allowWeakSeed = true }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        });
    }

    [Fact]
    public async Task LiveMatchCreation_GivenWeakSeed_Expected400WeakSeedUnlessExplicitlyAllowed()
    {
        // The web host mirrors the CLI policy; both bounds must name the same minimum.
        Assert.Equal(1_099_511_627_776UL, Wip.Tholdem.Web.SessionSeedPolicy.MinimumSeed);
        Assert.False(Wip.Tholdem.Web.SessionSeedPolicy.IsStrong(1_099_511_627_775UL));
        Assert.True(Wip.Tholdem.Web.SessionSeedPolicy.IsStrong(1_099_511_627_776UL));

        await ViewerTestSupport.WithServerAsync("live-seed-guard", async server =>
        {
            var request = new
            {
                seed = 42,
                hands = 1,
                delayMs = 0,
                seat0Agent = "random-legal",
                seat1Agent = "random-legal",
            };

            // A documented demo seed is refused while it is guessable from one observation.
            using (var response = await server.Client.PostAsJsonAsync("/api/live", request))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("weak-seed", document.RootElement.GetProperty("error").GetString());
                Assert.Equal(
                    1_099_511_627_776UL,
                    document.RootElement.GetProperty("minimum").GetUInt64());
            }

            // The explicit opt-in keeps deterministic demos and tests reproducible.
            using (var response = await server.Client.PostAsJsonAsync(
                       "/api/live",
                       new
                       {
                           seed = 42,
                           hands = 1,
                           delayMs = 0,
                           seat0Agent = "random-legal",
                           seat1Agent = "random-legal",
                           allowWeakSeed = true,
                       }))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(42UL, document.RootElement.GetProperty("seed").GetUInt64());
            }

            // A seed at the minimum is strong and needs no opt-in.
            using (var response = await server.Client.PostAsJsonAsync(
                       "/api/live",
                       new
                       {
                           seed = 1_099_511_627_776UL,
                           hands = 1,
                           delayMs = 0,
                           seat0Agent = "random-legal",
                           seat1Agent = "random-legal",
                       }))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }

            // The form sends the 64-bit seed as a decimal string so the browser keeps every bit.
            using (var response = await server.Client.PostAsJsonAsync(
                       "/api/live",
                       new
                       {
                           seed = "1099511627776",
                           hands = 1,
                           delayMs = 0,
                           seat0Agent = "random-legal",
                           seat1Agent = "random-legal",
                       }))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
        });
    }

    [Fact]
    public void ViewerAssets_GivenLiveForm_ExpectedStrongGeneratedSeedAndWeakSeedOptIn()
    {
        var viewerDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Web", "wwwroot", "viewer");
        var indexHtml = File.ReadAllText(Path.Combine(viewerDirectory, "index.html"));
        var seedInput = Regex.Match(indexHtml, "<input id=\"seed\"[^>]*>").Value;
        Assert.False(string.IsNullOrEmpty(seedInput), "The viewer form must keep its #seed input.");
        Assert.Contains("value=\"1099511627776\"", seedInput, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"42\"", seedInput, StringComparison.Ordinal);
        Assert.Contains("id=\"allow-weak-seed\"", indexHtml, StringComparison.Ordinal);

        var script = File.ReadAllText(Path.Combine(viewerDirectory, "app.js"));
        Assert.Contains("crypto.getRandomValues", script, StringComparison.Ordinal);
        Assert.Contains("randomSeedText", script, StringComparison.Ordinal);
        Assert.Contains("1099511627776", script, StringComparison.Ordinal);
        Assert.Contains("allow-weak-seed", script, StringComparison.Ordinal);
        Assert.Contains("allowWeakSeed", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordedSessions_GivenSimulatedHistory_ExpectedListingDetailReplayAndMissingHand404()
    {
        await ViewerTestSupport.WithServerAsync("replay", async server =>
        {
            var report = MatchSimulator.Run(
                new RandomLegalAgent(),
                new TightAggressiveAgent(),
                new SimulationOptions(7UL, 6));
            var sessionDirectory = Path.Combine(server.DataDirectory, "sessions", report.SessionId);
            MatchSimulator.Run(
                new RandomLegalAgent(),
                new TightAggressiveAgent(),
                new SimulationOptions(7UL, 6, HistoryDirectory: sessionDirectory));

            var list = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/sessions");
            Assert.Equal(0, list.GetProperty("skipped").GetInt32());
            var sessions = list.GetProperty("sessions").EnumerateArray().ToArray();
            Assert.Single(sessions);
            var manifest = sessions[0];
            Assert.Equal(report.SessionId, manifest.GetProperty("sessionId").GetString());
            Assert.Equal(7UL, manifest.GetProperty("seed").GetUInt64());
            Assert.Equal(6, manifest.GetProperty("hands").GetInt32());
            Assert.Equal(1000L, manifest.GetProperty("startingStack").GetInt64());
            Assert.Equal(5L, manifest.GetProperty("smallBlind").GetInt64());
            Assert.Equal(10L, manifest.GetProperty("bigBlind").GetInt64());
            Assert.Equal(
                new[] { "random-legal", "tight-aggressive" },
                ViewerTestSupport.StringArray(manifest, "agents"));
            Assert.Equal(
                AgentTestSupport.Sha256Hex(File.ReadAllBytes(Path.Combine(sessionDirectory, "session.json"))),
                manifest.GetProperty("manifestHash").GetString());
            Assert.Equal(6, manifest.GetProperty("handCount").GetInt32());

            // A malformed manifest is skipped, not surfaced as a session.
            var brokenDirectory = Path.Combine(server.DataDirectory, "sessions", "broken-session");
            Directory.CreateDirectory(brokenDirectory);
            File.WriteAllText(Path.Combine(brokenDirectory, "session.json"), "{ not-valid-json");
            var listWithBroken = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/sessions");
            Assert.Equal(1, listWithBroken.GetProperty("skipped").GetInt32());
            Assert.Single(listWithBroken.GetProperty("sessions").EnumerateArray());

            var detail = await ViewerTestSupport.GetJsonAsync(server.Client, $"/api/sessions/{report.SessionId}");
            var summaries = detail.GetProperty("hands").EnumerateArray().ToArray();
            Assert.Equal(6, summaries.Length);
            Assert.Equal(
                new[] { 1, 2, 3, 4, 5, 6 },
                summaries.Select(summary => summary.GetProperty("handNumber").GetInt32()).ToArray());

            var expected = AgentTestSupport.ReadRecord(sessionDirectory, 3);
            var third = summaries.Single(summary => summary.GetProperty("handNumber").GetInt32() == 3);
            Assert.Equal(
                expected.Board.Select(card => card.ToString()).ToArray(),
                ViewerTestSupport.StringArray(third, "board"));
            Assert.Equal(expected.Winners.ToArray(), ViewerTestSupport.IntArray(third, "winners"));
            Assert.Equal(expected.Showdown, third.GetProperty("showdown").GetBoolean());
            Assert.Equal(expected.PotAwarded, third.GetProperty("potAwarded").GetInt64());
            Assert.Equal(expected.FinalStacks.ToArray(), ViewerTestSupport.LongArray(third, "finalStacks"));

            using var table = await server.Client.GetAsync(
                $"/viewer/table?session={Uri.EscapeDataString(report.SessionId)}&hand=3");
            Assert.Equal(HttpStatusCode.OK, table.StatusCode);
            Assert.Equal("text/html", table.Content.Headers.ContentType?.MediaType);
            var html = await table.Content.ReadAsStringAsync();
            Assert.Contains($"data-hand-number=\"{expected.HandNumber}\"", html);
            foreach (var card in expected.Board)
            {
                Assert.Contains(card.ToString(), html);
            }

            foreach (var card in expected.HoleCardsSeat0.Concat(expected.HoleCardsSeat1))
            {
                Assert.Contains(card.ToString(), html);
            }

            Assert.Contains("action-row", html);
            Assert.Contains($"data-kind=\"{expected.Actions[0].Kind}\"", html);
            Assert.Contains("Preflop", html);
            Assert.Contains($"Pot awarded: {expected.PotAwarded}", html);
            Assert.Contains($"Showdown: {(expected.Showdown ? "yes" : "no")}", html);
            Assert.All(expected.Winners, winner => Assert.Contains($"Seat {winner}", html));

            using (var missingHand = await server.Client.GetAsync(
                       $"/viewer/table?session={Uri.EscapeDataString(report.SessionId)}&hand=99"))
            {
                Assert.Equal(HttpStatusCode.NotFound, missingHand.StatusCode);
            }

            using (var missingSession = await server.Client.GetAsync("/viewer/table?session=missing&hand=1"))
            {
                Assert.Equal(HttpStatusCode.NotFound, missingSession.StatusCode);
            }

            using (var missingDetail = await server.Client.GetAsync("/api/sessions/missing"))
            {
                Assert.Equal(HttpStatusCode.NotFound, missingDetail.StatusCode);
            }
        });
    }

    [Fact]
    public async Task StrategyAndLedgerEndpoints_GivenEmptyThenPopulatedDataDirectory_ExpectedSortedPassthroughAndChampion()
    {
        await ViewerTestSupport.WithServerAsync("strategies", async server =>
        {
            var emptyStrategies = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/strategies");
            Assert.Empty(emptyStrategies.GetProperty("strategies").EnumerateArray());
            Assert.Equal(0, emptyStrategies.GetProperty("skipped").GetInt32());

            var emptyLedger = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/ledger");
            Assert.Empty(emptyLedger.GetProperty("entries").EnumerateArray());
            Assert.Equal(0, emptyLedger.GetProperty("skipped").GetInt32());

            using (var response = await server.Client.GetAsync("/api/champion"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            var strategyDirectory = Path.Combine(server.DataDirectory, "strategies");
            Directory.CreateDirectory(strategyDirectory);
            File.WriteAllText(
                Path.Combine(strategyDirectory, "alpha.json"),
                "{\"threshold\":0.75,\"tags\":[\"value\",\"bluff\"],\"meta\":{\"owner\":\"qa\"}}");
            File.WriteAllText(Path.Combine(strategyDirectory, "zeta.json"), "{\"threshold\":0.5}");
            File.WriteAllText(Path.Combine(strategyDirectory, "broken.json"), "this is not json");
            File.WriteAllText(
                Path.Combine(server.DataDirectory, "ledger.json"),
                "[{\"iteration\":1,\"promoted\":false},{\"iteration\":2,\"promoted\":true}]");

            var strategies = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/strategies");
            var entries = strategies.GetProperty("strategies").EnumerateArray().ToArray();
            Assert.Equal(2, entries.Length);
            Assert.Equal(1, strategies.GetProperty("skipped").GetInt32());
            Assert.Equal(new[] { "alpha", "zeta" }, entries.Select(entry => entry.GetProperty("id").GetString()).ToArray());
            Assert.Equal("alpha.json", entries[0].GetProperty("file").GetString());
            Assert.Equal("zeta.json", entries[1].GetProperty("file").GetString());
            Assert.Equal(0.75, entries[0].GetProperty("payload").GetProperty("threshold").GetDouble());
            Assert.Equal(new[] { "value", "bluff" }, ViewerTestSupport.StringArray(entries[0].GetProperty("payload"), "tags"));

            var detail = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/strategies/alpha");
            Assert.Equal("alpha", detail.GetProperty("id").GetString());
            Assert.Equal(0.75, detail.GetProperty("payload").GetProperty("threshold").GetDouble());
            Assert.Equal("qa", detail.GetProperty("payload").GetProperty("meta").GetProperty("owner").GetString());

            using (var response = await server.Client.GetAsync("/api/strategies/missing"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            var ledger = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/ledger");
            var ledgerEntries = ledger.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(2, ledgerEntries.Length);
            Assert.Equal(0, ledger.GetProperty("skipped").GetInt32());
            Assert.Equal(1, ledgerEntries[0].GetProperty("iteration").GetInt32());
            Assert.False(ledgerEntries[0].GetProperty("promoted").GetBoolean());
            Assert.Equal(2, ledgerEntries[1].GetProperty("iteration").GetInt32());
            Assert.True(ledgerEntries[1].GetProperty("promoted").GetBoolean());

            using (var beforeChampion = await server.Client.GetAsync("/api/champion"))
            {
                Assert.Equal(HttpStatusCode.NotFound, beforeChampion.StatusCode);
            }

            File.WriteAllText(
                Path.Combine(server.DataDirectory, "active-champion.json"),
                "{\"id\":\"alpha\",\"iteration\":2,\"reason\":\"gated\"}");
            var champion = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/champion");
            Assert.Equal("alpha", champion.GetProperty("id").GetString());
            Assert.Equal(2, champion.GetProperty("iteration").GetInt32());
            Assert.Equal("gated", champion.GetProperty("reason").GetString());
        });
    }

    [Fact]
    public void ViewerAssets_GivenOfflineViewerDirectory_ExpectedNoExternalReferencesAndRelativeIndexAssets()
    {
        var viewerDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Web", "wwwroot", "viewer");
        Assert.True(Directory.Exists(viewerDirectory), $"Missing viewer directory: {viewerDirectory}");

        var files = Directory.GetFiles(viewerDirectory).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(3, files.Length);
        Assert.Contains(Path.Combine(viewerDirectory, "index.html"), files);
        Assert.Contains(Path.Combine(viewerDirectory, "viewer.css"), files);
        Assert.Contains(Path.Combine(viewerDirectory, "app.js"), files);

        var forbidden = new[] { "http://", "https://", "//cdn", "@import url(http" };
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.False(
                    text.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(file)} contains forbidden external reference '{token}'.");
            }
        }

        var indexHtml = File.ReadAllText(Path.Combine(viewerDirectory, "index.html"));
        var references = Regex.Matches(indexHtml, "(?:src|href)=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        // The inline favicon is an in-document SVG data URI: no request, no external asset.
        var dataReferences = references
            .Where(reference => reference.StartsWith("data:", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(dataReferences);
        Assert.StartsWith("data:image/svg+xml", dataReferences[0]);

        var assetReferences = references.Where(reference => !reference.StartsWith("data:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, assetReferences.Length);
        Assert.Contains("app.js", assetReferences);
        Assert.Contains("viewer.css", assetReferences);
        Assert.All(
            assetReferences,
            reference => Assert.False(
                reference.StartsWith('/') || reference.StartsWith("//") || reference.Contains("://", StringComparison.Ordinal),
                $"Index asset reference '{reference}' must be same-origin and relative."));
    }

    [Fact]
    public void AgentSources_GivenSpectatorBoundaryScan_ExpectedNoWebOrLiveIdentifiers()
    {
        var agentsDirectory = Path.Combine(AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Agents");
        Assert.True(Directory.Exists(agentsDirectory), $"Missing agents directory: {agentsDirectory}");

        var sources = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(sources.Length >= 10, $"Expected at least ten agent source files, found {sources.Length}.");
        Assert.Contains(sources, path => Path.GetFileName(path) == "IPokerAgent.cs");

        var identifiers = new[] { "SpectatorSnapshot", "TholdemWebHost", "LiveMatch" };
        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            foreach (var identifier in identifiers)
            {
                Assert.False(
                    text.Contains(identifier, StringComparison.Ordinal),
                    $"{Path.GetFileName(source)} references the spectator surface identifier '{identifier}'.");
            }
        }
    }

    [Fact]
    public async Task Status_GivenConfiguredDataDirectory_ExpectedOfflineSpectatorReport()
    {
        await ViewerTestSupport.WithServerAsync("status", async server =>
        {
            var status = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/status");
            Assert.Equal("spectator", status.GetProperty("mode").GetString());
            Assert.True(status.GetProperty("offline").GetBoolean());
            Assert.Equal(
                Path.GetFullPath(server.DataDirectory),
                Path.GetFullPath(status.GetProperty("dataDirectory").GetString()!));
            Assert.Equal(0, status.GetProperty("sessions").GetInt32());
            Assert.Equal(0, status.GetProperty("strategies").GetInt32());
            Assert.Equal(0, status.GetProperty("liveMatches").GetInt32());

            // liveMatches counts only running/paused matches; completed and stopped matches stay
            // readable for their final snapshot but are no longer "live" (round-7 RV-06).
            var runningMatch = await ViewerTestSupport.StartLiveAsync(
                server.Client, 1, 12, 100, "random-legal", "random-legal");
            var runningId = runningMatch.GetProperty("id").GetString()!;
            await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                runningId,
                root => root.GetProperty("status").GetString() == "running"
                    && root.GetProperty("actions").GetArrayLength() >= 1,
                "the status probe match to start playing");
            var whileRunning = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/status");
            Assert.Equal(1, whileRunning.GetProperty("liveMatches").GetInt32());

            using (var deleted = await server.Client.DeleteAsync($"/api/live/{runningId}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            }

            var afterStop = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/status");
            Assert.Equal(0, afterStop.GetProperty("liveMatches").GetInt32());

            var started = await ViewerTestSupport.StartLiveAsync(
                server.Client, 1, 1, 0, "random-legal", "random-legal");
            var id = started.GetProperty("id").GetString()!;
            await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the status probe match to complete");

            var afterMatch = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/status");
            Assert.Equal(0, afterMatch.GetProperty("liveMatches").GetInt32());

            var strategyDirectory = Path.Combine(server.DataDirectory, "strategies");
            Directory.CreateDirectory(strategyDirectory);
            File.WriteAllText(Path.Combine(strategyDirectory, "one.json"), "{\"threshold\":0.6}");
            var afterStrategy = await ViewerTestSupport.GetJsonAsync(server.Client, "/api/status");
            Assert.Equal(1, afterStrategy.GetProperty("strategies").GetInt32());
            Assert.True(afterStrategy.GetProperty("offline").GetBoolean());
        });
    }

    private sealed record LiveOutcome(string[] Hashes, string[] FinalHoleCards);

    private static async Task<LiveOutcome> RunLiveAsync(
        HttpClient client,
        ulong seed,
        int hands,
        string seat0Agent,
        string seat1Agent)
    {
        var started = await ViewerTestSupport.StartLiveAsync(
            client, seed, hands, 0, seat0Agent, seat1Agent);
        var id = started.GetProperty("id").GetString()!;
        var snapshot = await ViewerTestSupport.WaitForSnapshotAsync(
            client,
            id,
            root => root.GetProperty("status").GetString() == "complete",
            $"the seed {seed} live match to complete");
        var holeCards = ViewerTestSupport.StringArray(snapshot, "holeCardsSeat0")
            .Concat(ViewerTestSupport.StringArray(snapshot, "holeCardsSeat1"))
            .ToArray();
        Assert.Equal(4, holeCards.Length);
        return new LiveOutcome(ViewerTestSupport.StringArray(snapshot, "handHashes"), holeCards);
    }
}
