using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class BonesPoolApiWebAppFactory : WebApplicationFactory<Program>
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
public sealed class BonesPoolApiEndpointTests : IClassFixture<BonesPoolApiWebAppFactory>
{
    private readonly BonesPoolApiWebAppFactory _factory;

    public BonesPoolApiEndpointTests(BonesPoolApiWebAppFactory factory)
    {
        _factory = factory;
    }

    private static StrategyPool MakePool()
    {
        var pool = new StrategyPool(new Random(42));
        for (var i = 1; i <= 6; i++)
        {
            var entry = new StrategyEntry(
                new BonesStrategyId($"s{i}"), (i % 4) + 1,
                i % 2 == 0 ? BonesStrategyKind.Script : BonesStrategyKind.Markdown,
                $"// source {i}", 10 + i * 5, 6 + i, 3 + i / 2, 20 + i * 10, 1400 + i * 20)
            { IsElite = i == 1 };
            pool.Add(entry);
        }
        pool.RecordLineage(new BonesStrategyId("s2"), [new BonesStrategyId("s1")], "Improved", "better opening");
        pool.RecordLineage(new BonesStrategyId("s3"), [new BonesStrategyId("s1"), new BonesStrategyId("s2")], "Cross", "combined");
        return pool;
    }

    // Test 1: A1 - GET /bones/pool returns all entries
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetPool_GivenPopulatedPool_Returns200WithAllEntries()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal(6, entries.GetArrayLength());
        var first = entries[0];
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("winRate", out _));
    }

    // Test 2: A1 - empty pool returns empty array
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetPool_GivenEmptyPool_Returns200WithEmptyArray()
    {
        _factory.Holder.Pool = new StrategyPool(new Random(42));
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        Assert.Equal(0, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    // Test 3: A5 - null pool returns 503
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetPool_GivenNullPoolInstance_Returns503WithErrorBody()
    {
        _factory.Holder.Pool = null;
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool").Result;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        Assert.Equal("PoolUninitialized", doc.RootElement.GetProperty("error").GetString());
    }

    // Test 4: A1 - ordered by rating descending
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetPool_EntriesOrderedByCompositeScoreDescending()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool").Result;
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        var ratings = doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("rating").GetDouble()).ToList();
        for (var i = 0; i < ratings.Count - 1; i++)
            Assert.True(ratings[i] >= ratings[i + 1]);
    }

    // Test 5: A2 - detail with source and lineage
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetStrategyDetail_GivenValidId_Returns200WithSourceCodeAndLineage()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/s2").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        Assert.Equal("s2", doc.RootElement.GetProperty("strategyId").GetString());
        Assert.True(doc.RootElement.GetProperty("source").GetString()!.Length > 0);
        Assert.True(doc.RootElement.TryGetProperty("lineage", out var lineage));
        Assert.Equal("s1", lineage.GetProperty("predecessorId").GetString());
    }

    // Test 6: A2 - unknown ID returns 404
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetStrategyDetail_GivenUnknownId_Returns404()
    {
        _factory.Holder.Pool = new StrategyPool(new Random(42));
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/nope").Result;
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // Test 7: A2 - source is exact
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetStrategyDetail_SourceIsExactStrategyContent()
    {
        var pool = new StrategyPool(new Random(42));
        var src = "// Exact source\nreturn Move.Play(6, 6);";
        pool.Add(new StrategyEntry(new BonesStrategyId("exact"), 1, BonesStrategyKind.Script, src, 5, 3, 2, 10, 1500));
        _factory.Holder.Pool = pool;
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/exact").Result;
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        Assert.Equal(src, doc.RootElement.GetProperty("source").GetString());
    }

    // Test 8: A3 - lineage with parent
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetLineage_GivenStrategyWithParent_ReturnsChainWithPredecessor()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/s2/lineage").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        Assert.Equal("s1", doc.RootElement.GetProperty("predecessorId").GetString());
    }

    // Test 9: A3 - root strategy has no predecessor
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetLineage_GivenRootStrategy_ReturnsChainWithNoPredecessor()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/s1/lineage").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = resp.Content.ReadAsStringAsync().Result;
        using var doc = JsonDocument.Parse(json);
        // Root strategy has no predecessor — key is absent when null due to WhenWritingNull option
        Assert.False(doc.RootElement.TryGetProperty("predecessorId", out _));
    }

    // Test 10: A4 - aggregate stats
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetPoolStats_GivenPopulatedPool_ReturnsAggregateStatistics()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/stats").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        Assert.Equal(6, doc.RootElement.GetProperty("totalStrategies").GetInt32());
        Assert.True(doc.RootElement.GetProperty("averageElo").GetDouble() > 0);
        Assert.True(doc.RootElement.TryGetProperty("eloDistribution", out _));
        Assert.True(doc.RootElement.TryGetProperty("strategiesByKind", out _));
    }

    // Test 11: A4 - ELO buckets monotonic
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolApi)]
    public void GetPoolStats_EloDistributionBucketsAreMonotonic()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/stats").Result;
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        var mins = doc.RootElement.GetProperty("eloDistribution").EnumerateArray()
            .Select(b => b.GetProperty("min").GetDouble()).ToList();
        for (var i = 0; i < mins.Count - 1; i++)
            Assert.True(mins[i] <= mins[i + 1]);
    }

    // Test 21: C3 - MapPoolRoutes registers all endpoints
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolNavigation)]
    public void MapPoolRoutes_RegistersAllExpectedEndpoints()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        Assert.NotEqual(HttpStatusCode.NotFound, client.GetAsync("/bones/pool").Result.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, client.GetAsync("/bones/pool/s1").Result.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, client.GetAsync("/bones/pool/s1/lineage").Result.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, client.GetAsync("/bones/pool/stats").Result.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, client.GetAsync("/bones/pool/view").Result.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, client.GetAsync("/bones/pool/s1/view").Result.StatusCode);
    }

    // Test 22: D1 - /bones/pool/{id}/view returns HTML with source code
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void GetStrategyDetailView_GivenValidId_Returns200HtmlWithSourceCode()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/s2/view").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        Assert.NotNull(contentType);
        Assert.Contains("text/html", contentType);
        var html = resp.Content.ReadAsStringAsync().Result;
        Assert.Contains("// source 2", html);
        Assert.Contains("s2", html);
    }

    // Test 23: D1 - /bones/pool/{id}/view returns 404 for unknown ID
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void GetStrategyDetailView_GivenUnknownId_Returns404Html()
    {
        _factory.Holder.Pool = new StrategyPool(new Random(42));
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/nope/view").Result;
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        Assert.NotNull(contentType);
        Assert.Contains("text/html", contentType);
        var html = resp.Content.ReadAsStringAsync().Result;
        Assert.Contains("Strategy Not Found", html);
    }

    // Test 24: D1 - /bones/pool/{id}/view has back-to-pool navigation link
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolNavigation)]
    public void GetStrategyDetailView_GivenValidId_ExpectedContainsBackToPoolLink()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/s1/view").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = resp.Content.ReadAsStringAsync().Result;
        Assert.Contains("Back to pool dashboard", html);
        Assert.Contains("href=\"/bones/pool/view\"", html);
    }

    // Test 25: D1 - /bones/pool/view dashboard has card nav links
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void GetPoolDashboardView_GivenPopulatedPool_ExpectedCardHeadersHaveNavLinks()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/view").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = resp.Content.ReadAsStringAsync().Result;
        Assert.Contains("pool-card-nav-link", html);
        Assert.Contains("href=\"/bones/pool/s1/view\"", html);
    }

    // Test 26: D1 - /bones/pool/view "View full detail" link points to /view
    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void GetPoolDashboardView_GivenPopulatedPool_ExpectedViewFullDetailLinkPointsToViewRoute()
    {
        _factory.Holder.Pool = MakePool();
        using var client = _factory.CreateClient();
        using var resp = client.GetAsync("/bones/pool/view").Result;
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = resp.Content.ReadAsStringAsync().Result;
        // The "View full detail" link should point to /view, not the JSON endpoint
        Assert.Contains("href=\"/bones/pool/s2/view\"", html);
        // Should not have a link to just /bones/pool/{id} without /view (old broken behavior)
        Assert.DoesNotContain("href=\"/bones/pool/s2\"", html);
    }
}
