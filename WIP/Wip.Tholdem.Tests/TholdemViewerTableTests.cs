using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Drives the real Kestrel HTTP surface for the upgraded poker-table viewer: CSS card faces,
/// tabbed main-area panels, dealer button, hand-strength names, to-call and pot odds, winner
/// highlighting, the colour-coded action log, the inline favicon and the offline guarantee.
/// </summary>
public sealed class TholdemViewerTableTests
{
    private static readonly string[] TabViews = ["live", "replay", "strategies", "ledger"];

    private static readonly string[] LedgerLabels =
    [
        "Iteration",
        "Champion",
        "Challenger",
        "Promoted",
        "Reason",
        "Win rate",
        "bb/100",
        "LCB Δ",
        "Samples",
    ];

    private static readonly string[] HandNamePrefixes =
    [
        "High Card, ",
        "One Pair, ",
        "Two Pair, ",
        "Three of a Kind, ",
        "Straight, ",
        "Flush, ",
        "Full House, ",
        "Four of a Kind, ",
        "Straight Flush, ",
    ];

    private static readonly string[] EmptyStates =
    [
        "No live match yet — pick a seed and press Start",
        "Select a recorded session to replay",
        "No strategies recorded yet — run the training loop",
        "No promotions yet",
    ];

    [Fact]
    public async Task ViewerIndex_GivenTableExperienceMarkup_ExpectedTabsCardsFaviconLedgerAndSidebarControls()
    {
        await ViewerTestSupport.WithServerAsync("table-markup", async server =>
        {
            var html = await GetTextAsync(server.Client, "/viewer/index.html");

            foreach (var view in TabViews)
            {
                Assert.Contains($"class=\"tab\" data-view=\"{view}\"", html);
            }

            Assert.Contains("<meta name=\"viewport\"", html);
            Assert.Contains("card--", html);
            Assert.Contains("card--empty", html);
            Assert.Contains("rel=\"icon\"", html);
            Assert.Contains("href=\"data:image/svg+xml", html);

            foreach (var label in LedgerLabels)
            {
                Assert.Contains("<th>" + label + "</th>", html);
            }

            Assert.Contains("\"app.js\"", html);
            Assert.Contains("\"viewer.css\"", html);

            // The sidebar keeps the live form, controls, poll speed, sessions and replay slider.
            foreach (var id in new[]
                     {
                         "live-form", "seed", "hands", "delay", "seat0-agent", "seat1-agent", "start-live",
                         "pause-live", "resume-live", "stop-live", "poll-speed", "sessions",
                         "replay-panel", "replay-scrubber", "replay-hand",
                     })
            {
                Assert.Contains($"id=\"{id}\"", html);
            }

            Assert.Contains(EmptyStates[0], html);
            Assert.Contains(EmptyStates[2], html);
            Assert.Contains(EmptyStates[3], html);
        });
    }

    [Fact]
    public async Task ViewerStyles_GivenResponsiveTable_Expected900pxBreakpointReducedMotionAndCompactCards()
    {
        await ViewerTestSupport.WithServerAsync("table-styles", async server =>
        {
            var css = await GetTextAsync(server.Client, "/viewer/viewer.css");

            Assert.Contains("@media", css);
            Assert.Contains("max-width: 900px", css);
            Assert.Contains("prefers-reduced-motion", css);
            Assert.Contains(".card__rank", css);
            Assert.Contains(".card__suit", css);
            Assert.Contains(".card--back", css);
            Assert.Contains(".card--empty", css);
            Assert.Contains(".dealer-button", css);
            Assert.Contains(".is-winner", css);
            Assert.Contains(".tab", css);
            Assert.Contains(".pill--promoted", css);
            Assert.Contains(".pill--rejected", css);
            Assert.Contains(".action--fold", css);
            Assert.Contains(".action--check", css);
            Assert.Contains(".action--call", css);
            Assert.Contains(".action--bet", css);
            Assert.Contains(".action--raise", css);
            Assert.Contains(".action--allin", css);
        });
    }

    [Fact]
    public async Task ReplayTable_GivenHandWithFourCardBoard_ExpectedCardFacesHandNamesDealerWinnerAndActionChips()
    {
        await ViewerTestSupport.WithServerAsync("table-faces", async server =>
        {
            var sessionId = CreateRecordedSession(server, 50UL, 5);
            var directory = Path.Combine(server.DataDirectory, "sessions", sessionId);
            var records = AgentTestSupport.ReadAllRecords(directory);
            var candidates = records.Where(record => record.Board.Count == 4).ToArray();
            Assert.True(
                candidates.Length > 0,
                "The fixture session must contain a hand with a four-card board (flop plus turn).");
            var hand = candidates[0];

            using var response = await server.Client.GetAsync(
                $"/viewer/table?session={Uri.EscapeDataString(sessionId)}&hand={hand.HandNumber}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            var html = await response.Content.ReadAsStringAsync();

            // Every visible card is a CSS face with its exact data-card code, suit glyph and aria label.
            foreach (var card in hand.Board
                         .Concat(hand.HoleCardsSeat0)
                         .Concat(hand.HoleCardsSeat1))
            {
                Assert.Contains($"data-card=\"{card}\"", html);
                Assert.Contains($"aria-label=\"{CardAriaLabel(card)}\"", html);
                Assert.Contains(SuitGlyph(card.Suit), html);
                Assert.Contains($"card--{SuitName(card.Suit)}", html);
            }

            Assert.Contains("class=\"card", html);
            Assert.Equal(
                5 - hand.Board.Count,
                Regex.Matches(html, "class=\"card card--empty\"").Count);

            // Both seats carry a hand-strength name from the contracted set.
            var expectedNames = SeatHandNames(html);
            Assert.Equal(2, expectedNames.Length);
            Assert.All(expectedNames, name => Assert.True(IsKnownHandName(name), $"Unexpected hand name '{name}'."));

            // The winning seat is highlighted and badged; the dealer button marks the button seat.
            var winnerTag = SeatOpeningTag(html, hand.Winners[0]);
            Assert.Contains("is-winner", winnerTag);
            Assert.Contains("Winner", SeatBody(html, hand.Winners[0]));
            var dealerTag = SeatOpeningTag(html, hand.ButtonSeat);
            Assert.Contains("data-dealer=\"true\"", dealerTag);
            var otherSeat = 1 - hand.ButtonSeat;
            Assert.DoesNotContain("data-dealer=\"true\"", SeatOpeningTag(html, otherSeat));

            // The action log keeps one row per action with a street chip and a kind colour class.
            Assert.Contains("action-row", html);
            foreach (var kind in hand.Actions.Select(action => action.Kind).Distinct(StringComparer.Ordinal))
            {
                Assert.Contains($"data-kind=\"{kind}\"", html);
                Assert.Contains("action--" + kind.ToLowerInvariant(), html);
            }

            foreach (var street in hand.Actions.Select(action => action.Street).Distinct())
            {
                Assert.Contains($"chip--{StreetSlug(street)}", html);
            }

            Assert.Contains($"Pot awarded: {hand.PotAwarded}", html);
        });
    }

    [Fact]
    public async Task LiveSnapshot_GivenPacedMatchOnFlop_ExpectedHandNamesButtonToCallPotOddsAndCompletedWinner()
    {
        await ViewerTestSupport.WithServerAsync("table-live", async server =>
        {
            // Contracted delay-0 match: it plays through the streets and completes with every new
            // spectator field present; the completed snapshot names the winner.
            var started = await ViewerTestSupport.StartLiveAsync(
                server.Client, 42, 6, 0, "random-legal", "tight-aggressive");
            var id = started.GetProperty("id").GetString()!;
            var completed = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the delay-0 live match to complete");

            Assert.Equal("complete", completed.GetProperty("status").GetString());
            Assert.True(completed.GetProperty("isComplete").GetBoolean());
            Assert.Equal(6, completed.GetProperty("handHashes").GetArrayLength());

            var completedNames = ViewerTestSupport.StringArray(completed, "handNames");
            Assert.Equal(2, completedNames.Length);
            Assert.All(completedNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
            Assert.All(completedNames, name => Assert.True(IsKnownHandName(name), $"Unexpected hand name '{name}'."));
            Assert.InRange(completed.GetProperty("buttonSeat").GetInt32(), 0, 1);
            Assert.True(completed.GetProperty("amountToCall").GetInt64() >= 0);
            Assert.InRange(completed.GetProperty("potOdds").GetDouble(), 0.0, 1.0);
            var winners = ViewerTestSupport.IntArray(completed, "winners");
            Assert.NotEmpty(winners);
            Assert.All(winners, winner => Assert.InRange(winner, 0, 1));

            // Delay only paces progress, so a paced run makes the flop observable. On the flop the
            // names are evaluated best-five hands (board + both hole cards), never "Preflop".
            var paced = await ViewerTestSupport.StartLiveAsync(
                server.Client, 42, 6, 25, "random-legal", "tight-aggressive");
            var pacedId = paced.GetProperty("id").GetString()!;

            var flopSnapshot = default(JsonElement);
            var pacedCompleted = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                pacedId,
                root =>
                {
                    if (flopSnapshot.ValueKind == JsonValueKind.Undefined
                        && root.GetProperty("board").GetArrayLength() >= 3)
                    {
                        flopSnapshot = root.Clone();
                    }

                    return root.GetProperty("status").GetString() == "complete";
                },
                "the paced live match to reach the flop and complete");

            Assert.NotEqual(JsonValueKind.Undefined, flopSnapshot.ValueKind);
            var handNames = ViewerTestSupport.StringArray(flopSnapshot, "handNames");
            Assert.Equal(2, handNames.Length);
            Assert.All(handNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
            Assert.All(handNames, name => Assert.NotEqual("Preflop", name));
            Assert.All(handNames, name => Assert.True(IsKnownHandName(name), $"Unexpected hand name '{name}'."));

            var buttonSeat = flopSnapshot.GetProperty("buttonSeat").GetInt32();
            Assert.InRange(buttonSeat, 0, 1);

            var amountToCall = flopSnapshot.GetProperty("amountToCall").GetInt64();
            Assert.True(amountToCall >= 0, $"amountToCall must be non-negative; was {amountToCall}.");
            var potOdds = flopSnapshot.GetProperty("potOdds").GetDouble();
            Assert.InRange(potOdds, 0.0, 1.0);
            if (amountToCall == 0)
            {
                Assert.Equal(0.0, potOdds);
            }

            Assert.Equal("complete", pacedCompleted.GetProperty("status").GetString());
            Assert.Equal(2, ViewerTestSupport.StringArray(pacedCompleted, "handNames").Length);
        });
    }

    [Fact]
    public async Task SessionDetail_GivenRecordedHands_ExpectedHandNamesButtonSeatHandEndpointAndTraversalSafety()
    {
        await ViewerTestSupport.WithServerAsync("table-detail", async server =>
        {
            var sessionId = CreateRecordedSession(server, 50UL, 5);
            var directory = Path.Combine(server.DataDirectory, "sessions", sessionId);
            var records = AgentTestSupport.ReadAllRecords(directory);

            var detail = await ViewerTestSupport.GetJsonAsync(
                server.Client,
                "/api/sessions/" + Uri.EscapeDataString(sessionId));
            var summaries = detail.GetProperty("hands").EnumerateArray().ToArray();
            Assert.Equal(records.Length, summaries.Length);

            foreach (var summary in summaries)
            {
                var names = ViewerTestSupport.StringArray(summary, "handNames");
                Assert.Equal(2, names.Length);
                Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
                Assert.All(names, name => Assert.True(IsKnownHandName(name), $"Unexpected hand name '{name}'."));
                Assert.InRange(summary.GetProperty("buttonSeat").GetInt32(), 0, 1);
            }

            var flopHand = records.First(record => record.Board.Count >= 3);
            var flopSummary = summaries.Single(
                summary => summary.GetProperty("handNumber").GetInt32() == flopHand.HandNumber);
            Assert.All(
                ViewerTestSupport.StringArray(flopSummary, "handNames"),
                name => Assert.NotEqual("Preflop", name));

            // The replay UI reads one recorded hand as JSON (never as injected markup).
            var handJson = await ViewerTestSupport.GetJsonAsync(
                server.Client,
                $"/api/sessions/{Uri.EscapeDataString(sessionId)}/hands/{flopHand.HandNumber}");
            Assert.Equal(flopHand.HandNumber, handJson.GetProperty("handNumber").GetInt32());
            Assert.Equal(flopHand.ButtonSeat, handJson.GetProperty("buttonSeat").GetInt32());
            Assert.Equal(2, ViewerTestSupport.StringArray(handJson, "handNames").Length);
            Assert.Equal(
                flopHand.Board.Select(card => card.ToString()).ToArray(),
                ViewerTestSupport.StringArray(handJson, "board"));
            Assert.Equal(
                flopHand.HoleCardsSeat0.Concat(flopHand.HoleCardsSeat1)
                    .Select(card => card.ToString()).ToArray(),
                ViewerTestSupport.StringArray(handJson, "holeCardsSeat0")
                    .Concat(ViewerTestSupport.StringArray(handJson, "holeCardsSeat1")).ToArray());
            Assert.Equal(flopHand.Actions.Count, handJson.GetProperty("actions").GetArrayLength());

            foreach (var path in new[]
                     {
                         "/api/sessions/" + Uri.EscapeDataString("..\\..\\ledger"),
                         "/api/sessions/" + Uri.EscapeDataString("../.."),
                         "/api/sessions/" + Uri.EscapeDataString(sessionId) + "/hands/999",
                         "/api/strategies/" + Uri.EscapeDataString("..\\..\\ledger"),
                         "/viewer/table?session=" + Uri.EscapeDataString("..\\..")
                             + "&hand=1",
                         "/viewer/table?session=" + Uri.EscapeDataString(sessionId) + "&hand=999",
                     })
            {
                using var response = await server.Client.GetAsync(path);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        });
    }

    [Fact]
    public async Task ViewerScript_GivenArtifactContent_ExpectedNoInnerHtmlNoExternalUrlAndExactEmptyStates()
    {
        await ViewerTestSupport.WithServerAsync("table-script", async server =>
        {
            var script = await GetTextAsync(server.Client, "/viewer/app.js");

            Assert.DoesNotContain("innerHTML", script);
            Assert.Contains("textContent", script);
            Assert.Contains("document.createElement", script);
            Assert.Contains("/api/sessions/", script);
            Assert.Contains("card--", script);
            Assert.Contains("aria-label", script);
            foreach (var emptyState in EmptyStates)
            {
                Assert.Contains(emptyState, script);
            }

            var forbidden = new[] { "http://", "https://", "//cdn", "@import url(http" };
            foreach (var asset in new[] { "app.js", "viewer.css", "index.html" })
            {
                var text = await GetTextAsync(server.Client, "/viewer/" + asset);
                foreach (var token in forbidden)
                {
                    Assert.False(
                        text.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"{asset} contains the forbidden external reference '{token}'.");
                }
            }
        });
    }

    private static async Task<string> GetTextAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string CreateRecordedSession(ViewerTestServer server, ulong seed, int hands)
    {
        // Two random-legal seats: seed 50 over five hands deals hands with a four-card board (flop
        // plus turn), which is the fixture this suite exercises for the replay table. The agents'
        // observation-derived randomness changed the rendered deals in round 12; the assertion
        // itself (a four-card board must exist) is unchanged.
        var report = MatchSimulator.Run(
            new RandomLegalAgent(),
            new RandomLegalAgent(),
            new SimulationOptions(seed, hands));
        var directory = Path.Combine(server.DataDirectory, "sessions", report.SessionId);
        MatchSimulator.Run(
            new RandomLegalAgent(),
            new RandomLegalAgent(),
            new SimulationOptions(seed, hands, HistoryDirectory: directory));
        return report.SessionId;
    }

    private static string[] SeatHandNames(string html)
    {
        return Regex.Matches(html, "data-hand-name=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static string SeatOpeningTag(string html, int seat)
    {
        var match = Regex.Match(
            html,
            $"<section class=\"replay-seat[^\"]*\" data-seat=\"{seat}\"[^>]*>");
        Assert.True(match.Success, $"Missing replay seat section for seat {seat}.");
        return match.Value;
    }

    private static string SeatBody(string html, int seat)
    {
        var match = Regex.Match(
            html,
            $"<section class=\"replay-seat[^\"]*\" data-seat=\"{seat}\"[^>]*>(?<body>.*?)</section>",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"Missing replay seat section for seat {seat}.");
        return match.Groups["body"].Value;
    }

    private static bool IsKnownHandName(string name) =>
        name == "Preflop"
        || HandNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static string StreetSlug(int street) => street switch
    {
        0 => "preflop",
        1 => "flop",
        2 => "turn",
        3 => "river",
        _ => "showdown",
    };

    private static string SuitName(Suit suit) => suit switch
    {
        Suit.Clubs => "clubs",
        Suit.Diamonds => "diamonds",
        Suit.Hearts => "hearts",
        Suit.Spades => "spades",
        _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, "Unknown suit."),
    };

    private static string SuitGlyph(Suit suit) => suit switch
    {
        Suit.Clubs => "♣",
        Suit.Diamonds => "♦",
        Suit.Hearts => "♥",
        Suit.Spades => "♠",
        _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, "Unknown suit."),
    };

    private static string CardAriaLabel(Card card) => RankName(card.Rank) + " of " + SuitName(card.Suit);

    private static string RankName(Rank rank) => rank switch
    {
        Rank.Two => "Two",
        Rank.Three => "Three",
        Rank.Four => "Four",
        Rank.Five => "Five",
        Rank.Six => "Six",
        Rank.Seven => "Seven",
        Rank.Eight => "Eight",
        Rank.Nine => "Nine",
        Rank.Ten => "Ten",
        Rank.Jack => "Jack",
        Rank.Queen => "Queen",
        Rank.King => "King",
        Rank.Ace => "Ace",
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unknown rank."),
    };
}
