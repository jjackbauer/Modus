using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Tests;
using Xunit;

namespace Wip.Bones.Tests.Knowledge;

public sealed class BonesStrategyLibraryTests
{
    private const string StrategyLibraryItem = BonesRequirementsChecklistItems.StrategyLibrary;
    private const string LibraryRankingItem = BonesRequirementsChecklistItems.StrategyLibraryRanking;
    private const string UpsertBestStrategyItem = BonesRequirementsChecklistItems.StrategyLibraryUpsertBestStrategy;
    private const string LoadBestForAllSeatsItem = BonesRequirementsChecklistItems.StrategyLibraryLoadBestForAllSeats;

    [Fact]
    [Trait("ChecklistItem", StrategyLibraryItem)]
    [Trait("ChecklistItem", UpsertBestStrategyItem)]
    public void BonesStrategyLibrary_GivenUpsertAfterPromotion_ExpectedPersistsUnderDataDirectory()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var library = new BonesStrategyLibrary(dataDirectory);
            var playerId = new BonesPlayerId(2);
            var strategyId = new BonesStrategyId("promoted-seat-2-v3");
            var artifact = new BonesStrategyArtifact(
                strategyId,
                playerId,
                BonesStrategyKind.Script,
                "public sealed class SeatStrategy : IBonesPlayerSlot { }",
                BonesPromotionStatus.Active);
            var metrics = new BonesStrategyEffectivenessRecord(strategyId, playerId, 4, 3, 1, 12);

            Assert.True(library.UpsertBestStrategy(playerId, artifact, metrics));

            var expectedPath = Path.Combine(dataDirectory, ".bones", "strategy-library", "seat-2.json");
            Assert.True(File.Exists(expectedPath));

            var reloadedLibrary = new BonesStrategyLibrary(dataDirectory);
            var loaded = reloadedLibrary.LoadBestStrategy(playerId);

            Assert.NotNull(loaded);
            Assert.Equal(strategyId, loaded.StrategyId);
            Assert.Equal(BonesStrategyKind.Script, loaded.Kind);
            Assert.Equal(artifact.Source, loaded.Source);
            Assert.Equal(BonesPromotionStatus.Active, loaded.PromotionStatus);

            var ranking = reloadedLibrary.GetRanking(playerId);
            Assert.Single(ranking);
            Assert.Equal(3, ranking[0].Effectiveness.Wins);
            Assert.Equal(12, ranking[0].Effectiveness.CumulativeScoreDifferential);
            Assert.True(ranking[0].LastUpdatedUtc <= DateTimeOffset.UtcNow);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", StrategyLibraryItem)]
    public void BonesStrategyLibrary_GivenMissingEntry_ExpectedLoadBestStrategyReturnsNull()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-library-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var library = new BonesStrategyLibrary(dataDirectory);
            Assert.Null(library.LoadBestStrategy(new BonesPlayerId(3)));
            Assert.Empty(library.GetRanking(new BonesPlayerId(3)));
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", LibraryRankingItem)]
    public void BonesStrategyLibrary_GivenTwoEntriesForSeat_ExpectedLoadBestStrategyReturnsHigherWinRate()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-library-rank-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var library = new BonesStrategyLibrary(dataDirectory);
            var playerId = new BonesPlayerId(2);
            var weakerId = new BonesStrategyId("seat-2-weaker");
            var strongerId = new BonesStrategyId("seat-2-stronger");

            Assert.True(library.UpsertBestStrategy(
                playerId,
                new BonesStrategyArtifact(weakerId, playerId, BonesStrategyKind.Script, "weaker", BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(weakerId, playerId, 4, 1, 3, 2)));

            Assert.True(library.UpsertBestStrategy(
                playerId,
                new BonesStrategyArtifact(strongerId, playerId, BonesStrategyKind.Script, "stronger", BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(strongerId, playerId, 4, 3, 1, 2)));

            var best = library.LoadBestStrategy(playerId);
            Assert.NotNull(best);
            Assert.Equal(strongerId, best.StrategyId);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", LibraryRankingItem)]
    public void BonesStrategyLibrary_GivenTiedWinRate_ExpectedTieBreaksByScoreDifferential()
    {
        var lowerDifferential = CreateEntry(
            new BonesStrategyId("seat-2-low-diff"),
            new BonesPlayerId(2),
            wins: 1,
            matchesPlayed: 2,
            cumulativeScoreDifferential: 5,
            lastUpdatedUtc: DateTimeOffset.UtcNow.AddMinutes(-5));

        var higherDifferential = CreateEntry(
            new BonesStrategyId("seat-2-high-diff"),
            new BonesPlayerId(2),
            wins: 1,
            matchesPlayed: 2,
            cumulativeScoreDifferential: 20,
            lastUpdatedUtc: DateTimeOffset.UtcNow);

        var ranking = BonesStrategyLibraryRanking.SortDescending([lowerDifferential, higherDifferential]);

        Assert.Equal("seat-2-high-diff", ranking[0].StrategyId.Value);
        Assert.Equal("seat-2-low-diff", ranking[1].StrategyId.Value);
    }

    [Fact]
    [Trait("ChecklistItem", LibraryRankingItem)]
    public void RanksHigher_GivenScriptVsMarkdownWithIdenticalMetrics_PrefersScript()
    {
        var script = CreateEntry(
            new BonesStrategyId("tie-script"),
            new BonesPlayerId(1),
            wins: 3,
            matchesPlayed: 6,
            cumulativeScoreDifferential: 10,
            lastUpdatedUtc: DateTimeOffset.UtcNow,
            kind: BonesStrategyKind.Script);

        var markdown = CreateEntry(
            new BonesStrategyId("tie-markdown"),
            new BonesPlayerId(1),
            wins: 3,
            matchesPlayed: 6,
            cumulativeScoreDifferential: 10,
            lastUpdatedUtc: DateTimeOffset.UtcNow,
            kind: BonesStrategyKind.Markdown);

        var ranking = BonesStrategyLibraryRanking.SortDescending([markdown, script]);

        Assert.Equal("tie-script", ranking[0].StrategyId.Value);
        Assert.Equal("tie-markdown", ranking[1].StrategyId.Value);
        Assert.True(BonesStrategyLibraryRanking.RanksHigher(script, markdown));
        Assert.False(BonesStrategyLibraryRanking.RanksHigher(markdown, script));
    }

    [Fact]
    [Trait("ChecklistItem", LibraryRankingItem)]
    public void RanksHigher_GivenMarkdownWithBetterMetricsVsScript_PrefersBetterMetrics()
    {
        var script = CreateEntry(
            new BonesStrategyId("weaker-script"),
            new BonesPlayerId(1),
            wins: 2,
            matchesPlayed: 6,
            cumulativeScoreDifferential: 5,
            lastUpdatedUtc: DateTimeOffset.UtcNow,
            kind: BonesStrategyKind.Script);

        var markdown = CreateEntry(
            new BonesStrategyId("stronger-markdown"),
            new BonesPlayerId(1),
            wins: 4,
            matchesPlayed: 6,
            cumulativeScoreDifferential: 20,
            lastUpdatedUtc: DateTimeOffset.UtcNow,
            kind: BonesStrategyKind.Markdown);

        var ranking = BonesStrategyLibraryRanking.SortDescending([script, markdown]);

        Assert.Equal("stronger-markdown", ranking[0].StrategyId.Value);
        Assert.Equal("weaker-script", ranking[1].StrategyId.Value);
    }

    [Fact]
    [Trait("ChecklistItem", UpsertBestStrategyItem)]
    public void BonesStrategyLibrary_GivenInferiorMetrics_ExpectedUpsertBestStrategyRetainsIncumbent()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-library-retain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var library = new BonesStrategyLibrary(dataDirectory);
            var playerId = new BonesPlayerId(3);
            var incumbentId = new BonesStrategyId("seat-3-incumbent");
            var challengerId = new BonesStrategyId("seat-3-challenger");

            Assert.True(library.UpsertBestStrategy(
                playerId,
                new BonesStrategyArtifact(incumbentId, playerId, BonesStrategyKind.Script, "incumbent", BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(incumbentId, playerId, 6, 4, 2, 18)));

            Assert.False(library.UpsertBestStrategy(
                playerId,
                new BonesStrategyArtifact(challengerId, playerId, BonesStrategyKind.Script, "challenger", BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(challengerId, playerId, 6, 2, 4, 10)));

            var best = library.LoadBestStrategy(playerId);
            Assert.NotNull(best);
            Assert.Equal(incumbentId, best.StrategyId);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", LoadBestForAllSeatsItem)]
    public void BonesStrategyLibrary_GivenLoadBestStrategiesForAllSeats_ExpectedReturnsDictionaryWithFourEntries()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-library-allseats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var library = new BonesStrategyLibrary(dataDirectory);

            // Populate all 4 seats.
            for (var seat = 1; seat <= 4; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                var strategyId = new BonesStrategyId($"seat-{seat}-best");
                library.UpsertBestStrategy(
                    playerId,
                    new BonesStrategyArtifact(strategyId, playerId, BonesStrategyKind.Script,
                        $"// seat {seat} strategy", BonesPromotionStatus.Active),
                    new BonesStrategyEffectivenessRecord(strategyId, playerId, 5, 3, 2, 10));
            }

            var result = library.LoadBestStrategiesForAllSeats();

            Assert.Equal(4, result.Count);
            for (var seat = 1; seat <= 4; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                Assert.True(result.ContainsKey(playerId));
                Assert.Equal($"seat-{seat}-best", result[playerId].StrategyId.Value);
                Assert.Equal(BonesStrategyKind.Script, result[playerId].Kind);
                Assert.Equal($"// seat {seat} strategy", result[playerId].Source);
            }
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", LoadBestForAllSeatsItem)]
    public void BonesStrategyLibrary_GivenLoadBestStrategiesForAllSeatsWithMissingSeats_ExpectedReturnsPartialDictionaryWithIntegrityReport()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"bones-library-partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var library = new BonesStrategyLibrary(dataDirectory);

            // Only populate seats 1 and 3.
            var seat1Id = new BonesPlayerId(1);
            var seat3Id = new BonesPlayerId(3);
            library.UpsertBestStrategy(
                seat1Id,
                new BonesStrategyArtifact(new BonesStrategyId("s1"), seat1Id, BonesStrategyKind.Script,
                    "seat 1 source", BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("s1"), seat1Id, 5, 3, 2, 10));

            library.UpsertBestStrategy(
                seat3Id,
                new BonesStrategyArtifact(new BonesStrategyId("s3"), seat3Id, BonesStrategyKind.Script,
                    "seat 3 source", BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(new BonesStrategyId("s3"), seat3Id, 5, 4, 1, 20));

            var (strategies, integrity) = library.VerifyIntegrityWithReport();

            // Strategies dictionary has only 2 entries.
            Assert.Equal(2, strategies.Count);
            Assert.True(strategies.ContainsKey(new BonesPlayerId(1)));
            Assert.True(strategies.ContainsKey(new BonesPlayerId(3)));
            Assert.False(strategies.ContainsKey(new BonesPlayerId(2)));
            Assert.False(strategies.ContainsKey(new BonesPlayerId(4)));

            // Integrity report covers all 4 seats.
            Assert.Equal(4, integrity.Count);
            Assert.Contains(integrity, r => r.Seat == 1 && r.Status == BonesLibrarySeatIntegrityStatus.Present);
            Assert.Contains(integrity, r => r.Seat == 2 && r.Status == BonesLibrarySeatIntegrityStatus.Missing);
            Assert.Contains(integrity, r => r.Seat == 3 && r.Status == BonesLibrarySeatIntegrityStatus.Present);
            Assert.Contains(integrity, r => r.Seat == 4 && r.Status == BonesLibrarySeatIntegrityStatus.Missing);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static BonesStrategyLibraryEntry CreateEntry(
        BonesStrategyId strategyId,
        BonesPlayerId playerId,
        int wins,
        int matchesPlayed,
        int cumulativeScoreDifferential,
        DateTimeOffset lastUpdatedUtc,
        BonesStrategyKind kind = BonesStrategyKind.Script)
        => new(
            strategyId,
            playerId,
            kind,
            strategyId.Value,
            new BonesStrategyEffectivenessRecord(
                strategyId,
                playerId,
                matchesPlayed,
                wins,
                matchesPlayed - wins,
                cumulativeScoreDifferential),
            lastUpdatedUtc);
}
