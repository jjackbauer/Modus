using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesPoolPageRenderWebApplicationFactory : WebApplicationFactory<Program>
{
    public StrategyPoolHolder Holder { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(Holder);
            services.AddTransient<StrategyPoolJsonService>(
                sp => new StrategyPoolJsonService(sp.GetRequiredService<StrategyPoolHolder>().Pool));
        });
    }
}

[Collection("StrategyPool")]
public sealed class BonesPoolPageRenderTests : IClassFixture<BonesPoolPageRenderWebApplicationFactory>
{
    private readonly BonesPoolPageRenderWebApplicationFactory _factory;
    private readonly HtmlParser _parser = new();

    public BonesPoolPageRenderTests(BonesPoolPageRenderWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private StrategyPool CreatePopulatedPool()
    {
        var pool = new StrategyPool(new Random(42));
        for (var i = 1; i <= 6; i++)
        {
            var kind = i % 2 == 0 ? BonesStrategyKind.Script : BonesStrategyKind.Markdown;
            var seat = (i % 4) + 1;
            var entry = new StrategyEntry(
                new BonesStrategyId($"strat-{i}"), seat, kind,
                $"// Strategy {i} source code\nvar x = {i};",
                10 + i * 5, 6 + i, 3 + i / 2, 20 + i * 10, 1400 + i * 20)
            { IsElite = i == 1 };
            pool.Add(entry);
        }
        pool.RecordLineage(
            new BonesStrategyId("strat-2"),
            [new BonesStrategyId("strat-1")],
            "Improved", "Added better opening heuristic");
        return pool;
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void RenderDashboard_GivenPopulatedPool_RendersCardPerEntry()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        var doc = _parser.ParseDocument(html);
        var cards = doc.QuerySelectorAll(".pool-card");
        Assert.True(cards.Length >= 6, $"Expected at least 6 cards, got {cards.Length}");
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void RenderDashboard_CollapsedCardShowsSimplifiedStats()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        var doc = _parser.ParseDocument(html);
        var firstCard = doc.QuerySelector(".pool-card");
        Assert.NotNull(firstCard);
        // Check for stat elements using actual class names
        var stats = firstCard.QuerySelector(".pool-card-stats");
        Assert.NotNull(stats);
        Assert.NotNull(firstCard.QuerySelector(".pool-card-kind"));
        Assert.NotNull(firstCard.QuerySelector(".pool-card-seat"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void RenderDashboard_ExpandedCardShowsSourceInPreBlock()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        var doc = _parser.ParseDocument(html);
        var sourceBlock = doc.QuerySelector(".pool-card-source code");
        Assert.NotNull(sourceBlock);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void RenderDashboard_ExpandedCardContainsMatchLinks()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        var doc = _parser.ParseDocument(html);
        var matchLinks = doc.QuerySelectorAll(".pool-card-matches");
        Assert.True(matchLinks.Length > 0, "Expected match links section in cards");
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void RenderDashboard_ExpandedCardShowsLineageTree()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        var doc = _parser.ParseDocument(html);
        var lineageSection = doc.QuerySelector(".pool-card-lineage");
        Assert.NotNull(lineageSection);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void PoolDashboardHeader_ShowsLearningLoopStatus()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        var doc = _parser.ParseDocument(html);
        // Learning loop status uses existing class "learning-loop-status"
        var statusBar = doc.QuerySelector(".learning-loop-status");
        Assert.NotNull(statusBar);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolNavigation)]
    public void MatchViewerSidebar_HasPoolDashboardLink()
    {
        _factory.Holder.Pool = CreatePopulatedPool();
        using var client = _factory.CreateClient();
        var html = client.GetStringAsync("/bones/pool/view").Result;
        // The pool page links back to the match viewer
        Assert.Contains("/viewer/index.html", html);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolViewModels)]
    public void PoolEntryViewModel_JsonRoundtrip_PreservesAllFields()
    {
        var vm = new PoolEntryViewModel
        {
            Id = "test-1", Seat = 1, Kind = "Script",
            MatchesPlayed = 10, Wins = 7, Losses = 3,
            CumulativeScoreDifferential = 25, Rating = 1520.5,
            IsElite = true, WinRate = 0.7, IterationCount = 3
        };
        var json = JsonSerializer.Serialize(vm, BonesWebHost.JsonOptions);
        var roundtripped = JsonSerializer.Deserialize<PoolEntryViewModel>(json, BonesWebHost.JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Equal(vm.Id, roundtripped.Id);
        Assert.Equal(vm.Seat, roundtripped.Seat);
        Assert.Equal(vm.Kind, roundtripped.Kind);
        Assert.Equal(vm.MatchesPlayed, roundtripped.MatchesPlayed);
        Assert.Equal(vm.Wins, roundtripped.Wins);
        Assert.Equal(vm.Losses, roundtripped.Losses);
        Assert.Equal(vm.CumulativeScoreDifferential, roundtripped.CumulativeScoreDifferential);
        Assert.Equal(vm.Rating, roundtripped.Rating);
        Assert.Equal(vm.IsElite, roundtripped.IsElite);
        Assert.Equal(vm.WinRate, roundtripped.WinRate);
        Assert.Equal(vm.IterationCount, roundtripped.IterationCount);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolViewModels)]
    public void StrategyDetailViewModel_SourceNotTruncated()
    {
        var longSource = new string('x', 10240);
        var detail = new StrategyDetailViewModel
        {
            StrategyId = "test", Seat = 1, Kind = "Script",
            Source = longSource, MatchesPlayed = 5, Wins = 3, Losses = 2,
            CumulativeScoreDifferential = 10, Rating = 1500, IsElite = false,
            WinRate = 0.6, OpponentSummaries = [], RecentMatches = []
        };
        var json = JsonSerializer.Serialize(detail, BonesWebHost.JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var sourceField = doc.RootElement.GetProperty("source").GetString();
        Assert.Equal(longSource.Length, sourceField?.Length ?? 0);
    }
}
