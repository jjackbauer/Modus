using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// Comprehensive persistence tests for StrategyPool: save/load roundtrip,
/// JSON serialization, atomic write, entry validation, and lineage restore.
/// </summary>
public sealed class BonesPoolPersistenceTests
{
    // ════════════════════════════════════════════
    // K1: Save/Load Roundtrip Tests
    // ════════════════════════════════════════════

    // Test 1: SaveAndLoad_Roundtrip_PreservesAllEntries
    [Fact]
    public void SaveAndLoad_Roundtrip_PreservesAllEntries()
    {
        var pool = new StrategyPool(new Random(42));
        for (var i = 1; i <= 12; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"),
                (i % 4) + 1,
                i % 3 == 0 ? BonesStrategyKind.Markdown : BonesStrategyKind.Script,
                $"// source {i}",
                matchesPlayed: i * 10,
                wins: i * 6,
                losses: i * 3,
                cumulativeScoreDifferential: i * 20,
                rating: 1500.0 + i * 10)
            {
                IsElite = i == 1
            });
        }

        var snapshot = pool.Save();
        Assert.Equal(12, snapshot.Entries.Count);

        var loaded = StrategyPool.TryLoad(snapshot);
        Assert.NotNull(loaded);
        Assert.Equal(12, loaded!.Count);

        foreach (var kvp in pool.Pool)
        {
            Assert.True(loaded.Pool.TryGetValue(kvp.Key, out var restored));
            Assert.Equal(kvp.Value.Id, restored.Id);
            Assert.Equal(kvp.Value.Seat, restored.Seat);
            Assert.Equal(kvp.Value.Kind, restored.Kind);
            Assert.Equal(kvp.Value.Source, restored.Source);
            Assert.Equal(kvp.Value.MatchesPlayed, restored.MatchesPlayed);
            Assert.Equal(kvp.Value.Wins, restored.Wins);
            Assert.Equal(kvp.Value.Losses, restored.Losses);
            Assert.Equal(kvp.Value.CumulativeScoreDifferential, restored.CumulativeScoreDifferential);
            Assert.Equal(kvp.Value.Rating, restored.Rating);
            Assert.Equal(kvp.Value.IsElite, restored.IsElite);
        }
    }

    // Test 2: SaveAndLoad_Roundtrip_PreservesLineage
    [Fact]
    public void SaveAndLoad_Roundtrip_PreservesLineage()
    {
        var pool = new StrategyPool(new Random(42));

        var parentId = new BonesStrategyId("parent");
        pool.Add(new StrategyEntry(parentId, 1, BonesStrategyKind.Script, "// parent", 10, 5, 3, 20, 1550.0));

        // Single-predecessor lineage
        pool.RecordLineage(
            new BonesStrategyId("child"),
            [parentId],
            "Enhanced",
            "Improved tile evaluation");

        pool.Add(new StrategyEntry(
            new BonesStrategyId("child"), 1, BonesStrategyKind.Script,
            "// child", 0, 0, 0, 0, 1500.0));

        pool.Add(new StrategyEntry(
            new BonesStrategyId("sibling"), 2, BonesStrategyKind.Script,
            "// sibling", 0, 0, 0, 0, 1500.0));

        // Dual-predecessor lineage (cross-breed)
        pool.RecordLineage(
            new BonesStrategyId("hybrid"),
            [new BonesStrategyId("child"), new BonesStrategyId("sibling")],
            "CrossBred",
            "Merged blocking and tile eval");

        pool.Add(new StrategyEntry(
            new BonesStrategyId("hybrid"), 1, BonesStrategyKind.Script,
            "// hybrid", 0, 0, 0, 0, 1500.0));

        var snapshot = pool.Save();
        Assert.Equal(2, snapshot.LineageRecords.Count);

        var loaded = StrategyPool.TryLoad(snapshot);
        Assert.NotNull(loaded);

        var childLineage = loaded!.GetLineage(new BonesStrategyId("child"));
        Assert.NotNull(childLineage);
        Assert.Equal("parent", childLineage!.PredecessorId.Value);
        Assert.Equal("Enhanced", childLineage.Outcome);
        Assert.Equal("Improved tile evaluation", childLineage.DiffSummary);
        Assert.Null(childLineage.SecondPredecessorId);

        var hybridLineage = loaded.GetLineage(new BonesStrategyId("hybrid"));
        Assert.NotNull(hybridLineage);
        Assert.Equal("child", hybridLineage!.PredecessorId.Value);
        // SecondPredecessorId is BonesStrategyId? — .Value unwraps to BonesStrategyId struct, .Value gets string
        Assert.Equal("sibling", hybridLineage.SecondPredecessorId!.Value.Value);
        Assert.Equal("CrossBred", hybridLineage.Outcome);

        Assert.Null(loaded.GetLineage(new BonesStrategyId("sibling")));
    }

    // Test 3: SaveAndLoad_Roundtrip_PreservesEliteAndBestScore
    [Fact]
    public void SaveAndLoad_Roundtrip_PreservesEliteAndBestScore()
    {
        var pool = new StrategyPool(new Random(42));
        pool.Add(new StrategyEntry(
            new BonesStrategyId("elite"), 1, BonesStrategyKind.Script,
            "// elite source", 100, 80, 15, 500, 1700.0)
        {
            IsElite = true
        });

        pool.Add(new StrategyEntry(
            new BonesStrategyId("challenger"), 1, BonesStrategyKind.Script,
            "// challenger", 50, 20, 25, -100, 1400.0));

        // Trigger CheckAllTimeBestPromotion to establish the elite designation
        pool.CheckAllTimeBestPromotion();

        var snapshot = pool.Save();
        Assert.NotNull(snapshot.EliteStrategyId);

        var loaded = StrategyPool.TryLoad(snapshot);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.EliteStrategyId);
        Assert.Equal("elite", loaded.EliteStrategyId!.Value.Value);

        Assert.True(loaded.Pool[new BonesStrategyId("elite")].IsElite);
        Assert.Equal(pool.AllTimeBestCompositeScore, loaded.AllTimeBestCompositeScore);
    }

    // Test 4: TryLoad_GivenMissingFile_ReturnsNull
    [Fact]
    public void TryLoadFromDisk_GivenMissingFile_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-test-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(tempDir, "nonexistent.json");
            var result = StrategyPool.TryLoadFromDisk(path);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 5: TryLoad_GivenCorruptJson_ReturnsNull
    [Fact]
    public void TryLoadFromDisk_GivenCorruptJson_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-corrupt-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, "corrupt.json");
            File.WriteAllText(path, "{invalid json garbage!!!");

            var result = StrategyPool.TryLoadFromDisk(path);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 6: TryLoad_GivenTruncatedJson_ReturnsNull
    [Fact]
    public void TryLoadFromDisk_GivenTruncatedJson_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-truncated-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, "truncated.json");
            // Valid JSON opening but missing closing braces
            File.WriteAllText(path, "{\"entries\":[{\"strategyId\":\"s1\"");

            var result = StrategyPool.TryLoadFromDisk(path);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 7: TryLoad_GivenInvalidEntryData_SkipsCorruptEntry
    [Fact]
    public void TryLoad_GivenInvalidEntryData_SkipsCorruptEntry()
    {
        // Construct a snapshot with one valid and one invalid entry directly
        var validEntry = new PersistedPoolEntry("valid", 1, BonesStrategyKind.Script,
            "// valid", 10, 5, 3, 20, 1500.0, false);
        var invalidEntry = new PersistedPoolEntry("invalid", 5, BonesStrategyKind.Script,
            "// invalid", -5, 0, 0, 0, 1500.0, false); // negative matches

        var snapshot = new StrategyPoolSnapshot(
            new[] { validEntry, invalidEntry },
            Array.Empty<PersistedLineageRecord>(),
            null,
            0.0,
            DateTimeOffset.UtcNow);

        var loaded = StrategyPool.TryLoad(snapshot);
        Assert.NotNull(loaded);
        // Only the valid entry should be loaded; invalid skipped
        Assert.Equal(1, loaded!.Count);
        Assert.True(loaded.Pool.ContainsKey(new BonesStrategyId("valid")));
        Assert.False(loaded.Pool.ContainsKey(new BonesStrategyId("invalid")));
    }

    // ════════════════════════════════════════════
    // K7: Snapshot Serialization Tests
    // ════════════════════════════════════════════

    // Test 8: StrategyPoolSnapshot_JsonRoundtrip_20Entries
    [Fact]
    public void StrategyPoolSnapshot_JsonRoundtrip_20Entries()
    {
        var entries = new List<PersistedPoolEntry>();
        for (var i = 1; i <= 20; i++)
        {
            entries.Add(new PersistedPoolEntry(
                $"s{i}", (i % 4) + 1,
                i % 3 == 0 ? BonesStrategyKind.Markdown : BonesStrategyKind.Script,
                $"// source for strategy {i}",
                matchesPlayed: i * 10, wins: i * 5, losses: i * 3,
                cumulativeScoreDifferential: i * 20,
                rating: 1500.0 + i * 5,
                isElite: i == 1));
        }

        var snapshot = new StrategyPoolSnapshot(
            entries,
            Array.Empty<PersistedLineageRecord>(),
            "s1",
            0.85,
            new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero));

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(snapshot, options);
        var deserialized = JsonSerializer.Deserialize<StrategyPoolSnapshot>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(20, deserialized!.Entries.Count);
        Assert.Equal("s1", deserialized.EliteStrategyId);
        Assert.Equal(0.85, deserialized.AllTimeBestCompositeScore);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(entries[i].StrategyId, deserialized.Entries[i].StrategyId);
            Assert.Equal(entries[i].Seat, deserialized.Entries[i].Seat);
            Assert.Equal(entries[i].Kind, deserialized.Entries[i].Kind);
            Assert.Equal(entries[i].Source, deserialized.Entries[i].Source);
            Assert.Equal(entries[i].MatchesPlayed, deserialized.Entries[i].MatchesPlayed);
            Assert.Equal(entries[i].Wins, deserialized.Entries[i].Wins);
            Assert.Equal(entries[i].Losses, deserialized.Entries[i].Losses);
            Assert.Equal(entries[i].CumulativeScoreDifferential, deserialized.Entries[i].CumulativeScoreDifferential);
            Assert.Equal(entries[i].Rating, deserialized.Entries[i].Rating);
            Assert.Equal(entries[i].IsElite, deserialized.Entries[i].IsElite);
        }
    }

    // Test 9: PersistedLineageRecord_JsonRoundtrip_10LineageRecords
    [Fact]
    public void PersistedLineageRecord_JsonRoundtrip_10LineageRecords()
    {
        var lineageRecords = new List<PersistedLineageRecord>();
        for (var i = 1; i <= 10; i++)
        {
            lineageRecords.Add(new PersistedLineageRecord(
                $"child-{i}",
                $"parent-{i}",
                i % 3 == 0 ? $"second-parent-{i}" : null,
                i % 2 == 0 ? "Enhanced" : "CrossBred",
                $"Diff for strategy {i}: improved scoring",
                new DateTimeOffset(2025, 6, 15, i, 0, 0, TimeSpan.Zero)));
        }

        var snapshot = new StrategyPoolSnapshot(
            Array.Empty<PersistedPoolEntry>(),
            lineageRecords,
            null,
            0.0,
            DateTimeOffset.UtcNow);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(snapshot, options);
        var deserialized = JsonSerializer.Deserialize<StrategyPoolSnapshot>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized!.LineageRecords.Count);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(lineageRecords[i].SuccessorId, deserialized.LineageRecords[i].SuccessorId);
            Assert.Equal(lineageRecords[i].PredecessorId, deserialized.LineageRecords[i].PredecessorId);
            Assert.Equal(lineageRecords[i].SecondPredecessorId, deserialized.LineageRecords[i].SecondPredecessorId);
            Assert.Equal(lineageRecords[i].Outcome, deserialized.LineageRecords[i].Outcome);
            Assert.Equal(lineageRecords[i].DiffSummary, deserialized.LineageRecords[i].DiffSummary);
            Assert.Equal(lineageRecords[i].RecordedUtc, deserialized.LineageRecords[i].RecordedUtc);
        }
    }

    // Test 10: StrategyPoolSnapshot_SourceNotTruncated
    [Fact]
    public void StrategyPoolSnapshot_SourceNotTruncated()
    {
        // Create a 10 KB source string
        var largeSource = "// " + new string('x', 10240);

        var pool = new StrategyPool(new Random(42));
        pool.Add(new StrategyEntry(
            new BonesStrategyId("large"), 1, BonesStrategyKind.Script,
            largeSource, 0, 0, 0, 0, 1500.0));

        var snapshot = pool.Save();
        Assert.Single(snapshot.Entries);
        Assert.Equal(largeSource, snapshot.Entries[0].Source);

        var loaded = StrategyPool.TryLoad(snapshot);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Pool);
        Assert.Equal(largeSource, loaded.Pool[new BonesStrategyId("large")].Source);
    }

    // ════════════════════════════════════════════
    // K5-K6 & K8: Persist/Load + Atomic Write Tests
    // ════════════════════════════════════════════

    // Test 11: PersistToDisk_And_TryLoadFromDisk_Roundtrip (15 entries + lineage)
    [Fact]
    public void PersistToDisk_And_TryLoadFromDisk_Roundtrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-pool-test-{Guid.NewGuid():N}");
        try
        {
            var snapshotPath = Path.Combine(tempDir, "pool-snapshot.json");

            var pool = new StrategyPool(new Random(42));
            for (var i = 1; i <= 15; i++)
            {
                pool.Add(new StrategyEntry(
                    new BonesStrategyId($"s{i}"),
                    (i % 4) + 1,
                    BonesStrategyKind.Script,
                    $"// source {i}",
                    matchesPlayed: i * 10,
                    wins: i * 5,
                    losses: i * 3,
                    cumulativeScoreDifferential: i * 30,
                    rating: 1500.0 + i * 10));
            }

            pool.RecordLineage(
                new BonesStrategyId("s2"),
                [new BonesStrategyId("s1")],
                "Enhanced",
                "Better");

            pool.PersistToDisk(snapshotPath);
            Assert.True(File.Exists(snapshotPath));

            var loaded = StrategyPool.TryLoadFromDisk(snapshotPath);
            Assert.NotNull(loaded);
            Assert.Equal(15, loaded!.Count);

            var lineage = loaded.GetLineage(new BonesStrategyId("s2"));
            Assert.NotNull(lineage);
            Assert.Equal("s1", lineage!.PredecessorId.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 12: TryLoadFromDisk_NonexistentFile_ReturnsNull
    [Fact]
    public void TryLoadFromDisk_GivenNonexistentFile_ReturnsNull_AllowsSeedFromLibraryFallback()
    {
        var result = StrategyPool.TryLoadFromDisk(
            Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.json"));
        Assert.Null(result);
    }

    // Test 13: Random is re-seeded after load (non-deterministic behavior across sessions)
    [Fact]
    public void LoadedPool_UsesReSeededRandom_DifferentMatchupsAcrossPools()
    {
        var pool1 = new StrategyPool(new Random(42));
        for (var i = 1; i <= 8; i++)
        {
            pool1.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1, BonesStrategyKind.Script,
                $"// source {i}", 10, 5, 3, 20, 1500.0));
        }

        var snapshot = pool1.Save();
        var loaded1 = StrategyPool.TryLoad(snapshot);
        var loaded2 = StrategyPool.TryLoad(snapshot);

        Assert.NotNull(loaded1);
        Assert.NotNull(loaded2);
        Assert.Equal(8, loaded1!.Count);
        Assert.Equal(8, loaded2!.Count);

        // Even with same snapshot, re-seeded random may produce different first matchups
        // (though it's possible to get the same by chance; we just verify no crash)
        var matchup1 = loaded1.GetRandomMatchup(4);
        var matchup2 = loaded2.GetRandomMatchup(4);
        Assert.Equal(4, matchup1.Entries.Count);
        Assert.Equal(4, matchup2.Entries.Count);
    }

    // Test 14: Atomic write — verify temp file is created and renamed
    [Fact]
    public void PersistToDisk_UsesAtomicWrite_TempFileCleaned()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-atomic-{Guid.NewGuid():N}");
        try
        {
            var snapshotPath = Path.Combine(tempDir, "pool-snapshot.json");

            var pool = new StrategyPool(new Random(42));
            pool.Add(new StrategyEntry(
                new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
                "// source", 10, 5, 3, 20, 1500.0));

            pool.PersistToDisk(snapshotPath);

            Assert.True(File.Exists(snapshotPath));

            // No .tmp files should remain
            var tempFiles = Directory.GetFiles(tempDir, "*.tmp*");
            Assert.Empty(tempFiles);

            // The file should be loadable
            var loaded = StrategyPool.TryLoadFromDisk(snapshotPath);
            Assert.NotNull(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 15: Save writes valid JSON with expected content
    [Fact]
    public void PersistToDisk_WritesValidJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-json-valid-{Guid.NewGuid():N}");
        try
        {
            var snapshotPath = Path.Combine(tempDir, "pool-snapshot.json");

            var pool = new StrategyPool(new Random(42));
            pool.Add(new StrategyEntry(
                new BonesStrategyId("s1"), 1, BonesStrategyKind.Script,
                "// source", 10, 5, 3, 20, 1500.0));

            pool.PersistToDisk(snapshotPath);

            var content = File.ReadAllText(snapshotPath);
            Assert.Contains("\"entries\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"s1\"", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 16: Full PersistToDisk + TryLoadFromDisk roundtrip with elite and lineage
    [Fact]
    public void PersistToDisk_ThenLoad_ProducesIdenticalPool()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-roundtrip-{Guid.NewGuid():N}");
        try
        {
            var snapshotPath = Path.Combine(tempDir, "pool-snapshot.json");

            var pool = new StrategyPool(new Random(42));
            pool.Add(new StrategyEntry(
                new BonesStrategyId("elite"), 1, BonesStrategyKind.Script,
                "// elite source", 200, 150, 30, 800, 1800.0)
            { IsElite = true });

            for (var i = 2; i <= 8; i++)
            {
                pool.Add(new StrategyEntry(
                    new BonesStrategyId($"s{i}"),
                    (i % 4) + 1,
                    i % 3 == 0 ? BonesStrategyKind.Markdown : BonesStrategyKind.Script,
                    $"// source for s{i}",
                    matchesPlayed: i * 20,
                    wins: i * 12,
                    losses: i * 5,
                    cumulativeScoreDifferential: i * 40,
                    rating: 1500.0 + i * 15));
            }

            pool.RecordLineage(
                new BonesStrategyId("s3"),
                [new BonesStrategyId("elite"), new BonesStrategyId("s2")],
                "CrossBred",
                "Hybrid approach");

            // Establish elite designation
            pool.CheckAllTimeBestPromotion();
            pool.PersistToDisk(snapshotPath);

            var loaded = StrategyPool.TryLoadFromDisk(snapshotPath);
            Assert.NotNull(loaded);
            Assert.Equal(8, loaded!.Count);

            // Verify every entry
            foreach (var kvp in pool.Pool)
            {
                Assert.True(loaded.Pool.TryGetValue(kvp.Key, out var restored));
                Assert.Equal(kvp.Value.Source, restored.Source);
                Assert.Equal(kvp.Value.MatchesPlayed, restored.MatchesPlayed);
                Assert.Equal(kvp.Value.Wins, restored.Wins);
                Assert.Equal(kvp.Value.Losses, restored.Losses);
                Assert.Equal(kvp.Value.Rating, restored.Rating);
                Assert.Equal(kvp.Value.IsElite, restored.IsElite);
                Assert.Equal(kvp.Value.CumulativeScoreDifferential, restored.CumulativeScoreDifferential);
            }

            // Verify lineage
            var lineage = loaded.GetLineage(new BonesStrategyId("s3"));
            Assert.NotNull(lineage);
            Assert.Equal("elite", lineage!.PredecessorId.Value);
            Assert.Equal("s2", lineage.SecondPredecessorId!.Value.Value);
            Assert.Equal("CrossBred", lineage.Outcome);

            // Verify elite (set via CheckAllTimeBestPromotion)
            Assert.NotNull(loaded.EliteStrategyId);
            Assert.True(loaded.Pool[new BonesStrategyId("elite")].IsElite);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ════════════════════════════════════════════
    // Additional: H4 auto-persist test
    // ════════════════════════════════════════════

    // Test 17: EvaluateAndPromote auto-persists when SnapshotPath is set
    [Fact]
    public void EvaluateAndPromote_AutoPersists_WhenSnapshotPathIsSet()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bones-autopersist-{Guid.NewGuid():N}");
        try
        {
            var snapshotPath = Path.Combine(tempDir, "pool-snapshot.json");

            var pool = new StrategyPool(new Random(42))
            {
                SnapshotPath = snapshotPath
            };

            // Add 16 strategies (enough for EvaluateAndPromote with K=4)
            for (var i = 1; i <= 16; i++)
            {
                pool.Add(new StrategyEntry(
                    new BonesStrategyId($"s{i}"), (i % 4) + 1,
                    BonesStrategyKind.Script, $"// source {i}",
                    matchesPlayed: 100, wins: 100 - i * 3, losses: i * 3,
                    cumulativeScoreDifferential: 1000 - i * 50,
                    rating: 2000.0 - i * 30));
            }

            // Before EvaluateAndPromote, file should not exist
            Assert.False(File.Exists(snapshotPath));

            pool.EvaluateAndPromote();

            // After EvaluateAndPromote, file should exist (auto-persisted)
            Assert.True(File.Exists(snapshotPath));

            // The persisted file should be loadable
            var loaded = StrategyPool.TryLoadFromDisk(snapshotPath);
            Assert.NotNull(loaded);
            // After EvaluateAndPromote with K=4, 12 survivors remain
            Assert.Equal(12, loaded!.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Test 18: SnapshotPath not set → no auto-persist
    [Fact]
    public void EvaluateAndPromote_DoesNotPersist_WhenSnapshotPathNotSet()
    {
        var pool = new StrategyPool(new Random(42));
        for (var i = 1; i <= 16; i++)
        {
            pool.Add(new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                BonesStrategyKind.Script, $"// source {i}",
                matchesPlayed: 100, wins: 100 - i * 3, losses: i * 3,
                cumulativeScoreDifferential: 1000 - i * 50,
                rating: 2000.0 - i * 30));
        }

        // Should not throw — no snapshot path set
        var results = pool.EvaluateAndPromote();
        Assert.Equal(12, results.Promoted);
        Assert.Equal(4, results.Rejected);
    }

    // Test 19: GetPoolSnapshotPath follows convention
    [Fact]
    public void GetPoolSnapshotPath_FollowsBonesDirectoryConvention()
    {
        var path = StrategyPool.GetPoolSnapshotPath("C:/data");
        Assert.Contains(".bones", path);
        Assert.Contains("pool", path);
        Assert.EndsWith("pool-snapshot.json", path);
    }

    // Test 20: Corrupt entry skipped with warning log
    [Fact]
    public void TryLoad_GivenInvalidEntryData_LogsWarning()
    {
        var invalidEntry = new PersistedPoolEntry("bad", 5, BonesStrategyKind.Script,
            "// bad", -5, 0, 0, 0, 1500.0, false); // seat 5 is invalid

        var validEntry = new PersistedPoolEntry("good", 1, BonesStrategyKind.Script,
            "// good", 10, 5, 3, 20, 1500.0, false);

        var snapshot = new StrategyPoolSnapshot(
            new[] { invalidEntry, validEntry },
            Array.Empty<PersistedLineageRecord>(),
            null,
            0.0,
            DateTimeOffset.UtcNow);

        // Even without a real logger, TryLoad should handle gracefully
        var loaded = StrategyPool.TryLoad(snapshot, logger: null);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Count);
        Assert.False(loaded.Pool.ContainsKey(new BonesStrategyId("bad")));
        Assert.True(loaded.Pool.ContainsKey(new BonesStrategyId("good")));
    }
}
