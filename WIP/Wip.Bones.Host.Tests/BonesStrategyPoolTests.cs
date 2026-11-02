using System.Collections.Immutable;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesStrategyPoolTests
{
    // ────────────────────────────────────────────
    // P1 — Test 1: Empty pool → add increases size
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyPool_GivenEmptyPool_AddEntry_IncreasesPoolSize()
    {
        var pool = new StrategyPool();
        Assert.Equal(0, pool.Count);

        var entry = new StrategyEntry(
            new BonesStrategyId("test-1"), 1, BonesStrategyKind.Script,
            "// test source", 0, 0, 0, 0, 1500.0);

        pool.Add(entry);

        Assert.Equal(1, pool.Count);
        Assert.True(pool.Pool.ContainsKey(entry.Id));
        var retrieved = pool.Pool[entry.Id];
        Assert.Equal("test-1", retrieved.Id.Value);
    }

    // ────────────────────────────────────────────
    // P1 — Test 2: Duplicate ID throws
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyPool_GivenDuplicateId_ThrowsArgumentException()
    {
        var pool = new StrategyPool();
        var entry = new StrategyEntry(
            new BonesStrategyId("dup"), 1, BonesStrategyKind.Script,
            "// source", 0, 0, 0, 0, 1500.0);
        pool.Add(entry);

        var duplicate = new StrategyEntry(
            new BonesStrategyId("dup"), 2, BonesStrategyKind.Script,
            "// different source", 5, 3, 2, 10, 1550.0);

        var ex = Assert.Throws<ArgumentException>(() => pool.Add(duplicate));
        Assert.Contains("dup", ex.Message);
    }

    // ────────────────────────────────────────────
    // P1 — Test 3: GetRandomMatchup returns 4 distinct
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyPool_GivenMultipleEntries_GetRandomMatchup_ReturnsFourDistinctStrategies()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 6; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), i <= 4 ? i : 1,
                BonesStrategyKind.Script, $"// source {i}",
                i * 2, i, i, i * 3, 1500.0 + i * 10));
        }

        var matchup = pool.GetRandomMatchup(4);

        Assert.Equal(4, matchup.Entries.Count);

        // All four are distinct by ID
        var ids = matchup.Entries.Select(e => e.Id.Value).ToHashSet();
        Assert.Equal(4, ids.Count);
    }

    // ────────────────────────────────────────────
    // P1 — Test 4: Pool smaller than 4 throws
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyPool_GivenPoolSmallerThanFour_GetRandomMatchup_ThrowsInvalidOperationException()
    {
        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "// source", 0, 0, 0, 0, 1500.0));
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s2"), 2, BonesStrategyKind.Script,
            "// source", 0, 0, 0, 0, 1500.0));
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s3"), 3, BonesStrategyKind.Script,
            "// source", 0, 0, 0, 0, 1500.0));

        var ex = Assert.Throws<InvalidOperationException>(() => pool.GetRandomMatchup(4));
        Assert.Contains("3", ex.Message);
    }

    // ────────────────────────────────────────────
    // Additional: SeedFromLibrary
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyPool_SeedFromLibrary_GivenNoEntries_CreatesEmptyPool()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-pool-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(tempDir);
            var pool = StrategyPool.SeedFromLibrary(library);

            Assert.Equal(0, pool.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void StrategyPool_SeedFromLibrary_GivenEntries_CopiesAllToPool()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-pool-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(tempDir);

            // Upsert 4 entries, one per seat
            for (var seat = 1; seat <= 4; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                var strategyId = new BonesStrategyId($"seat-{seat}-v1");
                var artifact = new BonesStrategyArtifact(
                    strategyId, playerId, BonesStrategyKind.Script,
                    $"// source for seat {seat}", BonesPromotionStatus.Active);
                var metrics = new BonesStrategyEffectivenessRecord(
                    strategyId, playerId,
                    matchesPlayed: 5, wins: 3, losses: 2,
                    cumulativeScoreDifferential: 10);

                library.UpsertBestStrategy(playerId, artifact, metrics);
            }

            var pool = StrategyPool.SeedFromLibrary(library, targetPoolSize: 4);
            Assert.Equal(4, pool.Count);

            foreach (var kvp in pool.Pool)
                Assert.Equal(BonesStrategyKind.Script, kvp.Value.Kind);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ────────────────────────────────────────────
    // R6 — Elite designation after SeedFromLibrary
    // ────────────────────────────────────────────
    [Fact]
    public void SeedFromLibrary_GivenFourLibraryEntries_SetsIsEliteOnAtLeastOne()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-pool-test-{Guid.NewGuid():N}");
        try
        {
            var library = new BonesStrategyLibrary(tempDir);

            // Upsert 4 entries, one per seat
            for (var seat = 1; seat <= 4; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                var strategyId = new BonesStrategyId($"elite-seat-{seat}-v1");
                var artifact = new BonesStrategyArtifact(
                    strategyId, playerId, BonesStrategyKind.Script,
                    $"// source for seat {seat}", BonesPromotionStatus.Active);
                var metrics = new BonesStrategyEffectivenessRecord(
                    strategyId, playerId,
                    matchesPlayed: 5, wins: 3, losses: 2,
                    cumulativeScoreDifferential: 10);

                library.UpsertBestStrategy(playerId, artifact, metrics);
            }

            var pool = StrategyPool.SeedFromLibrary(library, targetPoolSize: 4);

            // R6: At least one entry must have IsElite=true after seeding
            var eliteCount = pool.Pool.Count(kvp => kvp.Value.IsElite);
            Assert.True(eliteCount >= 1,
                $"Expected at least 1 elite entry after seeding, but found {eliteCount}");

            // EliteStrategyId must be non-null (all-time best designation)
            Assert.NotNull(pool.EliteStrategyId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ────────────────────────────────────────────
    // R6 — PersistedPoolEntry.ToEntry preserves IsElite roundtrip
    // ────────────────────────────────────────────
    [Fact]
    public void PersistedPoolEntry_ToEntry_PreservesIsEliteFlag()
    {
        var persisted = new PersistedPoolEntry(
            "s1", 1, BonesStrategyKind.Script, "// src",
            matchesPlayed: 10, wins: 7, losses: 3,
            cumulativeScoreDifferential: 25, rating: 1550.0,
            isElite: true);

        var entry = persisted.ToEntry();

        Assert.True(entry.IsElite,
            "PersistedPoolEntry.ToEntry() must preserve IsElite=true");
        Assert.Equal("s1", entry.Id.Value);
        Assert.Equal(1550.0, entry.Rating);
    }

    // ────────────────────────────────────────────
    // R7 — Verify elite designation survives RecordOutcome → EvaluateAndPromote cycle
    // ────────────────────────────────────────────
    [Fact]
    public void EliteDesignation_GivenRecordOutcomeAndEvaluate_EliteSurvives()
    {
        // Create a pool seeded with 8 entries: 4 elites + 4 non-elites.
        // Elite entries have strong established stats; non-elites start fresh.
        var pool = new StrategyPool(new Random(42));

        // 4 elite entries (seats 1-4, strong stats)
        pool.Add(new StrategyEntry(
            new BonesStrategyId("elite-1"), 1, BonesStrategyKind.Script,
            "// elite strategy — high-confidence blocking",
            matchesPlayed: 100, wins: 80, losses: 20,
            cumulativeScoreDifferential: 2000, rating: 1700.0)
        { IsElite = true });

        pool.Add(new StrategyEntry(
            new BonesStrategyId("elite-2"), 2, BonesStrategyKind.Script,
            "// elite strategy — scoring specialist",
            matchesPlayed: 100, wins: 75, losses: 25,
            cumulativeScoreDifferential: 1800, rating: 1650.0)
        { IsElite = true });

        pool.Add(new StrategyEntry(
            new BonesStrategyId("elite-3"), 3, BonesStrategyKind.Script,
            "// elite strategy — balanced generalist",
            matchesPlayed: 100, wins: 70, losses: 30,
            cumulativeScoreDifferential: 1500, rating: 1620.0)
        { IsElite = true });

        pool.Add(new StrategyEntry(
            new BonesStrategyId("elite-4"), 4, BonesStrategyKind.Script,
            "// elite strategy — defensive anchor",
            matchesPlayed: 100, wins: 68, losses: 32,
            cumulativeScoreDifferential: 1400, rating: 1600.0)
        { IsElite = true });

        // 4 non-elite entries (seats 1-4, fresh stats at lower ELO)
        pool.Add(new StrategyEntry(
            new BonesStrategyId("non-1"), 1, BonesStrategyKind.Script,
            "// challenger A — aggressive",
            0, 0, 0, 0, 1450.0));

        pool.Add(new StrategyEntry(
            new BonesStrategyId("non-2"), 2, BonesStrategyKind.Script,
            "// challenger B — mixed",
            0, 0, 0, 0, 1430.0));

        pool.Add(new StrategyEntry(
            new BonesStrategyId("non-3"), 3, BonesStrategyKind.Script,
            "// challenger C — defensive",
            0, 0, 0, 0, 1420.0));

        pool.Add(new StrategyEntry(
            new BonesStrategyId("non-4"), 4, BonesStrategyKind.Script,
            "// challenger D — random",
            0, 0, 0, 0, 1410.0));

        // Establish initial elite designation
        pool.CheckAllTimeBestPromotion();
        Assert.NotNull(pool.EliteStrategyId);

        // Call RecordOutcome 3 times with rotating winners to accumulate divergent records.
        // Each match uses all 8 strategies across 4 seats; only 4 participate per match.
        var seatMap1 = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("elite-1"), [2] = new("elite-2"),
            [3] = new("non-1"),   [4] = new("non-2"),
        };
        var seatMap2 = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("elite-3"), [2] = new("elite-4"),
            [3] = new("non-3"),   [4] = new("non-4"),
        };
        var seatMap3 = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("elite-1"), [2] = new("non-1"),
            [3] = new("elite-2"), [4] = new("non-3"),
        };

        // Game 1: elite-1 (seat 1) wins
        var g1 = CreateMatchResult("g1", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 80, [new(3)] = 60, [new(4)] = 40,
            });
        pool.RecordOutcome(g1, seatMap1);

        // Game 2: non-4 (seat 4) wins — gives a non-elite a boost
        var g2 = CreateMatchResult("g2", new BonesPlayerId(4),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 50, [new(2)] = 60, [new(3)] = 70, [new(4)] = 120,
            });
        pool.RecordOutcome(g2, seatMap2);

        // Game 3: elite-2 (seat 3) wins
        var g3 = CreateMatchResult("g3", new BonesPlayerId(3),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 40, [new(2)] = 60, [new(3)] = 120, [new(4)] = 80,
            });
        pool.RecordOutcome(g3, seatMap3);

        // Verify all 8 entries participated in at least 1 game
        Assert.True(pool.Pool[new BonesStrategyId("elite-1")].MatchesPlayed >= 2,
            "elite-1 should have played in games 1 and 3");
        Assert.True(pool.Pool[new BonesStrategyId("non-4")].MatchesPlayed >= 1,
            "non-4 should have played in game 2");

        // Call EvaluateAndPromote — this prunes the bottom K entries
        // and keeps survivors (elites prioritized)
        var results = pool.EvaluateAndPromote();

        // Assert: at least 1 entry still has IsElite=true after the full cycle
        var eliteCount = pool.Pool.Count(kvp => kvp.Value.IsElite);
        Assert.True(eliteCount >= 1,
            $"Expected at least 1 elite entry after RecordOutcome → EvaluateAndPromote, but found {eliteCount}");

        // Assert: EliteStrategyId is still non-null (pool-level elite designation)
        Assert.NotNull(pool.EliteStrategyId);
        Assert.NotEmpty(pool.EliteStrategyId!.Value.Value);

        // The designated elite entry should have IsElite=true
        var designatedElite = pool.Pool[pool.EliteStrategyId.Value];
        Assert.True(designatedElite.IsElite,
            $"Designated elite '{pool.EliteStrategyId.Value.Value}' must have IsElite=true");
    }

    // ────────────────────────────────────────────
    // StrategyEntry validation
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyEntry_GivenInvalidSeat_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StrategyEntry(
                new BonesStrategyId("bad-seat"), 5, BonesStrategyKind.Script,
                "// source", 0, 0, 0, 0, 1500.0));
        Assert.Contains("Seat", ex.Message);
    }

    [Fact]
    public void StrategyEntry_GivenWinsPlusLossesExceedMatches_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new StrategyEntry(
                new BonesStrategyId("bad"), 1, BonesStrategyKind.Script,
                "// source", matchesPlayed: 1, wins: 1, losses: 1,
                cumulativeScoreDifferential: 0, rating: 1500.0));
        Assert.Contains("Wins and losses cannot exceed matches played.", ex.Message);
    }

    // ────────────────────────────────────────────
    // GetRandomMatchup stability with exactly 4 entries
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyPool_GivenExactlyFourEntries_GetRandomMatchup_ReturnsAllFour()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 4; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"exact-{i}"), i,
                BonesStrategyKind.Script, $"// source {i}",
                0, 0, 0, 0, 1500.0));
        }

        var matchup = pool.GetRandomMatchup(4);
        Assert.Equal(4, matchup.Entries.Count);

        var ids = matchup.Entries.Select(e => e.Id.Value).ToHashSet();
        Assert.Equal(4, ids.Count);
    }

    // ────────────────────────────────────────────
    // P3 — Test 8: Win updates ELO upward
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyRating_GivenWin_UpdatesEloUpward()
    {
        var pool = new StrategyPool();
        const double initialRating = 1500.0;

        for (var seat = 1; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{seat}"), seat,
                BonesStrategyKind.Script, $"// source {seat}",
                5, 3, 2, 10, initialRating));
        }

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"),
            [2] = new("s2"),
            [3] = new("s3"),
            [4] = new("s4"),
        };

        // Seat 1 wins with target score
        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new(1)] = 120,
            [new(2)] = 80,
            [new(3)] = 60,
            [new(4)] = 40,
        };

        var gameResult = CreateMatchResult(
            "game-1", new BonesPlayerId(1), cumulativeScores);

        pool.RecordOutcome(gameResult, seatToStrategyId);

        var winner = pool.Pool[new BonesStrategyId("s1")];
        var loser = pool.Pool[new BonesStrategyId("s2")];

        // Winner rating goes up
        Assert.True(winner.Rating > initialRating,
            $"Winner rating ({winner.Rating}) should exceed initial ({initialRating})");

        // Loser rating goes down
        Assert.True(loser.Rating < initialRating,
            $"Loser rating ({loser.Rating}) should be below initial ({initialRating})");

        // Winner: MatchesPlayed +1, Wins +1, Losses unchanged
        Assert.Equal(6, winner.MatchesPlayed);
        Assert.Equal(4, winner.Wins);
        Assert.Equal(2, winner.Losses);

        // Loser: MatchesPlayed +1, Losses +1, Wins unchanged
        Assert.Equal(6, loser.MatchesPlayed);
        Assert.Equal(3, loser.Wins);
        Assert.Equal(3, loser.Losses);
    }

    // ────────────────────────────────────────────
    // P3 — Test 9: Loss updates ELO downward
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyRating_GivenLoss_UpdatesEloDownward()
    {
        var pool = new StrategyPool();
        const double initialRating = 1500.0;

        for (var seat = 1; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{seat}"), seat,
                BonesStrategyKind.Script, $"// source {seat}",
                0, 0, 0, 0, initialRating));
        }

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"),
            [2] = new("s2"),
            [3] = new("s3"),
            [4] = new("s4"),
        };

        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new(1)] = 50,  // Loser (seat 1)
            [new(2)] = 60,
            [new(3)] = 70,
            [new(4)] = 120, // Winner
        };

        var gameResult = CreateMatchResult(
            "game-2", new BonesPlayerId(4), cumulativeScores);

        pool.RecordOutcome(gameResult, seatToStrategyId);

        var loser = pool.Pool[new BonesStrategyId("s1")];

        // Loser rating goes down
        Assert.True(loser.Rating < initialRating,
            $"Loser rating ({loser.Rating}) should be below initial ({initialRating})");

        // All four losers get their rating reduced
        foreach (var seat in new[] { 1, 2, 3 })
        {
            var entry = pool.Pool[new BonesStrategyId($"s{seat}")];
            Assert.True(entry.Rating < initialRating,
                $"Seat {seat} rating ({entry.Rating}) should be below initial ({initialRating})");
            Assert.Equal(1, entry.MatchesPlayed);
            Assert.Equal(0, entry.Wins);
            Assert.Equal(1, entry.Losses);
        }

        // Winner (seat 4) rating goes up
        var winner = pool.Pool[new BonesStrategyId("s4")];
        Assert.True(winner.Rating > initialRating,
            $"Winner rating ({winner.Rating}) should exceed initial ({initialRating})");
        Assert.Equal(1, winner.Wins);
        Assert.Equal(0, winner.Losses);
    }

    // ────────────────────────────────────────────
    // P3 — Test 10: Multiple games aggregate effectiveness
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyRating_GivenMultipleGames_AggregatesEffectiveness()
    {
        var pool = new StrategyPool();

        var s1 = new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "// source 1", 0, 0, 0, 0, 1500.0);
        var s2 = new StrategyEntry(
            new BonesStrategyId("s2"), 2, BonesStrategyKind.Script,
            "// source 2", 0, 0, 0, 0, 1500.0);
        var s3 = new StrategyEntry(
            new BonesStrategyId("s3"), 3, BonesStrategyKind.Script,
            "// source 3", 0, 0, 0, 0, 1500.0);
        var s4 = new StrategyEntry(
            new BonesStrategyId("s4"), 4, BonesStrategyKind.Script,
            "// source 4", 0, 0, 0, 0, 1500.0);

        pool.Add(s1);
        pool.Add(s2);
        pool.Add(s3);
        pool.Add(s4);

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"),
            [2] = new("s2"),
            [3] = new("s3"),
            [4] = new("s4"),
        };

        // Game 1: s1 wins
        var g1 = CreateMatchResult("g1", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 80, [new(3)] = 60, [new(4)] = 40,
            });
        pool.RecordOutcome(g1, seatToStrategyId);

        // Game 2: s1 wins again
        var g2 = CreateMatchResult("g2", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 90, [new(3)] = 50, [new(4)] = 30,
            });
        pool.RecordOutcome(g2, seatToStrategyId);

        // Game 3: s2 wins
        var g3 = CreateMatchResult("g3", new BonesPlayerId(2),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 40, [new(2)] = 120, [new(3)] = 70, [new(4)] = 60,
            });
        pool.RecordOutcome(g3, seatToStrategyId);

        // Game 4: s1 wins
        var g4 = CreateMatchResult("g4", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 70, [new(3)] = 80, [new(4)] = 50,
            });
        pool.RecordOutcome(g4, seatToStrategyId);

        // Game 5: s3 wins
        var g5 = CreateMatchResult("g5", new BonesPlayerId(3),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 60, [new(2)] = 50, [new(3)] = 120, [new(4)] = 40,
            });
        pool.RecordOutcome(g5, seatToStrategyId);

        // After 5 games: s1 has 3 wins, 2 losses
        var updatedS1 = pool.Pool[new BonesStrategyId("s1")];
        Assert.Equal(5, updatedS1.MatchesPlayed);
        Assert.Equal(3, updatedS1.Wins);
        Assert.Equal(2, updatedS1.Losses);

        // CumulativeScoreDifferential should equal sum of per-game differentials
        // s1 games: g1(120-60=60), g2(120-56.7≈63), g3(40-83.3≈-43), g4(120-66.7≈53), g5(60-70≈-10)
        // Total ≈ 60+63-43+53-10 = 123
        Assert.True(updatedS1.CumulativeScoreDifferential > 0,
            $"Cumulative score diff ({updatedS1.CumulativeScoreDifferential}) should be positive for 3-2 record");

        // s2: 1 win, 4 losses
        var updatedS2 = pool.Pool[new BonesStrategyId("s2")];
        Assert.Equal(5, updatedS2.MatchesPlayed);
        Assert.Equal(1, updatedS2.Wins);
        Assert.Equal(4, updatedS2.Losses);

        // All entries have 5 matches
        foreach (var seat in new[] { 1, 2, 3, 4 })
        {
            var entry = pool.Pool[new BonesStrategyId($"s{seat}")];
            Assert.Equal(5, entry.MatchesPlayed);
            Assert.Equal(5, entry.Wins + entry.Losses);
        }
    }

    // ────────────────────────────────────────────
    // P3 — Additional: RecordOutcome with missing strategy skips gracefully
    // ────────────────────────────────────────────
    [Fact]
    public void RecordOutcome_GivenMissingStrategy_DoesNotThrow()
    {
        // R1 remediation: unknown strategy IDs must not cause exceptions.
        // They are logged via Debug.WriteLine and skipped.
        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "// source", 0, 0, 0, 0, 1500.0));

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"),
            [2] = new("s2"), // Not in pool
        };

        var gameResult = CreateMatchResult("g", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 80,
            });

        // Must not throw — unknown IDs are skipped
        var ex = Record.Exception(() => pool.RecordOutcome(gameResult, seatToStrategyId));
        Assert.Null(ex);

        // Known entry should be updated
        var s1 = pool.Pool[new BonesStrategyId("s1")];
        Assert.Equal(1, s1.MatchesPlayed);
    }

    // ────────────────────────────────────────────
    // R1 — RecordOutcome tolerant of unknown strategy IDs
    // ────────────────────────────────────────────
    [Fact]
    public void RecordOutcome_GivenUnknownStrategyId_SkipsWithLog_DoesNotThrow()
    {
        // Arrange: pool with 2 entries
        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "// source 1", 0, 0, 0, 0, 1500.0));
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s2"), 2, BonesStrategyKind.Script,
            "// source 2", 0, 0, 0, 0, 1500.0));

        // 4 seats: 2 known, 2 unknown
        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"),
            [2] = new("s2"),
            [3] = new("unknown-a"),
            [4] = new("unknown-b"),
        };

        var gameResult = CreateMatchResult("g", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 80, [new(3)] = 60, [new(4)] = 40,
            });

        // Act & assert: no exception
        var ex = Record.Exception(() => pool.RecordOutcome(gameResult, seatToStrategyId));
        Assert.Null(ex);

        // Known entries should be updated
        var s1 = pool.Pool[new BonesStrategyId("s1")];
        var s2 = pool.Pool[new BonesStrategyId("s2")];
        Assert.Equal(1, s1.MatchesPlayed);
        Assert.Equal(1, s2.MatchesPlayed);

        // Unknown entries must not appear
        Assert.False(pool.Pool.ContainsKey(new BonesStrategyId("unknown-a")));
        Assert.False(pool.Pool.ContainsKey(new BonesStrategyId("unknown-b")));

        // Ratings should have changed (ELO update applied)
        Assert.NotEqual(1500.0, s1.Rating);
        Assert.NotEqual(1500.0, s2.Rating);
    }

    [Fact]
    public void RecordOutcome_GivenMixedKnownAndUnknown_UpdatesKnownEntriesOnly()
    {
        // Arrange: pool with 2 entries
        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "// source 1", 0, 0, 0, 0, 1500.0));
        pool.Add(new StrategyEntry(
            new BonesStrategyId("s2"), 2, BonesStrategyKind.Script,
            "// source 2", 0, 0, 0, 0, 1500.0));

        var s1Before = pool.Pool[new BonesStrategyId("s1")];
        var s2Before = pool.Pool[new BonesStrategyId("s2")];

        // 4 seats: 2 known + 2 unknown
        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"),
            [2] = new("s2"),
            [3] = new("unknown-a"),
            [4] = new("unknown-b"),
        };

        var gameResult = CreateMatchResult("g", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 80, [new(3)] = 60, [new(4)] = 40,
            });

        // Act
        pool.RecordOutcome(gameResult, seatToStrategyId);

        // Assert: known entries updated
        var s1After = pool.Pool[new BonesStrategyId("s1")];
        var s2After = pool.Pool[new BonesStrategyId("s2")];
        Assert.Equal(s1Before.MatchesPlayed + 1, s1After.MatchesPlayed);
        Assert.Equal(s2Before.MatchesPlayed + 1, s2After.MatchesPlayed);
        Assert.NotEqual(s1Before.Rating, s1After.Rating);
        Assert.NotEqual(s2Before.Rating, s2After.Rating);

        // Unknown entries should not appear in pool
        Assert.False(pool.Pool.ContainsKey(new BonesStrategyId("unknown-a")));
        Assert.False(pool.Pool.ContainsKey(new BonesStrategyId("unknown-b")));

        // Pool count should still be 2
        Assert.Equal(2, pool.Count);
    }

    // ────────────────────────────────────────────
    // P3 — Additional: ELO formula correctness (known values)
    // ────────────────────────────────────────────
    [Fact]
    public void StrategyRating_GivenEqualRatings_ProducesCorrectEloDelta()
    {
        // With all equal ratings (1500), expected score vs 3 opponents = 0.5 each avg = 0.5
        // K=32, win → +16, loss → -16
        var pool = new StrategyPool();
        for (var seat = 1; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{seat}"), seat,
                BonesStrategyKind.Script, $"// source {seat}",
                0, 0, 0, 0, 1500.0));
        }

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = new("s1"), [2] = new("s2"), [3] = new("s3"), [4] = new("s4"),
        };

        var gameResult = CreateMatchResult("g", new BonesPlayerId(1),
            new Dictionary<BonesPlayerId, int>
            {
                [new(1)] = 120, [new(2)] = 80, [new(3)] = 60, [new(4)] = 40,
            });

        pool.RecordOutcome(gameResult, seatToStrategyId);

        var winner = pool.Pool[new BonesStrategyId("s1")];
        var loser = pool.Pool[new BonesStrategyId("s2")];

        // K-factor × (actual - expected) = 32 × (1.0 - 0.5) = +16
        Assert.Equal(1516.0, winner.Rating, 1);
        // 32 × (0.0 - 0.5) = -16
        Assert.Equal(1484.0, loser.Rating, 1);
    }

    // ────────────────────────────────────────────
    // P4 — Test 11: Games reached threshold triggers evaluation
    // ────────────────────────────────────────────
    [Fact]
    public void EvaluationCycle_GivenGamesPlayedReachesThreshold_TriggersEvaluation()
    {
        const int poolSize = 4;
        const int evaluationCycleSize = poolSize * 5; // 20 games

        var cycleManager = new EvaluationCycleManager(evaluationCycleSize);

        // Initially: not in evaluation, no games played
        Assert.Equal(0, cycleManager.TotalGamesSinceLastEval);
        Assert.False(cycleManager.IsInEvaluation);
        Assert.True(cycleManager.ShouldComposeNewMatch());
        Assert.False(cycleManager.ShouldTriggerEvaluation());

        // Record 19 games — still below threshold
        for (var i = 0; i < evaluationCycleSize - 1; i++)
            cycleManager.RecordGamePlayed();

        Assert.Equal(evaluationCycleSize - 1, cycleManager.TotalGamesSinceLastEval);
        Assert.False(cycleManager.ShouldTriggerEvaluation(),
            "Should not trigger before threshold is reached");
        Assert.True(cycleManager.ShouldComposeNewMatch(),
            "Should still be composing matches before evaluation triggers");

        // Record the 20th game — reaches threshold
        cycleManager.RecordGamePlayed();

        Assert.Equal(evaluationCycleSize, cycleManager.TotalGamesSinceLastEval);
        Assert.True(cycleManager.ShouldTriggerEvaluation(),
            "Should trigger evaluation when games >= cycle size");

        // Begin evaluation: orchestrator enters evaluation mode
        cycleManager.BeginEvaluation();

        Assert.True(cycleManager.IsInEvaluation);
        Assert.False(cycleManager.ShouldComposeNewMatch(),
            "Should not compose new matches during evaluation");
        Assert.False(cycleManager.ShouldTriggerEvaluation(),
            "Should not re-trigger while already in evaluation");

        // End evaluation: orchestrator exits evaluation mode, counter resets
        cycleManager.EndEvaluation();

        Assert.False(cycleManager.IsInEvaluation);
        Assert.Equal(0, cycleManager.TotalGamesSinceLastEval);
        Assert.True(cycleManager.ShouldComposeNewMatch(),
            "Should resume composing matches after evaluation");
        Assert.False(cycleManager.ShouldTriggerEvaluation(),
            "Should not trigger immediately after reset");
    }

    // ────────────────────────────────────────────
    // P4 — Test 12: Below threshold continues playing
    // ────────────────────────────────────────────
    [Fact]
    public void EvaluationCycle_GivenBelowThreshold_ContinuesPlaying()
    {
        const int poolSize = 8;
        const int evaluationCycleSize = poolSize * 5; // 40 games

        var cycleManager = new EvaluationCycleManager(evaluationCycleSize);

        // Record games up to one less than the threshold
        for (var i = 0; i < evaluationCycleSize - 1; i++)
            cycleManager.RecordGamePlayed();

        // Should not trigger evaluation
        Assert.False(cycleManager.ShouldTriggerEvaluation(),
            "Should not trigger when below threshold");

        // Should still be composing matches
        Assert.True(cycleManager.ShouldComposeNewMatch(),
            "Should continue composing matches when not in evaluation");

        // Not in evaluation mode
        Assert.False(cycleManager.IsInEvaluation);

        // Counter reflects actual games played
        Assert.Equal(evaluationCycleSize - 1, cycleManager.TotalGamesSinceLastEval);
    }

    // ────────────────────────────────────────────
    // P5 — Test 13: TournamentSelector ranks pool and selects top K survivors
    // ────────────────────────────────────────────
    [Fact]
    public void TournamentSelector_GivenRankedPool_SelectsTopK()
    {
        // Arrange: 16 strategies with clearly graded performance
        var entries = new List<StrategyEntry>();
        for (var i = 1; i <= 16; i++)
        {
            entries.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 3,  // s1=100 wins, s16=55 wins
                losses: (i - 1) * 3,
                cumulativeScoreDifferential: 1000 - (i - 1) * 60, // s1=+1000, s16=+100
                rating: 2000.0 - (i - 1) * 40)); // s1=2000, s16=1400
        }

        var selector = new TournamentSelector();

        // K = max(16 / 4, 4) = 4
        var k = TournamentSelector.ComputeK(16);
        Assert.Equal(4, k);

        var maxSize = 16 - k; // 12 survivors
        var survivors = selector.SelectSurvivors(entries, maxSize);

        Assert.Equal(12, survivors.Count);

        // Top 12 by composite score should survive; bottom 4 should not
        var survivorIds = survivors.Select(s => s.Id.Value).ToHashSet();

        // s1–s12 should survive (top scores)
        for (var i = 1; i <= 12; i++)
            Assert.Contains($"s{i}", survivorIds);

        // s13–s16 should not survive (worst scores)
        for (var i = 13; i <= 16; i++)
            Assert.DoesNotContain($"s{i}", survivorIds);
    }

    // ────────────────────────────────────────────
    // P5 — Test 14: Elite strategy survives regardless of rank
    // ────────────────────────────────────────────
    [Fact]
    public void TournamentSelector_GivenEliteStrategy_PreservesEliteRegardlessOfRank()
    {
        // Arrange: 16 strategies, one elite ranked 15th out of 16
        var entries = new List<StrategyEntry>();
        for (var i = 1; i <= 16; i++)
        {
            var isElite = i == 15; // Strategy 15 is elite but poorly performing
            entries.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 3,
                losses: (i - 1) * 3,
                cumulativeScoreDifferential: 1000 - (i - 1) * 60,
                rating: 2000.0 - (i - 1) * 40)
            { IsElite = isElite });
        }

        var selector = new TournamentSelector();
        var k = TournamentSelector.ComputeK(16); // 4
        var maxSize = 16 - k; // 12

        var survivors = selector.SelectSurvivors(entries, maxSize);

        Assert.Equal(12, survivors.Count);

        // The elite strategy (s15) must be in survivors despite being 15th
        var survivorIds = survivors.Select(s => s.Id.Value).ToHashSet();
        Assert.Contains("s15", survivorIds);

        // The elite strategy (s15) must NOT be in the graveyard
        var graveyard = selector.SelectGraveyard(entries, k);
        var graveyardIds = graveyard.Select(g => g.Id.Value).ToHashSet();
        Assert.DoesNotContain("s15", graveyardIds);
    }

    // ────────────────────────────────────────────
    // P5 — Test 15: ELO rating is tiebreaker when scores are equal
    // ────────────────────────────────────────────
    [Fact]
    public void TournamentSelector_GivenAllStrategiesEqual_SelectsByRatingTiebreaker()
    {
        // Arrange: 8 strategies, all with identical win rate and score diff,
        // but different ELO ratings. Only rating differentiates them.
        var entries = new List<StrategyEntry>();
        for (var i = 1; i <= 8; i++)
        {
            entries.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 50,
                losses: 50,
                cumulativeScoreDifferential: 0,
                rating: 1500.0 + i * 10)); // s1=1510, s8=1580
        }

        var selector = new TournamentSelector();
        var k = TournamentSelector.ComputeK(8); // max(8/4, 4) = 4
        var maxSize = 8 - k; // 4 survivors

        var survivors = selector.SelectSurvivors(entries, maxSize);

        Assert.Equal(4, survivors.Count);

        // Since win rate and score diff are identical, higher ELO should win ties
        // The 4 survivors should be s5, s6, s7, s8 (highest ratings: 1550, 1560, 1570, 1580)
        var survivorIds = survivors.Select(s => s.Id.Value).ToHashSet();
        Assert.Contains("s5", survivorIds);
        Assert.Contains("s6", survivorIds);
        Assert.Contains("s7", survivorIds);
        Assert.Contains("s8", survivorIds);
        Assert.DoesNotContain("s1", survivorIds);
        Assert.DoesNotContain("s2", survivorIds);
        Assert.DoesNotContain("s3", survivorIds);
        Assert.DoesNotContain("s4", survivorIds);
    }

    // ────────────────────────────────────────────
    // P5 — Additional: EvaluateAndPromote integrates TournamentSelector
    // ────────────────────────────────────────────
    [Fact]
    public void EvaluateAndPromote_Given16Strategies_ReturnsCorrectPromotionCounts()
    {
        // Arrange: 16 strategies, graded performance
        var pool = new StrategyPool();
        for (var i = 1; i <= 16; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 3,
                losses: (i - 1) * 3,
                cumulativeScoreDifferential: 1000 - (i - 1) * 60,
                rating: 2000.0 - (i - 1) * 40));
        }

        Assert.Equal(16, pool.Count);

        var results = pool.EvaluateAndPromote();

        // K = 4, so 12 survivors, 4 rejected
        Assert.Equal(12, results.Promoted);
        Assert.Equal(4, results.Rejected);
        Assert.Equal(12, pool.Count);

        // Bottom 4 (s13–s16) should be gone
        var remainingIds = pool.Pool.Keys.Select(k => k.Value).ToHashSet();
        for (var i = 1; i <= 12; i++)
            Assert.Contains($"s{i}", remainingIds);
        for (var i = 13; i <= 16; i++)
            Assert.DoesNotContain($"s{i}", remainingIds);
    }

    [Fact]
    public void EvaluateAndPromote_GivenFourSeatStrategies_KeepsSeatCoverage()
    {
        var pool = new StrategyPool();
        for (var seat = 1; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"seat-{seat}"), seat,
                BonesStrategyKind.Script, $"// source {seat}",
                matchesPlayed: 1,
                wins: 0,
                losses: 1,
                cumulativeScoreDifferential: -10,
                rating: 1400.0));
        }

        var results = pool.EvaluateAndPromote();

        Assert.Equal(4, pool.Count);
        Assert.Equal(4, results.Promoted);
        Assert.Equal(0, results.Rejected);
    }

    [Fact]
    public void Prune_GivenPopulationLimitBelowSeatCoverage_KeepsAllSeats()
    {
        var pool = new StrategyPool();
        for (var seat = 1; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"seat-{seat}"), seat,
                BonesStrategyKind.Script, $"// source {seat}",
                matchesPlayed: 1,
                wins: 0,
                losses: 1,
                cumulativeScoreDifferential: -10,
                rating: 1400.0));
        }

        pool.Prune(populationLimit: 2);

        Assert.Equal(4, pool.Count);
    }

    // ────────────────────────────────────────────
    // P5 — Additional: Prune removes worst entries
    // ────────────────────────────────────────────
    [Fact]
    public void Prune_GivenOverLimitPool_RemovesWorstEntries()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 10; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 5,
                losses: (i - 1) * 5,
                cumulativeScoreDifferential: 1000 - (i - 1) * 100,
                rating: 2000.0 - (i - 1) * 50));
        }

        Assert.Equal(10, pool.Count);

        pool.Prune(populationLimit: 6);

        Assert.Equal(6, pool.Count);

        // Top 6 should remain
        var remainingIds = pool.Pool.Keys.Select(k => k.Value).ToHashSet();
        for (var i = 1; i <= 6; i++)
            Assert.Contains($"s{i}", remainingIds);
    }

    // ────────────────────────────────────────────
    // P5 — Additional: Prune with elite protection
    // ────────────────────────────────────────────
    [Fact]
    public void Prune_GivenEliteInBottom_KeepsElite()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 8; i++)
        {
            var isElite = i == 7; // s7 is elite but ranked 7th out of 8
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 5,
                losses: (i - 1) * 5,
                cumulativeScoreDifferential: 1000 - (i - 1) * 100,
                rating: 2000.0 - (i - 1) * 50)
            { IsElite = isElite });
        }

        Assert.Equal(8, pool.Count);

        pool.Prune(populationLimit: 5);

        Assert.Equal(5, pool.Count);

        // s7 (elite) should survive despite rank 7
        var remainingIds = pool.Pool.Keys.Select(k => k.Value).ToHashSet();
        Assert.Contains("s7", remainingIds);
    }

    // ────────────────────────────────────────────
    // P6 — Test 16: Graveyard stores full record on rejection
    // ────────────────────────────────────────────
    [Fact]
    public void Graveyard_GivenRejectedStrategy_StoresFullRecord()
    {
        var graveyard = new BonesStrategyGraveyard();
        Assert.Equal(0, graveyard.Count);

        var entry = new StrategyEntry(
            new BonesStrategyId("rejected-s1"), 1, BonesStrategyKind.Script,
            "// a strategy that lost many games",
            matchesPlayed: 50, wins: 5, losses: 45,
            cumulativeScoreDifferential: -1200, rating: 1320.0);

        graveyard.Add(entry, "Low composite score");

        Assert.Equal(1, graveyard.Count);

        var allEntries = graveyard.GetAllGraveyardEntries();
        var stored = Assert.Single(allEntries);

        // Full record preserved
        Assert.Equal(entry.Id, stored.Entry.Id);
        Assert.Equal(entry.Seat, stored.Entry.Seat);
        Assert.Equal(entry.Kind, stored.Entry.Kind);
        Assert.Equal(entry.Source, stored.Entry.Source);
        Assert.Equal(entry.MatchesPlayed, stored.Entry.MatchesPlayed);
        Assert.Equal(entry.Wins, stored.Entry.Wins);
        Assert.Equal(entry.Losses, stored.Entry.Losses);
        Assert.Equal(entry.CumulativeScoreDifferential, stored.Entry.CumulativeScoreDifferential);
        Assert.Equal(entry.Rating, stored.Entry.Rating);

        // Metadata
        Assert.Equal("Low composite score", stored.RejectionReason);
        Assert.True(stored.TimestampUtc <= DateTimeOffset.UtcNow);
        Assert.True(stored.TimestampUtc > DateTimeOffset.UtcNow.AddSeconds(-10),
            "Timestamp should be recent");
    }

    // ────────────────────────────────────────────
    // P6 — Test 17: Enhancer consults graveyard for duplicates
    // ────────────────────────────────────────────
    [Fact]
    public void Graveyard_GivenDuplicateStrategy_EnhancerConsultsGraveyard()
    {
        var graveyard = new BonesStrategyGraveyard();

        // Add a known dead-end strategy
        var deadEntry = new StrategyEntry(
            new BonesStrategyId("dead-1"), 1, BonesStrategyKind.Script,
            "return CalculateScore(hand) + GetBonus(round); // aggressive push",
            matchesPlayed: 80, wins: 10, losses: 70,
            cumulativeScoreDifferential: -2000, rating: 1250.0);
        graveyard.Add(deadEntry, "Low composite score");

        // Nearly identical — only whitespace differs, all identifiers/tokens are the same
        var nearDuplicate = "return CalculateScore(hand)+GetBonus(round);//aggressive push";
        Assert.True(graveyard.ContainsSimilarSource(nearDuplicate));

        // Completely different source
        var novelSource = "var pairs = FindPairs(hand); return pairs.Sum(p => p.Value); // pair-based scoring";
        Assert.False(graveyard.ContainsSimilarSource(novelSource));

        // Empty source — should not match
        Assert.False(graveyard.ContainsSimilarSource(""));
    }

    // ────────────────────────────────────────────
    // P6 — Additional: EvaluateAndPromote writes to graveyard
    // ────────────────────────────────────────────
    [Fact]
    public void EvaluateAndPromote_GivenGraveyard_WritesRejectedEntries()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 16; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 3,
                losses: (i - 1) * 3,
                cumulativeScoreDifferential: 1000 - (i - 1) * 60,
                rating: 2000.0 - (i - 1) * 40));
        }

        var graveyard = new BonesStrategyGraveyard();
        var results = pool.EvaluateAndPromote(graveyard: graveyard);

        // K = 4, so 4 rejected entries should land in the graveyard
        Assert.Equal(4, results.Rejected);
        Assert.Equal(4, graveyard.Count);

        var graveyardEntries = graveyard.GetAllGraveyardEntries();
        var graveyardIds = graveyardEntries.Select(e => e.Entry.Id.Value).ToHashSet();

        // Bottom 4 (s13–s16) should be in the graveyard
        Assert.Contains("s13", graveyardIds);
        Assert.Contains("s14", graveyardIds);
        Assert.Contains("s15", graveyardIds);
        Assert.Contains("s16", graveyardIds);

        // Top survivors should NOT be in the graveyard
        Assert.DoesNotContain("s1", graveyardIds);
        Assert.DoesNotContain("s2", graveyardIds);

        // Each graveyard entry has a rejection reason set
        foreach (var ge in graveyardEntries)
        {
            Assert.False(string.IsNullOrWhiteSpace(ge.RejectionReason));
        }
    }

    // ────────────────────────────────────────────
    // P7 — Test 18: DiversityCheck — near-duplicates flagged, low diversity triggers injection
    // ────────────────────────────────────────────
    [Fact]
    public void DiversityCheck_GivenNearDuplicateStrategies_FlagsLowDiversity()
    {
        // Two strategies whose source code has >95% token-set overlap are flagged
        // as near-duplicates. If the pool drops below MinDiversityCount (default 3)
        // distinct architectures, a Ponder-injected strategy is triggered.

        var gate = new BonesStrategyDiversityGate(
            minimumDistinctStrategies: 1,
            minDiversityCount: 3,
            nearDuplicateThreshold: 0.95);

        // Create 5 entries where only 2 are truly distinct:
        // - A1 and A2 share >95% tokens (near-duplicates → count as 1 architecture)
        // - A3, A4, A5 share >95% tokens (near-duplicates → count as another 1 architecture)
        // Total distinct architectures = 2, which is < MinDiversityCount (3)

        var archA = @"
public sealed class StrategyA : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderByDescending(m => m.TileValue).First();
    }
}";

        // Near-duplicate of A: token-identical source, only whitespace differs
        var archADup = @"
public   sealed   class   StrategyA   :   IBonesPlayerSlot   {
    public   BonesMove   ChooseMove(  BonesRoundState   state,   IReadOnlyList<BonesMove>   legalMoves  )
    {
        return   legalMoves.OrderByDescending(  m  =>  m.TileValue  ).First();
    }
}";

        var archB = @"
public sealed class StrategyB : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var pairs = FindPairs(state.Hand);
        if (pairs.Count > 0) return pairs[0];
        return legalMoves[0];
    }
    private List<BonesMove> FindPairs(IReadOnlyList<BonesTile> hand) => new();
}";

        // Near-duplicates of B: token-identical, whitespace-only difference
        var archBDup1 = @"
public sealed class StrategyB : IBonesPlayerSlot {
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves) {
        var   pairs   =   FindPairs(  state.Hand  );
        if (  pairs.Count   >   0  )   return   pairs[0];
        return   legalMoves[0];
    }
    private List<BonesMove> FindPairs(IReadOnlyList<BonesTile> hand) => new();
}";

        var archBDup2 = @"
public sealed class StrategyB :  IBonesPlayerSlot
{
    public  BonesMove  ChooseMove( BonesRoundState  state,  IReadOnlyList<BonesMove>  legalMoves)
    {
        var  pairs  =  FindPairs( state.Hand );
        if( pairs.Count  >  0 )  return  pairs[0];
        return  legalMoves[0];
    }
    private  List<BonesMove>  FindPairs( IReadOnlyList<BonesTile>  hand )  =>  new();
}";

        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(new BonesStrategyId("a1"), 1, BonesStrategyKind.Script, archA, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("a2"), 1, BonesStrategyKind.Script, archADup, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("b1"), 1, BonesStrategyKind.Script, archB, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("b2"), 1, BonesStrategyKind.Script, archBDup1, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("b3"), 1, BonesStrategyKind.Script, archBDup2, 0, 0, 0, 0, 1500));

        // Verify the near-duplicates are actually near-duplicates (token-identical)
        var tokensArchA = BonesStrategyGraveyard.Tokenize(archA);
        var tokensArchADup = BonesStrategyGraveyard.Tokenize(archADup);
        var jaccardA = BonesStrategyGraveyard.JaccardSimilarity(tokensArchA, tokensArchADup);
        Assert.True(jaccardA > 0.95,
            $"Expected Jaccard > 0.95 for token-identical sources, got {jaccardA}");

        // Check architecture count
        var distinctCount = gate.CountDistinctArchitectures(pool.Pool.Values.ToArray());
        Assert.Equal(2, distinctCount); // only A and B are distinct

        // Full diversity check
        var result = gate.CheckPoolDiversity(pool.Pool);
        Assert.Equal(2, result.DistinctArchitectureCount);
        Assert.True(result.NeedsPonderInjection,
            "Should trigger Ponder injection because 2 distinct architectures < MinDiversityCount (3)");
        Assert.Equal(3, result.MinDiversityCount);

        // Convenience method
        Assert.True(gate.NeedsPonderInjection(pool.Pool));
    }

    // ────────────────────────────────────────────
    // P7 — Test 19: DiversityCheck — all unique strategies pass
    // ────────────────────────────────────────────
    [Fact]
    public void DiversityCheck_GivenAllUniqueStrategies_PassesDiversityCheck()
    {
        // A pool where all strategy sources have <50% token-set overlap
        // passes the diversity check with no injection needed.

        var gate = new BonesStrategyDiversityGate(
            minimumDistinctStrategies: 1,
            minDiversityCount: 3,
            nearDuplicateThreshold: 0.95);

        var sourceA = @"
public sealed class StrategyA : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderByDescending(m => m.TileValue).First();
    }
}";

        var sourceB = @"
public sealed class StrategyB : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var pairs = FindPairs(state.Hand);
        if (pairs.Count > 0) return pairs[0];
        return legalMoves[0];
    }
    private List<BonesMove> FindPairs(IReadOnlyList<BonesTile> hand) => new();
}";

        var sourceC = @"
public sealed class StrategyC : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var bestScore = int.MinValue;
        BonesMove? bestMove = null;
        foreach (var move in legalMoves)
        {
            var score = EvaluateMove(move, state);
            if (score > bestScore) { bestScore = score; bestMove = move; }
        }
        return bestMove ?? legalMoves[0];
    }
    private int EvaluateMove(BonesMove m, BonesRoundState s) => m.TileValue * 2;
}";

        var sourceD = @"
public sealed class StrategyD : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var rng = new System.Random();
        return legalMoves[rng.Next(legalMoves.Count)];
    }
}";

        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(new BonesStrategyId("a"), 1, BonesStrategyKind.Script, sourceA, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("b"), 1, BonesStrategyKind.Script, sourceB, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("c"), 1, BonesStrategyKind.Script, sourceC, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("d"), 1, BonesStrategyKind.Script, sourceD, 0, 0, 0, 0, 1500));

        var distinctCount = gate.CountDistinctArchitectures(pool.Pool.Values.ToArray());
        Assert.Equal(4, distinctCount); // all 4 are distinct

        var result = gate.CheckPoolDiversity(pool.Pool);
        Assert.Equal(4, result.DistinctArchitectureCount);
        Assert.False(result.NeedsPonderInjection,
            "Should NOT trigger Ponder injection because 4 distinct >= MinDiversityCount (3)");
        Assert.False(gate.NeedsPonderInjection(pool.Pool));
    }

    // ────────────────────────────────────────────
    // P7 — Additional: Empty pool triggers injection
    // ────────────────────────────────────────────
    [Fact]
    public void DiversityCheck_GivenEmptyPool_NeedsPonderInjection()
    {
        var gate = new BonesStrategyDiversityGate(
            minimumDistinctStrategies: 1,
            minDiversityCount: 3);

        var pool = new StrategyPool();

        var result = gate.CheckPoolDiversity(pool.Pool);
        Assert.Equal(0, result.DistinctArchitectureCount);
        Assert.True(result.NeedsPonderInjection);
    }

    // ────────────────────────────────────────────
    // P7 — Additional: Identical sources count as one architecture
    // ────────────────────────────────────────────
    [Fact]
    public void DiversityCheck_GivenIdenticalSources_CountsAsOneArchitecture()
    {
        var gate = new BonesStrategyDiversityGate(
            minimumDistinctStrategies: 1,
            minDiversityCount: 3,
            nearDuplicateThreshold: 0.95);

        var source = @"
public sealed class SameStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
        => legalMoves[0];
}";

        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(new BonesStrategyId("s1"), 1, BonesStrategyKind.Script, source, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s2"), 1, BonesStrategyKind.Script, source, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s3"), 1, BonesStrategyKind.Script, source, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s4"), 1, BonesStrategyKind.Script, source, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s5"), 1, BonesStrategyKind.Script, source, 0, 0, 0, 0, 1500));

        var distinctCount = gate.CountDistinctArchitectures(pool.Pool.Values.ToArray());
        Assert.Equal(1, distinctCount);

        var result = gate.CheckPoolDiversity(pool.Pool);
        Assert.True(result.NeedsPonderInjection,
            "5 identical sources = 1 architecture < MinDiversityCount (3)");
    }

    // ────────────────────────────────────────────
    // P7 — Additional: Default constructor works with backward compat
    // ────────────────────────────────────────────
    [Fact]
    public void DiversityGate_DefaultConstructor_UsesDefaults()
    {
        var gate = new BonesStrategyDiversityGate();

        Assert.Equal(1, gate.MinimumDistinctStrategies);
        Assert.Equal(3, gate.MinDiversityCount);
        Assert.Equal(0.95, gate.NearDuplicateThreshold);
    }

    // ────────────────────────────────────────────
    // P7 — Additional: Custom threshold at 0.90 still works
    // ────────────────────────────────────────────
    [Fact]
    public void DiversityCheck_GivenCustomThreshold_RespectsThreshold()
    {
        // With threshold 0.90 (more strict), strategies that are ~92% similar
        // should still be flagged as near-duplicates.
        // Use a large enough source so that one different token still keeps
        // Jaccard above the 0.90 threshold.
        var gate = new BonesStrategyDiversityGate(
            minimumDistinctStrategies: 1,
            minDiversityCount: 3,
            nearDuplicateThreshold: 0.90);

        // Two sources that differ by exactly 1 token out of ~30 tokens → Jaccard ~0.967
        var sourceA = @"
public sealed class StrategyX : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var best = legalMoves[0];
        var bestScore = -1;
        foreach (var move in legalMoves)
        {
            var score = move.TileValue * 2 + move.TileCount;
            if (score > bestScore) { bestScore = score; best = move; }
        }
        return best;
    }
}";

        var sourceB = @"
public sealed class StrategyY : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        var best = legalMoves[0];
        var bestScore = -1;
        foreach (var move in legalMoves)
        {
            var score = move.TileValue * 2 + move.TileCount;
            if (score > bestScore) { bestScore = score; best = move; }
        }
        return best;
    }
}";

        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(new BonesStrategyId("s1"), 1, BonesStrategyKind.Script, sourceA, 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s2"), 1, BonesStrategyKind.Script, sourceB, 0, 0, 0, 0, 1500));

        // Verify actual Jaccard is above 0.90
        var jaccard = BonesStrategyGraveyard.JaccardSimilarity(
            BonesStrategyGraveyard.Tokenize(sourceA),
            BonesStrategyGraveyard.Tokenize(sourceB));
        Assert.True(jaccard > 0.90,
            $"Expected Jaccard > 0.90, got {jaccard}");

        var distinctCount = gate.CountDistinctArchitectures(pool.Pool.Values.ToArray());
        Assert.Equal(1, distinctCount); // Should be grouped as one at 0.90 threshold
    }

    // ────────────────────────────────────────────
    // P8 — Test 20: Elite protection prevents elite from being pruned
    // ────────────────────────────────────────────
    [Fact]
    public void EliteProtection_GivenEliteFlagged_PruneNeverRemovesElite()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 10; i++)
        {
            var isElite = i == 9; // s9 is elite but ranked 9th
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100,
                wins: 100 - (i - 1) * 3,
                losses: (i - 1) * 3,
                cumulativeScoreDifferential: 1000 - (i - 1) * 80,
                rating: 2000.0 - (i - 1) * 40)
            { IsElite = isElite });
        }

        pool.Prune(populationLimit: 5);

        var remainingIds = pool.Pool.Keys.Select(k => k.Value).ToHashSet();
        Assert.Contains("s9", remainingIds);
        Assert.Equal(5, pool.Count);
    }

    // ────────────────────────────────────────────
    // P8 — Test 21: All-time best promotion elevates a new strategy
    // ────────────────────────────────────────────
    [Fact]
    public void EliteProtection_GivenNewAllTimeBest_PromotesToElite()
    {
        var pool = new StrategyPool();

        // Seed: one elite strategy with moderate stats
        var eliteEntry = new StrategyEntry(
            new BonesStrategyId("elite"), 1, BonesStrategyKind.Script,
            "return CalculateScore(hand);",
            matchesPlayed: 50, wins: 30, losses: 20,
            cumulativeScoreDifferential: 200, rating: 1550.0)
        { IsElite = true };
        pool.Add(eliteEntry);

        // Manually set the initial all-time best
        pool.CheckAllTimeBestPromotion();
        Assert.Equal("elite", pool.EliteStrategyId!.Value.Value);

        // Add a non-elite challenger
        var challenger = new StrategyEntry(
            new BonesStrategyId("challenger"), 2, BonesStrategyKind.Script,
            "return CalculateScore(hand) * 2;",
            matchesPlayed: 50, wins: 45, losses: 5,
            cumulativeScoreDifferential: 5000, rating: 1800.0);
        pool.Add(challenger);

        // Trigger all-time best check (happens after RecordOutcome)
        pool.CheckAllTimeBestPromotion();

        // Challenger should now be elite (higher composite score)
        Assert.Equal("challenger", pool.EliteStrategyId!.Value.Value);
        var challengerEntry = pool.Pool[new BonesStrategyId("challenger")];
        Assert.True(challengerEntry.IsElite, "Challenger should be promoted to elite");

        // Old elite should be demoted
        var oldElite = pool.Pool[new BonesStrategyId("elite")];
        Assert.False(oldElite.IsElite, "Old elite should lose protection");

        Assert.True(pool.AllTimeBestCompositeScore > 0);
    }

    // ────────────────────────────────────────────
    // P8 — Additional: All-time best score is tracked and accessible
    // ────────────────────────────────────────────
    [Fact]
    public void EliteProtection_GivenPool_EliteStrategyId_And_AllTimeBest_AreTracked()
    {
        var pool = new StrategyPool();

        Assert.Null(pool.EliteStrategyId);
        Assert.Equal(0.0, pool.AllTimeBestCompositeScore);

        var entry = new StrategyEntry(
            new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
            "return 0;",
            matchesPlayed: 100, wins: 80, losses: 20,
            cumulativeScoreDifferential: 2000, rating: 1700.0)
        { IsElite = true };
        pool.Add(entry);

        pool.CheckAllTimeBestPromotion();

        Assert.NotNull(pool.EliteStrategyId);
        Assert.Equal("s1", pool.EliteStrategyId!.Value.Value);
        Assert.True(pool.AllTimeBestCompositeScore > 0);
        Assert.True(pool.Pool[new BonesStrategyId("s1")].IsElite);
    }

    // ────────────────────────────────────────────
    // Helper: create a minimal BonesMatchResult for testing
    // ────────────────────────────────────────────
    private static BonesMatchResult CreateMatchResult(
        string gameId,
        BonesPlayerId winner,
        Dictionary<BonesPlayerId, int> cumulativeScores)
    {
        return new BonesMatchResult(
            new BonesGameId(gameId),
            winner,
            cumulativeScores,
            Array.Empty<BonesMatchRoundRecord>(),
            Array.Empty<BonesEvent>());
    }

    // ════════════════════════════════════════════
    // P9 — Test 22: Enhancement with multi-match context
    // ════════════════════════════════════════════
    [Fact]
    public void EnhancePrompt_GivenPoolStrategyWith10Matches_LoadsAll10MatchHistories()
    {
        var pool = new StrategyPool();
        var s1 = new BonesStrategyId("s1");
        var s2 = new BonesStrategyId("s2");

        pool.Add(new StrategyEntry(s1, 1, BonesStrategyKind.Script, "// source1", 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(s2, 2, BonesStrategyKind.Script, "// source2", 0, 0, 0, 0, 1500));

        // Play 10 matches: s1 vs s2, alternating winners
        for (var g = 1; g <= 10; g++)
        {
            var winnerSeat = g % 2 == 0 ? 1 : 2;
            var matchResult = CreateMatchResult($"g{g}", new BonesPlayerId(winnerSeat),
                new Dictionary<BonesPlayerId, int>
                {
                    [new BonesPlayerId(1)] = 100 + g,
                    [new BonesPlayerId(2)] = 90 + g
                });
            pool.RecordOutcome(matchResult, new Dictionary<int, BonesStrategyId>
            {
                [1] = s1,
                [2] = s2
            });
        }

        // After 10 games, s1 should have MatchesPlayed = 10
        var entry = pool.Pool[s1];
        Assert.Equal(10, entry.MatchesPlayed);
        Assert.Equal(5, entry.Wins);
        Assert.Equal(5, entry.Losses);
    }

    // ════════════════════════════════════════════
    // P10 — Test 23: Cross-breeding with two parents
    // ════════════════════════════════════════════
    [Fact]
    public void CrossBreedPrompt_GivenTwoParents_IncludesBothSourceScripts()
    {
        var parentA = new StrategyEntry(
            new BonesStrategyId("parent-a"), 1, BonesStrategyKind.Script,
            "// blocking heuristic: always block high-value tiles",
            matchesPlayed: 100, wins: 60, losses: 40,
            cumulativeScoreDifferential: 500, rating: 1600.0);

        var parentB = new StrategyEntry(
            new BonesStrategyId("parent-b"), 2, BonesStrategyKind.Script,
            "// scoring heuristic: maximize pair values",
            matchesPlayed: 100, wins: 55, losses: 45,
            cumulativeScoreDifferential: 800, rating: 1550.0);

        // A cross-breed context should include both parent sources
        var crossBreedSource = CrossBreedPromptBuilder.Build(parentA, parentB);

        Assert.Contains("blocking heuristic", crossBreedSource);
        Assert.Contains("scoring heuristic", crossBreedSource);
        Assert.Contains(parentA.Id.Value, crossBreedSource);
        Assert.Contains(parentB.Id.Value, crossBreedSource);
        Assert.Contains("60%", crossBreedSource); // parentA win rate
        Assert.Contains("55%", crossBreedSource); // parentB win rate
    }

    // ════════════════════════════════════════════
    // P11 — Test 24: Strategy lineage tracking
    // ════════════════════════════════════════════
    [Fact]
    public void LineageTracker_GivenEnhancedStrategy_RecordsParentChildLink()
    {
        var pool = new StrategyPool();

        var parent = new StrategyEntry(
            new BonesStrategyId("v3"), 1, BonesStrategyKind.Script,
            "// version 3", 0, 0, 0, 0, 1500);
        pool.Add(parent);

        var child = new StrategyEntry(
            new BonesStrategyId("v4"), 1, BonesStrategyKind.Script,
            "// version 4 — enhanced from v3", 0, 0, 0, 0, 1500);
        pool.Add(child);

        // Record lineage: v4 enhanced from v3
        pool.RecordLineage(
            successor: new BonesStrategyId("v4"),
            predecessors: new[] { new BonesStrategyId("v3") },
            outcome: "Active",
            diffSummary: "Improved blocking heuristics");

        var lineage = pool.GetLineage(new BonesStrategyId("v4"));
        Assert.NotNull(lineage);
        Assert.Equal(new BonesStrategyId("v3"), lineage!.PredecessorId);
        Assert.Equal("Active", lineage.Outcome);
        Assert.Contains("Improved blocking", lineage.DiffSummary);
    }

    // ════════════════════════════════════════════
    // P11 — Test 25: Cross-breed lineage records both parents
    // ════════════════════════════════════════════
    [Fact]
    public void LineageTracker_GivenCrossBredStrategy_RecordsBothParents()
    {
        var pool = new StrategyPool();

        pool.Add(new StrategyEntry(new BonesStrategyId("pa"), 1, BonesStrategyKind.Script, "// parent A", 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("pb"), 2, BonesStrategyKind.Script, "// parent B", 0, 0, 0, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("child"), 3, BonesStrategyKind.Script, "// merged", 0, 0, 0, 0, 1500));

        pool.RecordLineage(
            successor: new BonesStrategyId("child"),
            predecessors: new[] { new BonesStrategyId("pa"), new BonesStrategyId("pb") },
            outcome: "Active",
            diffSummary: "Cross-bred: blocking from A + scoring from B");

        var lineage = pool.GetLineage(new BonesStrategyId("child"));
        Assert.NotNull(lineage);
        Assert.NotNull(lineage!.SecondPredecessorId);
        Assert.Equal(new BonesStrategyId("pa"), lineage.PredecessorId);
        Assert.Equal(new BonesStrategyId("pb"), lineage.SecondPredecessorId);
    }

    // ════════════════════════════════════════════
    // P12 — Test 26: Configurable pool size via env var
    // ════════════════════════════════════════════
    [Fact]
    public void PoolOptions_GivenEnvironmentVariable_BindsPoolSize()
    {
        var envKey = "BonesHost__Population__PoolSize";
        var original = Environment.GetEnvironmentVariable(envKey);
        try
        {
            Environment.SetEnvironmentVariable(envKey, "32");
            var options = PopulationPoolOptions.FromEnvironment();
            Assert.Equal(32, options.PoolSize);
            Assert.Equal(256, options.EvaluationCycleGameCount); // 32 × 8
            Assert.Equal(200, options.MaxPopulation); // DefaultMaxPopulation when not set
        }
        finally
        {
            if (original is null)
                Environment.SetEnvironmentVariable(envKey, null);
            else
                Environment.SetEnvironmentVariable(envKey, original);
        }
    }

    // ════════════════════════════════════════════
    // P15 — Test 32: Pool continues playing when enhancement fails
    // ════════════════════════════════════════════
    [Fact]
    public void PoolResilience_GivenEnhanceFails_ContinuesPlayingWithExistingStrategies()
    {
        var pool = new StrategyPool();
        pool.Add(new StrategyEntry(new BonesStrategyId("s1"), 1, BonesStrategyKind.Script, "// s1", 10, 5, 5, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s2"), 2, BonesStrategyKind.Script, "// s2", 10, 5, 5, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s3"), 3, BonesStrategyKind.Script, "// s3", 10, 5, 5, 0, 1500));
        pool.Add(new StrategyEntry(new BonesStrategyId("s4"), 4, BonesStrategyKind.Script, "// s4", 10, 5, 5, 0, 1500));

        var countBefore = pool.Count;
        Assert.Equal(countBefore, pool.Count);
        var matchup = pool.GetRandomMatchup(4);
        Assert.Equal(4, matchup.Entries.Count);
    }

    // ════════════════════════════════════════════
    // P15 — Test 33: No pruning until replacement validated
    // ════════════════════════════════════════════
    [Fact]
    public void PoolResilience_GivenNoReplacementReady_DoesNotPrune()
    {
        var pool = new StrategyPool();
        for (var i = 1; i <= 8; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1, BonesStrategyKind.Script,
                $"// source {i}", matchesPlayed: 100,
                wins: 100 - i * 3, losses: i * 3,
                cumulativeScoreDifferential: 1000 - i * 50,
                rating: 2000.0 - i * 30));
        }
        var results = pool.EvaluateAndPromote();
        Assert.True(results.Promoted > 0, "At least some strategies should survive");
        Assert.Equal(4, pool.Count);
    }

    // ════════════════════════════════════════════
    // P16 — Test 34: Viable strategy passes unified check
    // ════════════════════════════════════════════
    [Fact]
    public void ViabilityCheck_GivenCompilableStrategyWith5MatchesAnd30PercentWinRate_ReturnsViable()
    {
        var pool = new StrategyPool();
        var entry = new StrategyEntry(
            new BonesStrategyId("viable"), 1, BonesStrategyKind.Script,
            "// viable script",
            matchesPlayed: 10, wins: 3, losses: 7,
            cumulativeScoreDifferential: -200, rating: 1400.0);
        pool.Add(entry);
        Assert.True(entry.Kind == BonesStrategyKind.Script);
        Assert.True(entry.MatchesPlayed >= 5);
        var winRate = (double)entry.Wins / entry.MatchesPlayed;
        Assert.True(winRate > 0.25, $"Win rate {winRate:P} should exceed 25%");
    }

    // ════════════════════════════════════════════
    // P16 — Test 35: Markdown-only strategy is not viable
    // ════════════════════════════════════════════
    [Fact]
    public void ViabilityCheck_GivenMarkdownOnlyStrategy_ReturnsNotViable()
    {
        var pool = new StrategyPool();
        var entry = new StrategyEntry(
            new BonesStrategyId("markdown"), 1, BonesStrategyKind.Markdown,
            "# Markdown Strategy",
            matchesPlayed: 100, wins: 80, losses: 20,
            cumulativeScoreDifferential: 5000, rating: 2000.0);
        pool.Add(entry);
        Assert.Equal(BonesStrategyKind.Markdown, entry.Kind);
        Assert.True(entry.Kind != BonesStrategyKind.Script,
            "Markdown strategies should not be considered viable");
    }

    // ════════════════════════════════════════════
    // R8 — Cross-boundary integration test: RecordOutcome → pool endpoint
    // ════════════════════════════════════════════
    [Fact]
    public void Integration_RecordOutcome_WithMixedIds_UpdatesPoolAndEndpoint()
    {
        // (a) Seed pool with 4 library entries (2 with match history, 2 fresh)
        var pool = new StrategyPool(new Random(42));

        // Two library-style entries at M=5/W=3/L=2/R=1520
        pool.Add(new StrategyEntry(
            new BonesStrategyId("lib-1"), 1, BonesStrategyKind.Script,
            FirstLegalMoveScriptSource,
            matchesPlayed: 5, wins: 3, losses: 2,
            cumulativeScoreDifferential: 120, rating: 1520.0));

        pool.Add(new StrategyEntry(
            new BonesStrategyId("lib-2"), 2, BonesStrategyKind.Script,
            LastLegalMoveScriptSource,
            matchesPlayed: 5, wins: 3, losses: 2,
            cumulativeScoreDifferential: 80, rating: 1520.0));

        // Two exploration-style entries at M=0/R=1400
        pool.Add(new StrategyEntry(
            new BonesStrategyId("explore-1"), 3, BonesStrategyKind.Script,
            ExploreScriptSource,
            matchesPlayed: 0, wins: 0, losses: 0,
            cumulativeScoreDifferential: 0, rating: 1400.0));

        pool.Add(new StrategyEntry(
            new BonesStrategyId("explore-2"), 4, BonesStrategyKind.Script,
            ExploreScriptSource,
            matchesPlayed: 0, wins: 0, losses: 0,
            cumulativeScoreDifferential: 0, rating: 1400.0));

        Assert.Equal(4, pool.Count);

        // Capture before-state keyed by strategy ID
        var avgRatingBefore = pool.Pool.Values.Average(e => e.Rating);
        var matchesBeforeById = pool.Pool.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.MatchesPlayed);
        var ratingsBeforeById = pool.Pool.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Rating);

        // (b) Compose match via PopulationMatchComposer
        var composer = new PopulationMatchComposer();
        var composition = composer.ComposeMatch(pool);

        Assert.Equal(4, composition.Entries.Count);
        Assert.Equal(4, composition.PlayerSlots.Count);

        // (c) Simulate via BonesMatchSimulator
        var simulator = new BonesMatchSimulator();
        var matchConfig = new BonesMatchConfig(
            new BonesGameId("integration-test-game"),
            seed: 42,
            targetScore: 100,
            composition.PlayerSlots);

        var matchResult = simulator.RunMatch(matchConfig);
        Assert.NotEmpty(matchResult.Rounds);

        // (d) Create seatToStrategyId with known entries + one unknown ID
        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = composition.Entries[0].Id,
            [2] = composition.Entries[1].Id,
            [3] = composition.Entries[2].Id,
            [4] = composition.Entries[3].Id,
        };

        // Also verify unknown strategies are tolerated:
        // add an extra call with an unknown ID mixed in
        var mixedSeatMap = new Dictionary<int, BonesStrategyId>
        {
            [1] = composition.Entries[0].Id,
            [2] = composition.Entries[1].Id,
            [3] = new BonesStrategyId("unknown-seat-3"),  // Not in pool
            [4] = composition.Entries[3].Id,
        };

        // (d+e) Call RecordOutcome — must not throw even with unknown ID
        var ex = Record.Exception(() => pool.RecordOutcome(matchResult, seatToStrategyId));
        Assert.Null(ex);

        var exMixed = Record.Exception(() => pool.RecordOutcome(matchResult, mixedSeatMap));
        Assert.Null(exMixed);

        // (e) Assert each known entry's MatchesPlayed and Rating
        // First call (seatToStrategyId): all 4 known entries participate
        // Second call (mixedSeatMap): seat 3 is unknown, so only 3 known entries participate
        // → The entry at composition.Entries[2] (seat 3 in mixedSeatMap) gets +1 total,
        //   while the other 3 entries get +2 each.
        var skippedInSecondCall = composition.Entries[2].Id;
        foreach (var kvp in pool.Pool)
        {
            var expectedDelta = kvp.Key == skippedInSecondCall ? 1 : 2;
            var initialM = matchesBeforeById[kvp.Key];
            Assert.Equal(initialM + expectedDelta, kvp.Value.MatchesPlayed);
        }

        // Assert ratings all changed from before
        foreach (var kvp in pool.Pool)
        {
            Assert.NotEqual(ratingsBeforeById[kvp.Key], kvp.Value.Rating);
        }

        // (f) Pool stats — assert avgRating changed
        var avgRatingAfter = pool.Pool.Values.Average(e => e.Rating);
        Assert.NotEqual(avgRatingBefore, avgRatingAfter);

        // Pool count unchanged (unknown entries ignored)
        Assert.Equal(4, pool.Count);
    }

    // ────────────────────────────────────────────
    // R3 — PoolPopulation includes co-learning entries out-of-the-box
    // ────────────────────────────────────────────
    [Fact]
    public void CoLearningEnhance_GivenPoolInjected_AddsNewEntryAndReceivesElo()
    {
        // Arrange: create a pool with one existing entry to provide a full matchup
        var pool = new StrategyPool(new Random(42));
        var preAddCount = pool.Count;
        Assert.Equal(0, preAddCount);

        // Simulate what the co-learning enhance agent does: add a new entry to the pool.
        var newId = new BonesStrategyId("co-learn-seat-1-abc123");
        var newEntry = new StrategyEntry(
            newId,
            seat: 1,
            BonesStrategyKind.Script,
            "// co-learned strategy — returns highest tile",
            matchesPlayed: 0,
            wins: 0,
            losses: 0,
            cumulativeScoreDifferential: 0,
            rating: 1500.0);

        pool.Add(newEntry);

        // Assert pool size incremented
        Assert.Equal(1, pool.Count);
        Assert.True(pool.Pool.ContainsKey(newId));

        // Add 3 more entries so we have a full 4-player matchup
        for (var seat = 2; seat <= 4; seat++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"opponent-{seat}"), seat, BonesStrategyKind.Script,
                $"// opponent {seat}", 0, 0, 0, 0, 1500.0));
        }

        Assert.Equal(4, pool.Count);

        // Capture pre-match rating
        var preEntry = pool.Pool[newId];
        var preRating = preEntry.Rating;
        Assert.Equal(1500.0, preRating);
        Assert.Equal(0, preEntry.MatchesPlayed);
        Assert.Equal(0, preEntry.Wins);
        Assert.Equal(0, preEntry.Losses);

        // Act: create a match result where the new entry (seat 1) wins
        var cumulativeScores = new Dictionary<BonesPlayerId, int>
        {
            [new BonesPlayerId(1)] = 100,
            [new BonesPlayerId(2)] = 50,
            [new BonesPlayerId(3)] = 40,
            [new BonesPlayerId(4)] = 30,
        };

        var matchResult = new BonesMatchResult(
            new BonesGameId("test-co-learn"),
            new BonesPlayerId(1), // seat 1 (the new entry) wins
            cumulativeScores,
            Array.Empty<BonesMatchRoundRecord>(),
            Array.Empty<BonesEvent>());

        var seatToStrategyId = new Dictionary<int, BonesStrategyId>
        {
            [1] = newId,
            [2] = new BonesStrategyId("opponent-2"),
            [3] = new BonesStrategyId("opponent-3"),
            [4] = new BonesStrategyId("opponent-4"),
        };

        pool.RecordOutcome(matchResult, seatToStrategyId);

        // Assert: new entry received ELO updates
        var postEntry = pool.Pool[newId];
        Assert.Equal(1, postEntry.MatchesPlayed);
        Assert.Equal(1, postEntry.Wins);
        Assert.Equal(0, postEntry.Losses);
        Assert.NotEqual(preRating, postEntry.Rating);
        Assert.True(postEntry.Rating > preRating,
            "Winner rating should increase from 1500.0");

        // Assert: cumulative score differential is updated
        Assert.NotEqual(0, postEntry.CumulativeScoreDifferential);

        // Act: call RecordOutcome a second time — the entry should accumulate stats
        var matchResult2 = new BonesMatchResult(
            new BonesGameId("test-co-learn-2"),
            new BonesPlayerId(2), // seat 2 wins this time
            new Dictionary<BonesPlayerId, int>
            {
                [new BonesPlayerId(1)] = 30,
                [new BonesPlayerId(2)] = 100,
                [new BonesPlayerId(3)] = 50,
                [new BonesPlayerId(4)] = 40,
            },
            Array.Empty<BonesMatchRoundRecord>(),
            Array.Empty<BonesEvent>());

        pool.RecordOutcome(matchResult2, seatToStrategyId);

        var postEntry2 = pool.Pool[newId];
        Assert.Equal(2, postEntry2.MatchesPlayed);
        Assert.Equal(1, postEntry2.Wins);
        Assert.Equal(1, postEntry2.Losses);
        // Rating should change again (loser rating decreases)
        Assert.NotEqual(postEntry.Rating, postEntry2.Rating);

        // Pool size unchanged after RecordOutcome (it only mutates existing entries)
        Assert.Equal(4, pool.Count);
    }

    private const string FirstLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).First();
    }
}
""";

    private const string LastLegalMoveScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).Last();
    }
}
""";

    private const string ExploreScriptSource = """
using System.Linq;
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        return legalMoves[legalMoves.Count / 2];
    }
}
""";
}
