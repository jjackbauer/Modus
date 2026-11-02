namespace Wip.Bones.Web.Tests;

internal static class BonesPoolRequirementsChecklistItems
{
    public const string PoolApi = "Add strategy pool HTTP API endpoints under /bones/pool/* [mandatory - user specified]";
    public const string PoolDashboard = "Add StrategyPoolPageRenderer dashboard page with expandable cards [mandatory - user specified]";
    public const string PoolJsonService = "Add StrategyPoolJsonService bridging StrategyPool.Instance to JSON view models [mandatory - behavior-proof]";
    public const string PoolNavigation = "Add navigation link and learning-loop status to pool dashboard [mandatory - UX continuity]";
    public const string PoolStaticAssets = "Add pool.css and pool.js static assets [mandatory - UI consistency]";
    public const string PoolViewModels = "Add typed view model records for pool visualization [mandatory - typed DTO]";
}
