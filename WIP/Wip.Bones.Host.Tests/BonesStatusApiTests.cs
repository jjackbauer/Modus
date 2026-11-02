using System.Linq;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesStatusApiTests
{
    private const string StatusControlApiItem = BonesHostRequirementsChecklistItems.StatusControlApi;

    [Fact]
    [Trait("ChecklistItem", StatusControlApiItem)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedReportsRunningStateAndIterationCount()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1 && response.IsRunning,
            TimeSpan.FromSeconds(120));

        Assert.True(status.IsRunning);
        Assert.True(status.IterationCount >= 1);
        Assert.False(string.IsNullOrWhiteSpace(status.CurrentStage));
    }

    [Fact]
    [Trait("ChecklistItem", StatusControlApiItem)]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.LiveMatchFeed)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedIncludesReachableViewerUrl()
    {
        await using var factory = new BonesHostWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => !string.IsNullOrWhiteSpace(response.ViewerUrl),
            TimeSpan.FromSeconds(120));

        Assert.False(string.IsNullOrWhiteSpace(status.ViewerUrl));

        var viewerPath = new Uri(status.ViewerUrl!).PathAndQuery;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        HttpStatusCode viewerStatus = HttpStatusCode.NotFound;
        while (DateTime.UtcNow < deadline)
        {
            viewerStatus = (await client.GetAsync(viewerPath)).StatusCode;
            if (viewerStatus == HttpStatusCode.OK)
                break;

            await Task.Delay(100);
        }

        Assert.Equal(HttpStatusCode.OK, viewerStatus);
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.StatusApiLearningPlayer)]
    public async Task BonesStatusApi_GivenRunningLoop_ExpectedReportsLearningPlayerBudgetAndResumeFields()
    {
        await using var factory = new BonesHostWebApplicationFactory(configureConfiguration: settings =>
        {
            settings["BonesHost:MaxGamesPerRun"] = "50";
            settings["BonesHost:ResumeFromBest"] = "false";
        });

        using var client = factory.CreateClient();
        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1
                && response.LearningPlayer is not null
                && response.GamesSimulated > 0,
            TimeSpan.FromSeconds(120));

        // Default iteration: observe (GameCount=1) + play (1) + promotion evaluation (5).
        Assert.True(status.GamesSimulated >= 2);
        Assert.Equal(50, status.MaxGamesPerRun);
        Assert.False(status.ResumeFromBest);
        Assert.NotNull(status.LearningPlayer);
        Assert.False(string.IsNullOrWhiteSpace(status.LearningPlayer!.ActiveStrategyId));
        Assert.NotNull(status.LearningPlayer.Effectiveness);
    }

    [Fact]
    [Trait("ChecklistItem", "StatusApiEffectivenessConsistency")]
    public async Task BonesStatusApi_GivenLearningPlayerActive_LearningPlayerEffectivenessMatchesLibraryEffectiveness()
    {
        // Pre-populate the library with a known entry for the learning seat (seat 1).
        // When ResumeFromBest=true, the bootstrapper copies the library entry as the
        // active strategy, so learningPlayer.effectiveness and libraryEffectiveness
        // both read from the same library source — they must be identical.
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        var libraryDir = Path.Combine(dataDirectory, ".bones", "strategy-library");
        Directory.CreateDirectory(libraryDir);
        var libraryJson = """
        {
          "strategyId": "library-seed-v1",
          "playerSeat": 1,
          "kind": "Script",
          "source": "public sealed class S : IBonesPlayerSlot { public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> legal) => legal[0]; }",
          "matchesPlayed": 12,
          "wins": 7,
          "losses": 5,
          "cumulativeScoreDifferential": 34,
          "lastUpdatedUtc": "2025-01-15T00:00:00Z"
        }
        """;
        File.WriteAllText(Path.Combine(libraryDir, "seat-1.json"), libraryJson);

        await using var factory = new BonesHostWebApplicationFactory(
            dataDirectory: dataDirectory,
            configureConfiguration: settings =>
            {
                settings["BonesHost:ResumeFromBest"] = "true";
            });

        using var client = factory.CreateClient();
        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.LearningPlayer is not null
                && response.LibraryEffectiveness is not null,
            TimeSpan.FromSeconds(120));

        // When the active strategy is bootstrapped from the library,
        // the strategy IDs and effectiveness metrics must match.
        Assert.NotNull(status.LearningPlayer);
        Assert.NotNull(status.LibraryEffectiveness);
        Assert.Equal(
            status.LearningPlayer!.ActiveStrategyId,
            status.LibraryEffectiveness!.StrategyId);

        var le = status.LearningPlayer.Effectiveness;
        var lib = status.LibraryEffectiveness;
        Assert.Equal(12, le.MatchesPlayed);
        Assert.Equal(7, le.Wins);
        Assert.Equal(5, le.Losses);
        Assert.Equal(34, le.CumulativeScoreDifferential);
        Assert.Equal(lib.MatchesPlayed, le.MatchesPlayed);
        Assert.Equal(lib.Wins, le.Wins);
        Assert.Equal(lib.Losses, le.Losses);
        Assert.Equal(lib.CumulativeScoreDifferential, le.CumulativeScoreDifferential);
    }

    [Fact]
    [Trait("ChecklistItem", "StatusApiWinRateConsistency")]
    public async Task BonesStatusApi_GivenLearningPlayerSeat_PerSeatStrategyWinRateMatchesLearningPlayerEffectiveness()
    {
        // Pre-populate library for seat 1 so both learningPlayer and perSeatStrategies
        // read from the same source. Win rate must match.
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();
        var libraryDir = Path.Combine(dataDirectory, ".bones", "strategy-library");
        Directory.CreateDirectory(libraryDir);
        var libraryJson = """
        {
          "strategyId": "lib-winrate-test",
          "playerSeat": 1,
          "kind": "Script",
          "source": "public sealed class S : IBonesPlayerSlot { public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> legal) => legal[0]; }",
          "matchesPlayed": 20,
          "wins": 13,
          "losses": 7,
          "cumulativeScoreDifferential": 55,
          "lastUpdatedUtc": "2025-01-15T00:00:00Z"
        }
        """;
        File.WriteAllText(Path.Combine(libraryDir, "seat-1.json"), libraryJson);

        await using var factory = new BonesHostWebApplicationFactory(
            dataDirectory: dataDirectory,
            configureConfiguration: settings =>
            {
                settings["BonesHost:ResumeFromBest"] = "true";
            });

        using var client = factory.CreateClient();
        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.LearningPlayer is not null
                && response.PerSeatStrategies is not null
                && response.PerSeatStrategies.Count > 0,
            TimeSpan.FromSeconds(120));

        Assert.NotNull(status.LearningPlayer);
        Assert.NotNull(status.PerSeatStrategies);

        var learningSeat = status.LearningPlayer!.Seat;
        var learningSeatStrategy = status.PerSeatStrategies!
            .FirstOrDefault(s => s.Seat == learningSeat);
        Assert.NotNull(learningSeatStrategy);

        var le = status.LearningPlayer.Effectiveness;
        var expectedWinRate = (double)le.Wins / le.MatchesPlayed;

        // Win rate must equal wins/matchesPlayed from the learning player's effectiveness
        // (both read from the same library source).
        Assert.Equal(20, le.MatchesPlayed);
        Assert.Equal(13, le.Wins);
        Assert.Equal(expectedWinRate, learningSeatStrategy!.WinRate, 0.001);
        Assert.Equal(le.MatchesPlayed, learningSeatStrategy.MatchesPlayed);
    }

    // ── Item A: actualGamesSimulated ───────────────────────────────────────
    /// <summary>
    /// FIX PROOF: The status response includes actualGamesSimulated matching gamesSimulated
    /// (both reflect RecordGames-only count after the earlier TryReserve double-counting fix).
    /// Uses raw JSON to avoid deserialization quirks with long positional records.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", "StatusApiActualGamesSimulated")]
    public async Task BonesStatusApi_GivenRunningLoop_ActualGamesSimulatedMatchesGamesSimulated()
    {
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings =>
            {
                settings["BonesHost:MaxGamesPerRun"] = "50";
            });

        using var client = factory.CreateClient();

        // Wait until the loop has produced actual game counts.
        await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1 && response.GamesSimulated > 0,
            TimeSpan.FromSeconds(120));

        // Use raw JSON for the actualGamesSimulated assertion.
        var rawResponse = await client.GetAsync("/api/bones/status");
        rawResponse.EnsureSuccessStatusCode();
        var json = await rawResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("actualGamesSimulated", out var actual));
        var gamesSimulated = root.GetProperty("gamesSimulated").GetInt32();
        var actualGamesSimulated = actual.GetInt32();
        var maxGamesPerRun = root.GetProperty("maxGamesPerRun").GetInt32();

        Assert.Equal(gamesSimulated, actualGamesSimulated);
        Assert.True(actualGamesSimulated > 0);
    }

    // ── Item B: perSeatLastPromotion includes timestampUtc ─────────────────
    /// <summary>
    /// FIX PROOF: Each perSeatLastPromotionOutcome entry contains a timestampUtc
    /// field as an ISO 8601 string reflecting when the promotion decision was recorded.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", "StatusApiPerSeatPromotionTimestamp")]
    public async Task BonesStatusApi_GivenPromotionOutcomes_PerSeatLastPromotionIncludesTimestamp()
    {
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings =>
            {
                settings["BonesHost:AllSeatsLearning"] = "true";
                settings["BonesHost:ParallelSessionCount"] = "4";
            });

        using var client = factory.CreateClient();
        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.PerSeatLastPromotionOutcome is not null
                && response.PerSeatLastPromotionOutcome.Count > 0,
            TimeSpan.FromSeconds(120));

        Assert.NotNull(status.PerSeatLastPromotionOutcome);
        Assert.NotEmpty(status.PerSeatLastPromotionOutcome);

        foreach (var entry in status.PerSeatLastPromotionOutcome)
        {
            Assert.True(entry.Seat > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.TimestampUtc));

            // Verify it is a valid ISO 8601 DateTimeOffset string.
            Assert.True(
                DateTimeOffset.TryParse(entry.TimestampUtc, out var parsed),
                $"timestampUtc '{entry.TimestampUtc}' is not a valid ISO 8601 date-time.");

            // Timestamp must be recent (within the last 10 minutes).
            var age = DateTimeOffset.UtcNow - parsed;
            Assert.True(age.TotalMinutes < 10,
                $"timestampUtc is {age.TotalMinutes:F1} minutes old, expected < 10.");
        }
    }

    // ── Item C: deepSeekRetriesUsed removed ────────────────────────────────
    /// <summary>
    /// FIX PROOF: The hardcoded deepSeekRetriesUsed=0 field is removed from the
    /// status response. A hardcoded zero is misleading when no retry counter exists.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", "StatusApiDeepSeekRetriesUsedRemoved")]
    public async Task BonesStatusApi_GivenStatusResponse_DeepSeekRetriesUsedRemoved()
    {
        await using var factory = new BonesHostWebApplicationFactory();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/bones/status");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        // The field must not exist in the response.
        Assert.False(
            document.RootElement.TryGetProperty("deepSeekRetriesUsed", out _),
            "deepSeekRetriesUsed must be absent from the status response.");
    }

    // ── Item D: strategiesPending count ────────────────────────────────────
    /// <summary>
    /// FIX PROOF: strategiesPending = strategiesCompiled - strategiesPromoted - strategiesRejected.
    /// Reflects strategies compiled but not yet promoted or rejected.
    /// Uses raw JSON to also assert strategiesCompiled field exists.
    /// </summary>
    [Fact]
    [Trait("ChecklistItem", "StatusApiStrategiesPending")]
    public async Task BonesStatusApi_GivenCompiledButUnevaluatedStrategies_StrategiesPendingMatchesExpected()
    {
        await using var factory = new BonesHostWebApplicationFactory(
            configureConfiguration: settings =>
            {
                settings["BonesHost:AllSeatsLearning"] = "true";
                settings["BonesHost:ParallelSessionCount"] = "4";
            });

        using var client = factory.CreateClient();
        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        // Use raw JSON to assert the relationship without needing StrategiesCompiled
        // on the typed response record (it is available but validate via raw parse too).
        var rawResponse = await client.GetAsync("/api/bones/status");
        rawResponse.EnsureSuccessStatusCode();
        var json = await rawResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var compiled = root.GetProperty("strategiesCompiled").GetInt32();
        var pending = root.GetProperty("strategiesPending").GetInt32();
        var promoted = root.GetProperty("strategiesPromoted").GetInt32();
        var rejected = root.GetProperty("strategiesRejected").GetInt32();

        // The arithmetic relationship holds across any combination of values.
        Assert.Equal(compiled - promoted - rejected, pending);
        Assert.True(compiled > 0, "strategiesCompiled must be > 0 after at least one iteration.");
    }
}
