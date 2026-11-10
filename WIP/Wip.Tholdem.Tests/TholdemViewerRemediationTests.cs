using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Baselines;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Xunit;
using Xunit.Sdk;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Round-7 review remediation facts for the spectator viewer. The viewer is a browser SPA whose
/// DOM behaviour is proved by the round-7 screenshots and the headless-Chromium capture tool (the
/// Playwright check is deliberately outside the committed suite per <c>docs/goal-tholdem/goal.md</c>);
/// these facts pin the server contract plus the exact source/CSS rules that produced each finding,
/// so reverting a fix fails a committed test.
/// </summary>
public sealed class TholdemViewerRemediationTests
{
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

    // --- RV-01 (P1): the seat ring follows the seat to act, never the last actor -----------------

    [Fact]
    public async Task LiveActionRing_GivenRunningSnapshot_ExpectedActingSeatEqualsEngineSeatAndUiRingsIt()
    {
        await ViewerTestSupport.WithServerAsync("rv01", async server =>
        {
            var (id, frozen) = await FreezeMidHandAsync(server.Client, 42UL);
            try
            {
                Assert.Equal("paused", frozen.GetProperty("status").GetString());
                Assert.False(frozen.GetProperty("isComplete").GetBoolean());

                // The snapshot's actingSeat must be the engine's actual seat to act for exactly the
                // action prefix the spectator has been shown. The action tail is used only to rebuild
                // that prefix; the assertion target is the API field.
                var handNumber = frozen.GetProperty("handNumber").GetInt32();
                Assert.Equal(1, handNumber);
                var handSeed = SeedDerivation.Derive(42UL, handNumber, 0, 0, 0UL);
                var table = HeadsUpHandEngine.Start(handSeed, handNumber, 1000, 5, 10, (handNumber - 1) % 2);
                var handActions = frozen.GetProperty("actions").EnumerateArray()
                    .Where(action => action.GetProperty("handNumber").GetInt32() == handNumber)
                    .ToArray();
                Assert.NotEmpty(handActions);
                foreach (var action in handActions)
                {
                    var kind = Enum.Parse<ActionKind>(action.GetProperty("kind").GetString()!);
                    table = HeadsUpHandEngine.Apply(table, new PokerAction(kind, action.GetProperty("amount").GetInt64()));
                }

                Assert.False(table.IsComplete, "The frozen hand must still be in progress.");
                Assert.InRange(table.ActingSeat, 0, 1);
                Assert.Equal(table.ActingSeat, frozen.GetProperty("actingSeat").GetInt32());
                Assert.Equal(handActions[^1].GetProperty("seat").GetInt32(), frozen.GetProperty("lastActionSeat").GetInt32());

                // The rendered ring is driven by snapshot.actingSeat; the old bug (ring from
                // actions[actions.length - 1].seat) must not be able to come back.
                var script = await GetTextAsync(server.Client, "/viewer/app.js");
                var seatState = FunctionBody(script, "function liveSeatState(snapshot)");
                Assert.Contains("snapshot.actingSeat", seatState);
                Assert.Contains("snapshot.lastActionSeat", seatState);
                Assert.Contains("isActive: seatState.acting === 0", script);
                Assert.Contains("isActive: seatState.acting === 1", script);
                Assert.DoesNotContain("actions[actions.length - 1].seat", script);
            }
            finally
            {
                await DeleteQuietlyAsync(server.Client, id);
            }

            // A completed hand has no seat to act: actingSeat is -1 (the old snapshot had no field).
            var finished = await ViewerTestSupport.StartLiveAsync(
                server.Client, 7, 1, 0, "random-legal", "random-legal");
            var finishedId = finished.GetProperty("id").GetString()!;
            var complete = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                finishedId,
                root => root.GetProperty("status").GetString() == "complete",
                "the one-hand live match to complete");
            Assert.True(complete.GetProperty("isComplete").GetBoolean());
            Assert.Equal(-1, complete.GetProperty("actingSeat").GetInt32());
        });
    }

    // --- RV-02 (P1): the live log identifies the hand on every row --------------------------------

    [Fact]
    public async Task LiveActionLog_GivenMultiHandMatch_ExpectedHandSeparatorsAndEveryRowAttributable()
    {
        await ViewerTestSupport.WithServerAsync("rv02", async server =>
        {
            var started = await ViewerTestSupport.StartLiveAsync(
                server.Client, 42, 3, 0, "random-legal", "tight-aggressive");
            var id = started.GetProperty("id").GetString()!;
            var completed = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the three-hand live match to complete");

            var actions = completed.GetProperty("actions").EnumerateArray().ToArray();
            Assert.NotEmpty(actions);
            var handNumbers = actions
                .Select(action => action.GetProperty("handNumber").GetInt32())
                .Distinct()
                .Order()
                .ToArray();
            Assert.Equal(new[] { 1, 2, 3 }, handNumbers);
            Assert.All(handNumbers, hand => Assert.Contains(
                actions,
                action => action.GetProperty("handNumber").GetInt32() == hand));

            // The rendered log must open each hand with a separator and tag every row with its hand,
            // so no row can be attributed to the wrong hand when the retained tail spans hands.
            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("function createHandSeparator(handNumber)", script);
            Assert.Contains("'Hand ' + handNumber", script);
            Assert.Contains("function appendActionRows(container, actions, startIndex, lastHand)", script);
            Assert.Contains("actionHand !== hand", script);
            Assert.Contains("row.dataset.hand = String(action.handNumber)", script);
        });
    }

    // --- RV-03: a completed hand shows the awarded pot, not the settled zero -----------------

    [Fact]
    public async Task LivePot_GivenCompletedHand_ExpectedPotAwardedInsteadOfPostSettlementZero()
    {
        await ViewerTestSupport.WithServerAsync("rv03", async server =>
        {
            var started = await ViewerTestSupport.StartLiveAsync(
                server.Client, 7, 1, 0, "random-legal", "random-legal");
            var id = started.GetProperty("id").GetString()!;
            var complete = await ViewerTestSupport.WaitForSnapshotAsync(
                server.Client,
                id,
                root => root.GetProperty("status").GetString() == "complete",
                "the one-hand live match to complete");

            // After settlement the engine pot is 0; the snapshot must still expose what was won.
            Assert.Equal(0, complete.GetProperty("pot").GetInt64());
            var awarded = complete.GetProperty("potAwarded").GetInt64();
            Assert.True(awarded > 0, $"potAwarded must be positive after a hand resolves; was {awarded}.");
            var winners = ViewerTestSupport.IntArray(complete, "winners");
            Assert.NotEmpty(winners);
            Assert.Equal(2000L, ViewerTestSupport.LongArray(complete, "stacks").Sum());

            // Cross-check against the deterministic recording of the same match.
            var history = Path.Combine(server.DataDirectory, "expected-hand");
            MatchSimulator.Run(
                new RandomLegalAgent(),
                new RandomLegalAgent(),
                new SimulationOptions(7UL, 1, HistoryDirectory: history));
            var expected = AgentTestSupport.ReadRecord(history, 1);
            Assert.Equal(expected.PotAwarded, awarded);
            Assert.Equal(expected.Winners.ToArray(), winners);
            Assert.Equal(expected.FinalStacks.ToArray(), ViewerTestSupport.LongArray(complete, "stacks"));

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("'Pot awarded ' + potAwarded", script);
        });
    }

    // --- RV-04: replay keeps the awarded amount visible after the final pot-0 row ---------------

    [Fact]
    public async Task ReplaySettlement_GivenFoldThatAwardsThePot_ExpectedLabelledSettlementRow()
    {
        await ViewerTestSupport.WithServerAsync("rv04", async server =>
        {
            // The seed-1 fixture contains a hand that ends with a fold awarding the pot: the last
            // action's potAfter is 0 while the hand still awards the pot to the winner.
            var sessionId = CreateRecordedSession(server, 1UL, 5, new RandomLegalAgent(), new RandomLegalAgent());
            var directory = Path.Combine(server.DataDirectory, "sessions", sessionId);
            var records = AgentTestSupport.ReadAllRecords(directory);
            var hand = records.First(record => record.Actions.Count > 0
                && record.Actions[^1].Kind == "Fold"
                && record.Actions[^1].PotAfter == 0
                && record.PotAwarded > 0);
            Assert.Equal("Fold", hand.Actions[^1].Kind);
            Assert.Equal(0, hand.Actions[^1].PotAfter);
            Assert.True(hand.PotAwarded > 0);

            var handJson = await ViewerTestSupport.GetJsonAsync(
                server.Client,
                $"/api/sessions/{Uri.EscapeDataString(sessionId)}/hands/{hand.HandNumber}");
            var finalRecordedAction = handJson.GetProperty("actions").EnumerateArray().Last();
            Assert.Equal(0, finalRecordedAction.GetProperty("potAfter").GetInt64());
            Assert.Equal(hand.PotAwarded, handJson.GetProperty("potAwarded").GetInt64());

            var html = await GetTextAsync(
                server.Client,
                $"/viewer/table?session={Uri.EscapeDataString(sessionId)}&hand={hand.HandNumber}");
            var settlementIndex = html.IndexOf("data-settlement=\"true\"", StringComparison.Ordinal);
            var foldIndex = html.LastIndexOf("data-kind=\"Fold\"", StringComparison.Ordinal);
            Assert.True(settlementIndex >= 0, "The replay table must render a settlement row.");
            Assert.True(
                settlementIndex > foldIndex,
                "The settlement row must follow the final fold row so the log does not end on pot 0.");
            Assert.Contains($"data-pot-awarded=\"{hand.PotAwarded}\"", html);
            Assert.Contains($"pot awarded {hand.PotAwarded}", html);
            Assert.Contains($"Seat {hand.Winners[0]} awarded", html);

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("function createSettlementRow(settlement)", script);
            Assert.Contains("'pot awarded ' + settlement.potAwarded", script);
        });
    }

    // --- RV-05: the idle table is deliberately empty ------------------------------------------

    [Fact]
    public async Task IdleTable_GivenNoLiveMatch_ExpectedDashStacksNoCardsAndNoMatchText()
    {
        await ViewerTestSupport.WithServerAsync("rv05", async server =>
        {
            var html = await GetTextAsync(server.Client, "/viewer/index.html");
            Assert.Contains("data-seat-stack>—<", html);
            Assert.Contains("data-seat-hand-name>—<", html);
            Assert.True(
                Regex.IsMatch(html, "<p id=\"pot\"[^>]*>—</p>"),
                "The initial pot must be the deliberate idle placeholder, not 'Pot 0'.");
            Assert.Contains("No live match yet — pick a seed and press Start", html);
            Assert.DoesNotContain("data-seat-stack>0<", html);
            Assert.DoesNotContain("card--back", html);

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            var empty = FunctionBody(script, "function showTableEmpty()");
            Assert.Contains("'—'", empty);
            Assert.Contains("clearCardSlot", empty);
            Assert.DoesNotContain("renderHoleCards", empty);
        });
    }

    // --- RV-07: replay seat headers name the agents, not the stack label -----------------------

    [Fact]
    public async Task ReplaySeatHeaders_GivenManifestAgents_ExpectedAgentsInAgentSlotAndHandJsonAgents()
    {
        await ViewerTestSupport.WithServerAsync("rv07", async server =>
        {
            var sessionId = CreateRecordedSession(
                server, 777UL, 5, new TightAggressiveAgent(), new RandomLegalAgent());
            var directory = Path.Combine(server.DataDirectory, "sessions", sessionId);
            var manifest = AgentTestSupport.ReadManifest(directory);
            Assert.Equal(new[] { "tight-aggressive", "random-legal" }, manifest.Agents);

            var handJson = await ViewerTestSupport.GetJsonAsync(
                server.Client,
                $"/api/sessions/{Uri.EscapeDataString(sessionId)}/hands/2");
            Assert.Equal(manifest.Agents, ViewerTestSupport.StringArray(handJson, "agents"));

            var html = await GetTextAsync(
                server.Client,
                $"/viewer/table?session={Uri.EscapeDataString(sessionId)}&hand=2");
            Assert.Contains($"data-seat-agent=\"{manifest.Agents[0]}\"", html);
            Assert.Contains($"data-seat-agent=\"{manifest.Agents[1]}\"", html);
            Assert.Contains($">Agent: {manifest.Agents[0]}<", html);
            Assert.Contains($">Agent: {manifest.Agents[1]}<", html);
            Assert.DoesNotContain("data-seat-agent=\"final stack\"", html);

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("hand.agents", script);
            Assert.DoesNotContain("agent: 'final stack'", script);
        });
    }

    // --- RV-08: the ledger is readable (stacked with labels) at <=480 px -----------------------

    [Fact]
    public async Task LedgerMobile_GivenNarrowViewport_ExpectedStackedLabelledRows()
    {
        await ViewerTestSupport.WithServerAsync("rv08", async server =>
        {
            var css = await GetTextAsync(server.Client, "/viewer/viewer.css");
            var narrow = MediaBlock(css, "max-width: 480px");
            Assert.Contains(".ledger-table td::before", narrow);
            Assert.Contains("attr(data-label)", narrow);
            Assert.Contains("min-width: 0", narrow);
            Assert.DoesNotContain("min-width: 42rem", css);

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("const LEDGER_COLUMNS", script);
            foreach (var label in LedgerLabels)
            {
                Assert.Contains("'" + label + "'", script);
            }

            Assert.Contains("cell.dataset.label = label", script);
            Assert.Contains("LEDGER_COLUMNS[8]", script);
        });
    }

    // --- RV-09: no seat clipping and a single-row board at 901-1240 px -------------------------

    [Fact]
    public async Task ResponsiveMidWidths_Given901To1150px_ExpectedShrunkTableAndStackedShellBelow1024()
    {
        await ViewerTestSupport.WithServerAsync("rv09", async server =>
        {
            var css = await GetTextAsync(server.Client, "/viewer/viewer.css");

            // The base layout must fit the full board row in the middle column.
            var baseTable = CssBlock(css, ".poker-table");
            Assert.Contains("minmax(21rem", baseTable);

            var mid = MediaBlock(css, "max-width: 1240px");
            Assert.Contains("width: 17rem", CssBlock(mid, ".viewer-sidebar"));
            var midTable = CssBlock(mid, ".poker-table");
            Assert.Contains("minmax(9rem", midTable);
            Assert.Contains("minmax(17rem", midTable);
            Assert.Contains("--card-w: 3rem", CssBlock(mid, ".board .card"));

            var stacked = MediaBlock(css, "max-width: 1024px");
            Assert.Contains("flex-direction: column", CssBlock(stacked, ".viewer-shell"));
            Assert.Contains("width: 100%", CssBlock(stacked, ".viewer-sidebar"));
        });
    }

    // --- RV-10: table and view buttons on the first phone screen -------------------------------

    [Fact]
    public async Task MobileFirstScreen_Given390px_ExpectedTableBeforeControls()
    {
        await ViewerTestSupport.WithServerAsync("rv10", async server =>
        {
            var css = await GetTextAsync(server.Client, "/viewer/viewer.css");
            var mobile = MediaBlock(css, "max-width: 900px");
            Assert.Contains("order: -1", CssBlock(mobile, ".viewer-main"));
        });
    }

    // --- RV-11: Space is not hijacked from a focused control -----------------------------------

    [Fact]
    public async Task KeyboardSpace_GivenFocusedControl_ExpectedPauseShortcutDeferred()
    {
        await ViewerTestSupport.WithServerAsync("rv11", async server =>
        {
            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("function isInteractiveTarget(target)", script);
            var guard = FunctionBody(script, "function isInteractiveTarget(target)");
            Assert.Contains("target.closest(", guard);
            Assert.Contains("button", guard);
            Assert.Contains("input", guard);

            var handler = FunctionBody(script, "function handleKeydown(event)");
            var guardIndex = handler.IndexOf("isInteractiveTarget(event.target)", StringComparison.Ordinal);
            var spaceIndex = handler.IndexOf("event.key === ' '", StringComparison.Ordinal);
            Assert.True(guardIndex >= 0, "handleKeydown must gate every shortcut on the focus target.");
            Assert.True(
                spaceIndex > guardIndex,
                "Space must be left to the focused control; the pause shortcut only applies off-controls.");
        });
    }

    // --- RV-12: no half-implemented ARIA tabs pattern; 1-4 is the documented model -------------

    [Fact]
    public async Task ViewNavigation_GivenButtonBar_ExpectedNoTabRoleDocumented1To4AndArrowGuard()
    {
        await ViewerTestSupport.WithServerAsync("rv12", async server =>
        {
            var html = await GetTextAsync(server.Client, "/viewer/index.html");
            Assert.DoesNotContain("role=\"tab\"", html);
            Assert.DoesNotContain("role=\"tablist\"", html);
            Assert.DoesNotContain("role=\"tabpanel\"", html);
            Assert.DoesNotContain("aria-controls", html);
            Assert.Contains("<kbd>1</kbd>–<kbd>4</kbd> switch views", html);
            Assert.Contains("aria-pressed=\"true\"", html);

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            var handler = FunctionBody(script, "function handleKeydown(event)");
            var guardIndex = handler.IndexOf("isInteractiveTarget(event.target)", StringComparison.Ordinal);
            var leftIndex = handler.IndexOf("event.key === 'ArrowLeft'", StringComparison.Ordinal);
            Assert.True(guardIndex >= 0);
            Assert.True(
                leftIndex > guardIndex,
                "Arrow keys must not scrub the replay while a view/session button has focus.");
            Assert.Contains("setAttribute('aria-pressed'", script);
        });
    }

    // --- RV-13: the log legend reaches normal-text contrast ------------------------------------

    [Fact]
    public async Task ActionLegend_GivenDarkPanel_ExpectedAtLeastNormalTextContrast()
    {
        await ViewerTestSupport.WithServerAsync("rv13", async server =>
        {
            var css = await GetTextAsync(server.Client, "/viewer/viewer.css");
            var legend = CssBlock(css, ".action-legend");
            Assert.DoesNotContain("var(--border)", legend);
            var color = CssColor(css, legend, "color");
            var panel = CssVariable(css, "--panel");
            var ratio = ContrastRatio(color, panel);
            Assert.True(
                ratio >= 4.5,
                $"Action-log legend contrast {ratio:F2}:1 must be >= 4.5:1 ({color} on {panel}).");
        });
    }

    // --- RV-14: live regions, acting seat and selection semantics ------------------------------

    [Fact]
    public async Task LiveRegions_GivenDynamicRegions_ExpectedStatusLogTurnAndSelectionSemantics()
    {
        await ViewerTestSupport.WithServerAsync("rv14", async server =>
        {
            var html = await GetTextAsync(server.Client, "/viewer/index.html");
            Assert.Contains("role=\"status\"", Tag(html, "status"));
            Assert.Contains("aria-live=\"polite\"", Tag(html, "status"));
            Assert.Contains("role=\"status\"", Tag(html, "live-meta"));
            Assert.Contains("aria-live=\"polite\"", Tag(html, "live-meta"));
            Assert.Contains("aria-live=\"polite\"", Tag(html, "pot"));
            Assert.Contains("aria-live=\"polite\"", Tag(html, "board"));
            Assert.Contains("role=\"log\"", Tag(html, "action-log"));
            Assert.Contains("aria-live=\"polite\"", Tag(html, "action-log"));
            Assert.Contains("data-turn hidden", html);
            Assert.Contains("To act", html);
            Assert.Contains("data-last-action hidden", html);
            Assert.Contains("Last action", html);

            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            var renderSeat = FunctionBody(script, "function renderSeat(seat, dealerSeat)");
            Assert.Contains("aria-current", renderSeat);
            Assert.Contains("setAttribute('aria-current', 'true')", script);
            Assert.Contains("setAttribute('aria-pressed'", script);
            Assert.Contains("function appendLiveActions(container, actions)", script);
            Assert.Contains("function setText(node, text)", script);
        });
    }

    // --- RV-15: never snap the action log back while the user reads older rows -----------------

    [Fact]
    public async Task ActionLogScroll_GivenUserScrolledUp_ExpectedNoSnapToBottom()
    {
        await ViewerTestSupport.WithServerAsync("rv15", async server =>
        {
            var script = await GetTextAsync(server.Client, "/viewer/app.js");
            Assert.Contains("function isScrolledToBottom(container)", script);

            var append = FunctionBody(script, "function appendLiveActions(container, actions)");
            Assert.Contains("isScrolledToBottom(container)", append);
            Assert.Contains("if (atBottom)", append);
            Assert.DoesNotContain("container.scrollTop = container.scrollHeight", append);

            var render = FunctionBody(script, "function renderActions(container, actions, options)");
            Assert.Contains("if (settings.follow !== false && atBottom)", render);
        });
    }

    // --- helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts a one-hand match with a real pace, waits until at least one action has been shown,
    /// pauses it, and returns the frozen snapshot. A match that completed first is retried.
    /// </summary>
    private static async Task<(string Id, JsonElement Snapshot)> FreezeMidHandAsync(
        HttpClient client,
        ulong seed)
    {
        for (var attempt = 0; attempt < 4; attempt += 1)
        {
            var started = await ViewerTestSupport.StartLiveAsync(
                client, seed, 1, 250, "tight-aggressive", "random-legal");
            var id = started.GetProperty("id").GetString()!;
            try
            {
                await ViewerTestSupport.WaitForSnapshotAsync(
                    client,
                    id,
                    root => root.GetProperty("status").GetString() == "running"
                        && !root.GetProperty("isComplete").GetBoolean()
                        && root.GetProperty("actions").GetArrayLength() >= 1,
                    "a running hand with at least one action");
                using (var pause = await client.PostAsync($"/api/live/{id}/pause", null))
                {
                    Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
                }

                var frozen = await ViewerTestSupport.GetJsonAsync(client, $"/api/live/{id}");
                if (frozen.GetProperty("status").GetString() == "paused"
                    && !frozen.GetProperty("isComplete").GetBoolean())
                {
                    return (id, frozen);
                }
            }
            catch (XunitException)
            {
                // The paced hand can complete before the pause lands; retry with a fresh match.
            }

            await DeleteQuietlyAsync(client, id);
        }

        throw new XunitException("Could not freeze a running hand mid-action after four attempts.");
    }

    private static async Task DeleteQuietlyAsync(HttpClient client, string id)
    {
        try
        {
            using var response = await client.DeleteAsync($"/api/live/{id}");
        }
        catch (HttpRequestException)
        {
        }
    }

    private static async Task<string> GetTextAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Extracts a JavaScript function body (including braces) by signature.</summary>
    private static string FunctionBody(string script, string signature)
    {
        var start = script.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new XunitException($"Expected app.js to define '{signature}'.");
        }

        var open = script.IndexOf('{', start);
        if (open < 0)
        {
            throw new XunitException($"Expected an opening brace after '{signature}'.");
        }

        var depth = 0;
        for (var index = open; index < script.Length; index += 1)
        {
            switch (script[index])
            {
                case '{':
                    depth += 1;
                    break;
                case '}':
                    depth -= 1;
                    if (depth == 0)
                    {
                        return script.Substring(open, index - open + 1);
                    }

                    break;
            }
        }

        throw new XunitException($"Unbalanced braces after '{signature}'.");
    }

    private static string CreateRecordedSession(
        ViewerTestServer server,
        ulong seed,
        int hands,
        IPokerAgent seat0,
        IPokerAgent seat1)
    {
        // The first run computes the deterministic session id; the second writes the session
        // directory under the temporary data directory.
        var report = MatchSimulator.Run(seat0, seat1, new SimulationOptions(seed, hands));
        var directory = Path.Combine(server.DataDirectory, "sessions", report.SessionId);
        MatchSimulator.Run(seat0, seat1, new SimulationOptions(seed, hands, HistoryDirectory: directory));
        return report.SessionId;
    }

    /// <summary>Extracts one <c>@media (...)</c> block, including nested rules.</summary>
    private static string MediaBlock(string css, string condition)
    {
        var start = css.IndexOf("@media (" + condition + ")", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new XunitException($"Expected viewer.css to define '@media ({condition})'.");
        }

        return BracedBlock(css, start, $"@media ({condition})");
    }

    /// <summary>Extracts one CSS rule block by exact selector.</summary>
    private static string CssBlock(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        if (start < 0)
        {
            start = css.IndexOf(selector + "{", StringComparison.Ordinal);
        }

        if (start < 0)
        {
            throw new XunitException($"Expected viewer.css to define a rule for '{selector}'.");
        }

        return BracedBlock(css, start, selector);
    }

    private static string BracedBlock(string text, int startIndex, string description)
    {
        var open = text.IndexOf('{', startIndex);
        if (open < 0)
        {
            throw new XunitException($"Expected an opening brace for {description}.");
        }

        var depth = 0;
        for (var index = open; index < text.Length; index += 1)
        {
            switch (text[index])
            {
                case '{':
                    depth += 1;
                    break;
                case '}':
                    depth -= 1;
                    if (depth == 0)
                    {
                        return text.Substring(open, index - open + 1);
                    }

                    break;
            }
        }

        throw new XunitException($"Unbalanced braces for {description}.");
    }

    /// <summary>Returns the opening tag that carries <c>id="..."</c>.</summary>
    private static string Tag(string html, string id)
    {
        var marker = "id=\"" + id + "\"";
        var index = html.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new XunitException($"Expected the viewer markup to contain {marker}.");
        }

        var open = html.LastIndexOf('<', index);
        var close = html.IndexOf('>', index);
        if (open < 0 || close < 0)
        {
            throw new XunitException($"Could not read the element tag for {marker}.");
        }

        return html.Substring(open, close - open + 1);
    }

    private static string CssVariable(string css, string name)
    {
        var match = Regex.Match(
            css,
            Regex.Escape(name) + @"\s*:\s*(#[0-9a-fA-F]{6})\s*;",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new XunitException($"Expected viewer.css to define {name} as a hex colour.");
        }

        return match.Groups[1].Value;
    }

    private static string CssColor(string css, string rule, string property)
    {
        var match = Regex.Match(
            rule,
            @"(?<![-\w])" + Regex.Escape(property)
                + @"\s*:\s*(#[0-9a-fA-F]{6}|var\((--[a-z-]+)\))",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new XunitException($"Expected the CSS rule to declare '{property}: <hex|var>'.");
        }

        var value = match.Groups[1].Value;
        return value.StartsWith("var(", StringComparison.Ordinal)
            ? CssVariable(css, match.Groups[2].Value)
            : value;
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(character => new string(character, 2)));
        }

        var red = LinearChannel(value[..2]);
        var green = LinearChannel(value.Substring(2, 2));
        var blue = LinearChannel(value.Substring(4, 2));
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double LinearChannel(string hex)
    {
        var value = Convert.ToInt32(hex, 16) / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
