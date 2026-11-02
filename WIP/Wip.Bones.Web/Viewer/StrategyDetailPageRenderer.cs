using System.Net;
using System.Text;

namespace Wip.Bones.Web.Viewer;

/// <summary>
/// Server-renders the strategy detail page at /bones/pool/{strategyId}/view.
/// Mirrors <see cref="BonesViewerPageRenderer"/> and <see cref="StrategyPoolPageRenderer"/> in pattern.
/// </summary>
public static class StrategyDetailPageRenderer
{
    private const string Placeholder = "\u2014";

    public static string Render(StrategyDetailViewModel detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine($"  <title>Strategy {WebUtility.HtmlEncode(detail.StrategyId)} — Detail</title>");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/viewer.css\" />");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/pool.css\" />");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main class=\"strategy-detail\" aria-label=\"Strategy detail\">");

        // Header
        builder.AppendLine("    <header class=\"viewer-header\">");
        builder.AppendLine($"      <h1>Strategy {WebUtility.HtmlEncode(detail.StrategyId)}</h1>");
        builder.AppendLine($"      <p class=\"pool-summary\">Seat {detail.Seat} | {WebUtility.HtmlEncode(detail.Kind)} | ELO: {detail.Rating:F0} | Win Rate: {detail.WinRate:P1}</p>");
        if (detail.IsElite)
        {
            builder.AppendLine("      <span class=\"badge badge-elite\">\u2605 Elite</span>");
        }
        builder.AppendLine("    </header>");

        // Navigation
        builder.AppendLine("    <p class=\"pool-nav\">");
        builder.AppendLine("      <a href=\"/bones/pool/view\">\u2190 Back to pool dashboard</a>");
        builder.AppendLine("      <a href=\"/viewer/index.html\">\u2190 Back to match viewer</a>");
        builder.AppendLine("    </p>");

        // Stats panel
        builder.AppendLine("    <section class=\"detail-stats\" aria-label=\"Strategy statistics\">");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">Seat</span><span class=\"stat-value\">{detail.Seat}</span></div>");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">Kind</span><span class=\"stat-value\">{WebUtility.HtmlEncode(detail.Kind)}</span></div>");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">ELO</span><span class=\"stat-value\">{detail.Rating:F0}</span></div>");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">Win Rate</span><span class=\"stat-value\">{detail.WinRate:P1}</span></div>");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">Matches</span><span class=\"stat-value\">{detail.MatchesPlayed}</span></div>");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">W/L</span><span class=\"stat-value\">{detail.Wins}/{detail.Losses}</span></div>");
        builder.AppendLine($"      <div class=\"detail-stat\"><span class=\"stat-label\">Score Diff</span><span class=\"stat-value\">{detail.CumulativeScoreDifferential:+#;-#;0}</span></div>");
        if (detail.IsElite)
        {
            builder.AppendLine("      <div class=\"detail-stat detail-elite\"><span class=\"stat-label\">\u2605 Elite</span></div>");
        }
        builder.AppendLine("    </section>");

        // Source code
        builder.AppendLine("    <section class=\"detail-source\" aria-label=\"Strategy source code\">");
        builder.AppendLine("      <h2>Source Code</h2>");
        builder.AppendLine($"      <pre class=\"pool-card-source\"><code class=\"language-csharp\">{WebUtility.HtmlEncode(detail.Source)}</code></pre>");
        builder.AppendLine("    </section>");

        // Lineage
        if (detail.Lineage is not null)
        {
            builder.AppendLine("    <section class=\"detail-lineage\" aria-label=\"Strategy lineage\">");
            builder.AppendLine("      <h2>Lineage</h2>");
            builder.AppendLine("      <div class=\"pool-card-lineage\">");
            if (detail.Lineage.PredecessorId is not null)
            {
                builder.AppendLine($"        <div class=\"lineage-entry\"><span class=\"lineage-role\">Predecessor</span><a href=\"/bones/pool/{WebUtility.HtmlEncode(detail.Lineage.PredecessorId)}/view\">{WebUtility.HtmlEncode(detail.Lineage.PredecessorId)}</a></div>");
            }
            if (detail.Lineage.SecondPredecessorId is not null)
            {
                builder.AppendLine($"        <div class=\"lineage-entry\"><span class=\"lineage-role\">Second Parent</span><a href=\"/bones/pool/{WebUtility.HtmlEncode(detail.Lineage.SecondPredecessorId)}/view\">{WebUtility.HtmlEncode(detail.Lineage.SecondPredecessorId)}</a></div>");
            }
            builder.AppendLine($"        <div class=\"lineage-entry\"><span class=\"lineage-role\">Outcome</span><span class=\"lineage-value\">{WebUtility.HtmlEncode(detail.Lineage.Outcome)}</span></div>");
            builder.AppendLine($"        <div class=\"lineage-entry\"><span class=\"lineage-role\">Diff</span><span class=\"lineage-value\">{WebUtility.HtmlEncode(detail.Lineage.DiffSummary)}</span></div>");
            if (detail.Lineage.SuccessorIds.Count > 0)
            {
                builder.AppendLine("        <div class=\"lineage-entry\"><span class=\"lineage-role\">Successors</span><span class=\"lineage-value\">");
                foreach (var su in detail.Lineage.SuccessorIds)
                {
                    builder.AppendLine($"          <a href=\"/bones/pool/{WebUtility.HtmlEncode(su)}/view\">{WebUtility.HtmlEncode(su)}</a> ");
                }
                builder.AppendLine("        </span></div>");
            }
            builder.AppendLine("      </div>");
            builder.AppendLine("    </section>");
        }

        // Opponent summaries
        if (detail.OpponentSummaries.Count > 0)
        {
            builder.AppendLine("    <section class=\"detail-opponents\" aria-label=\"Opponent summaries\">");
            builder.AppendLine("      <h2>Opponents</h2>");
            builder.AppendLine("      <table class=\"pool-card-opponents\">");
            builder.AppendLine("        <thead><tr><th>Strategy</th><th>Kind</th><th>ELO</th><th>Games</th><th>W</th><th>L</th></tr></thead>");
            builder.AppendLine("        <tbody>");
            foreach (var o in detail.OpponentSummaries)
            {
                builder.AppendLine($"          <tr><td><a href=\"/bones/pool/{WebUtility.HtmlEncode(o.OpponentStrategyId)}/view\">{WebUtility.HtmlEncode(o.OpponentStrategyId)}</a></td><td>{WebUtility.HtmlEncode(o.OpponentKind)}</td><td>{o.OpponentElo:F0}</td><td>{o.GamesAgainst}</td><td>{o.WinsAgainst}</td><td>{o.LossesAgainst}</td></tr>");
            }
            builder.AppendLine("        </tbody>");
            builder.AppendLine("      </table>");
            builder.AppendLine("    </section>");
        }

        // Recent matches
        if (detail.RecentMatches.Count > 0)
        {
            builder.AppendLine("    <section class=\"detail-matches\" aria-label=\"Recent matches\">");
            builder.AppendLine("      <h2>Recent Matches</h2>");
            builder.AppendLine("      <div class=\"pool-card-match-links\">");
            foreach (var m in detail.RecentMatches)
            {
                builder.AppendLine($"        <a href=\"/bones/sessions/{WebUtility.HtmlEncode(m.SessionId)}/matches/{WebUtility.HtmlEncode(m.MatchId)}/view\">Match {WebUtility.HtmlEncode(m.MatchId)}</a>");
            }
            builder.AppendLine("      </div>");
            builder.AppendLine("    </section>");
        }

        builder.AppendLine("  </main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    /// <summary>
    /// Renders a 404 page for an unknown strategy.
    /// </summary>
    public static string RenderNotFound(string strategyId)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <title>Strategy Not Found</title>");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/viewer.css\" />");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/pool.css\" />");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main class=\"strategy-detail\">");
        builder.AppendLine("    <header class=\"viewer-header\">");
        builder.AppendLine($"      <h1>Strategy Not Found: {WebUtility.HtmlEncode(strategyId)}</h1>");
        builder.AppendLine("      <p>The requested strategy does not exist in the pool.</p>");
        builder.AppendLine("    </header>");
        builder.AppendLine("    <p class=\"pool-nav\">");
        builder.AppendLine("      <a href=\"/bones/pool/view\">\u2190 Back to pool dashboard</a>");
        builder.AppendLine("      <a href=\"/viewer/index.html\">\u2190 Back to match viewer</a>");
        builder.AppendLine("    </p>");
        builder.AppendLine("  </main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }
}
