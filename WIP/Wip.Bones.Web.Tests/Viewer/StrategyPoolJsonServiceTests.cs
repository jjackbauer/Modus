using System.Text.Json;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("StrategyPool")]
public sealed class StrategyPoolJsonServiceTests
{
    private const string ChecklistItem = BonesPoolRequirementsChecklistItems.PoolJsonService;

    private static StrategyEntry CreateEntry(
        string id, int seat = 1,
        BonesStrategyKind kind = BonesStrategyKind.Script,
        string source = "// test source",
        int matches = 10, int wins = 6, int losses = 4,
        int scoreDiff = 20, double rating = 1520.0,
        bool isElite = false)
    {
        return new StrategyEntry(
            new BonesStrategyId(id), seat, kind, source,
            matches, wins, losses, scoreDiff, rating)
        { IsElite = isElite };
    }

    private static StrategyPool CreatePopulatedPool()
    {
        var pool = new StrategyPool();
        pool.Add(CreateEntry("alpha", seat: 1, rating: 1600, wins: 8, losses: 2, scoreDiff: 60, isElite: true));
        pool.Add(CreateEntry("beta", seat: 2, rating: 1550, wins: 7, losses: 3, scoreDiff: 40));
        pool.Add(CreateEntry("gamma", seat: 3, kind: BonesStrategyKind.Markdown, rating: 1500, wins: 5, losses: 5, scoreDiff: 0));
        pool.Add(CreateEntry("delta", seat: 4, rating: 1450, wins: 4, losses: 6, scoreDiff: -20));
        return pool;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildPoolSnapshot_GivenPopulatedPool_ReturnsEntriesOrderedByRatingDescending()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var snapshot = sut.BuildPoolSnapshot();

        Assert.Equal(4, snapshot.Entries.Count);
        Assert.True(snapshot.IsRunning);
        for (var i = 1; i < snapshot.Entries.Count; i++)
            Assert.True(snapshot.Entries[i - 1].Rating >= snapshot.Entries[i].Rating);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildPoolSnapshot_GivenEmptyPool_ReturnsEmptyEntriesAndStats()
    {
        var sut = new StrategyPoolJsonService(new StrategyPool());
        var snapshot = sut.BuildPoolSnapshot();

        Assert.Empty(snapshot.Entries);
        Assert.NotNull(snapshot.Stats);
        Assert.Equal(0, snapshot.Stats.TotalStrategies);
        Assert.Equal(0, snapshot.Stats.TotalMatchesPlayed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildPoolSnapshot_GivenNullInstance_ReturnsEmptySnapshot()
    {
        var sut = new StrategyPoolJsonService(null);
        var snapshot = sut.BuildPoolSnapshot();
        Assert.Empty(snapshot.Entries);
        Assert.False(snapshot.IsRunning);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildStrategyDetail_GivenValidId_ReturnsDetailWithSourceAndLineage()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var detail = sut.BuildStrategyDetail(new BonesStrategyId("alpha"));

        Assert.NotNull(detail);
        Assert.Equal("alpha", detail!.StrategyId);
        Assert.Equal(BonesStrategyKind.Script.ToString(), detail.Kind);
        Assert.Equal("// test source", detail.Source);
        Assert.Equal(1600.0, detail.Rating);
        Assert.True(detail.IsElite);
        Assert.True(detail.WinRate > 0);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildStrategyDetail_GivenUnknownId_ReturnsNull()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var detail = sut.BuildStrategyDetail(new BonesStrategyId("nonexistent"));
        Assert.Null(detail);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildStrategyDetail_SourceIsExactStrategyContent()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var detail = sut.BuildStrategyDetail(new BonesStrategyId("alpha"));
        Assert.NotNull(detail);
        Assert.Equal("// test source", detail!.Source);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildStrategyDetail_GivenNullInstance_ReturnsNull()
    {
        var sut = new StrategyPoolJsonService(null);
        var detail = sut.BuildStrategyDetail(new BonesStrategyId("alpha"));
        Assert.Null(detail);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildPoolStats_GivenPopulatedPool_ReturnsAggregateStatistics()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var stats = sut.BuildPoolStats();

        Assert.Equal(4, stats.TotalStrategies);
        Assert.True(stats.TotalMatchesPlayed > 0);
        Assert.True(stats.TotalWins > 0);
        Assert.True(stats.AverageElo > 1400);
        Assert.True(stats.EliteCount > 0);
        Assert.Contains("Script", stats.StrategiesByKind.Keys);
        Assert.Contains("Markdown", stats.StrategiesByKind.Keys);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildPoolStats_EloDistributionBucketsAreMonotonic()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var stats = sut.BuildPoolStats();

        Assert.NotEmpty(stats.EloDistribution);
        for (var i = 1; i < stats.EloDistribution.Count; i++)
        {
            Assert.True(stats.EloDistribution[i - 1].Min < stats.EloDistribution[i].Min,
                $"ELO bucket min values must be ascending");
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildPoolStats_GivenEmptyPool_ReturnsZeroStats()
    {
        var sut = new StrategyPoolJsonService(new StrategyPool());
        var stats = sut.BuildPoolStats();
        Assert.Equal(0, stats.TotalStrategies);
        Assert.Equal(0, stats.TotalMatchesPlayed);
        Assert.Equal(0, stats.TotalWins);
        Assert.Equal(0.0, stats.AverageElo);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildLineageViewModel_GivenStrategyWithParent_ReturnsChainWithPredecessor()
    {
        var pool = CreatePopulatedPool();
        pool.RecordLineage(
            new BonesStrategyId("gamma"),
            [new BonesStrategyId("alpha")],
            "enhanced", "improved scoring");
        var sut = new StrategyPoolJsonService(pool);
        var lineage = sut.BuildLineageViewModel(new BonesStrategyId("gamma"));

        Assert.NotNull(lineage);
        Assert.Equal("alpha", lineage!.PredecessorId);
        Assert.Equal("enhanced", lineage.Outcome);
        Assert.Equal("improved scoring", lineage.DiffSummary);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildLineageViewModel_GivenRootStrategy_ReturnsChainWithNoPredecessor()
    {
        var pool = CreatePopulatedPool();
        pool.RecordLineage(
            new BonesStrategyId("gamma"),
            [new BonesStrategyId("alpha")],
            "enhanced", "improved scoring");
        var sut = new StrategyPoolJsonService(pool);
        var lineage = sut.BuildLineageViewModel(new BonesStrategyId("alpha"));

        Assert.NotNull(lineage);
        Assert.Null(lineage!.PredecessorId);
        Assert.Contains("gamma", lineage.SuccessorIds);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BuildLineageViewModel_GivenUnknownId_ReturnsNull()
    {
        var sut = new StrategyPoolJsonService(CreatePopulatedPool());
        var lineage = sut.BuildLineageViewModel(new BonesStrategyId("unknown"));
        Assert.Null(lineage);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void PoolEntryViewModel_JsonRoundtrip_PreservesAllFields()
    {
        var original = new PoolEntryViewModel
        {
            Id = "strategy-1", Seat = 1, Kind = "Script",
            MatchesPlayed = 10, Wins = 7, Losses = 3,
            CumulativeScoreDifferential = 42, Rating = 1520.5,
            IsElite = true, WinRate = 0.7, IterationCount = 3,
        };
        var json = JsonSerializer.Serialize(original, BonesWebHost.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PoolEntryViewModel>(json, BonesWebHost.JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized!.Id);
        Assert.Equal(original.Seat, deserialized.Seat);
        Assert.Equal(original.Kind, deserialized.Kind);
        Assert.Equal(original.Rating, deserialized.Rating);
        Assert.True(json.Contains("\"id\""));
        Assert.True(json.Contains("\"seat\""));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void StrategyDetailViewModel_SourceNotTruncated()
    {
        var longSource = new string('x', 10_000);
        var detail = new StrategyDetailViewModel
        {
            StrategyId = "long-source",
            Seat = 1, Kind = "Script", Source = longSource,
            MatchesPlayed = 0, Wins = 0, Losses = 0,
            CumulativeScoreDifferential = 0, Rating = 1500,
            IsElite = false, WinRate = 0,
            OpponentSummaries = [],
            RecentMatches = [],
        };
        var json = JsonSerializer.Serialize(detail, BonesWebHost.JsonOptions);
        Assert.Contains(longSource, json);
        var deserialized = JsonSerializer.Deserialize<StrategyDetailViewModel>(json, BonesWebHost.JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(longSource.Length, deserialized!.Source.Length);
    }
}
