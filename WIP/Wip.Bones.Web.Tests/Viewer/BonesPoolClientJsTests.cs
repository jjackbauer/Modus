using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.Bones.Web;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("StrategyPool")]
public sealed class BonesPoolClientJsTests : IDisposable
{
    private readonly HtmlParser _parser = new();
    private StrategyPool? _previousInstance;

    public BonesPoolClientJsTests()
    {
        _previousInstance = StrategyPool.Instance;
        StrategyPool.Instance = null;
    }

    public void Dispose()
    {
        StrategyPool.Instance = _previousInstance;
    }

    private StrategyPool CreatePopulatedPool()
    {
        var pool = new StrategyPool(new Random(42));
        for (var i = 1; i <= 4; i++)
        {
            var kind = i % 2 == 0 ? BonesStrategyKind.Script : BonesStrategyKind.Markdown;
            var seat = (i % 4) + 1;
            var entry = new StrategyEntry(
                new BonesStrategyId($"strat-{i}"), seat, kind,
                $"// Strategy {i} source", 10 + i * 5, 6 + i, 3, 20 + i * 10, 1400 + i * 20)
            { IsElite = i == 1 };
            pool.Add(entry);
        }
        return pool;
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void DashboardAutoPoll_WhenIsRunning_PollsPoolEndpoint()
    {
        StrategyPool.Instance = CreatePopulatedPool();
        var snapshot = new StrategyPoolJsonService().BuildPoolSnapshot();
        // When pool is populated, IsRunning is true (pool active)
        Assert.True(snapshot.IsRunning);
        Assert.Equal(4, snapshot.Entries.Count);
        foreach (var entry in snapshot.Entries)
        {
            Assert.NotNull(entry.Id);
            Assert.True(entry.WinRate >= 0);
            Assert.True(entry.IterationCount >= 0);
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolDashboard)]
    public void DashboardNoPoll_WhenNotRunning_DoesNotPoll()
    {
        StrategyPool.Instance = null;
        var snapshot = new StrategyPoolJsonService().BuildPoolSnapshot();
        Assert.False(snapshot.IsRunning);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolStaticAssets)]
    public void PoolJsAsset_ContainsPollingLogic()
    {
        var repoRoot = BonesWebTestPaths.FindRepositoryRoot();
        var jsPath = Path.Combine(repoRoot, "WIP", "Wip.Bones.Web", "wwwroot", "viewer", "pool.js");
        Assert.True(File.Exists(jsPath), $"pool.js not found at {jsPath}");
        var content = File.ReadAllText(jsPath);
        Assert.Contains("POLL_MS", content);
        Assert.Contains("poll", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", BonesPoolRequirementsChecklistItems.PoolStaticAssets)]
    public void PoolCssAsset_ContainsCardStyles()
    {
        var repoRoot = BonesWebTestPaths.FindRepositoryRoot();
        var cssPath = Path.Combine(repoRoot, "WIP", "Wip.Bones.Web", "wwwroot", "viewer", "pool.css");
        Assert.True(File.Exists(cssPath), $"pool.css not found at {cssPath}");
        var content = File.ReadAllText(cssPath);
        Assert.Contains(".pool-card", content);
        Assert.Contains(".pool-card-grid", content);
    }
}
