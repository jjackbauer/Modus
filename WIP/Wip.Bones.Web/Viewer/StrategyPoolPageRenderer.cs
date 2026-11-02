using System.Globalization;
using System.Net;
using System.Text;

namespace Wip.Bones.Web.Viewer;

/// <summary>
/// Server-renders the strategy pool dashboard page at /bones/pool/view.
/// Mirrors <see cref="BonesViewerPageRenderer"/> in pattern.
/// </summary>
public static class StrategyPoolPageRenderer
{
    private const string Placeholder = "\u2014";

    public static string Render(PoolSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("  <title>Strategy Pool Dashboard</title>");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/viewer.css\" />");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/pool.css\" />");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main id=\"pool-dashboard\" class=\"pool-dashboard\"");
        builder.AppendLine($"    data-is-running=\"{FormatBoolAttr(snapshot.IsRunning)}\"");
        builder.AppendLine($"    data-entry-count=\"{snapshot.Entries.Count}\">");
        builder.AppendLine("    <header class=\"viewer-header\">");
        builder.AppendLine("      <h1>Strategy Pool Dashboard</h1>");
        builder.AppendLine($"      <p class=\"pool-summary\">{snapshot.Entries.Count} strategies | {snapshot.Stats.TotalMatchesPlayed} matches | Avg ELO: {snapshot.Stats.AverageElo:F0}</p>");
        builder.AppendLine("    </header>");

        // Learning loop status bar
        builder.AppendLine("    <aside class=\"pool-sidebar\" aria-label=\"Pool sidebar\">");
        AppendLearningLoopStatus(builder, "    ");
        builder.AppendLine($"      <p class=\"pool-nav\"><a href=\"/viewer/index.html\">\u2190 Back to match viewer</a></p>");
        builder.AppendLine("    </aside>");

        // Card grid
        builder.AppendLine("    <section class=\"pool-main\" aria-label=\"Strategy cards\">");
        builder.AppendLine("      <div class=\"pool-card-grid\">");

        for (var i = 0; i < snapshot.Entries.Count; i++)
        {
            var entry = snapshot.Entries[i];
            var encodedId = WebUtility.HtmlEncode(entry.Id);
            builder.AppendLine($"        <article class=\"pool-card\" data-strategy-id=\"{encodedId}\" data-seat=\"{entry.Seat}\">");
            builder.AppendLine("          <header class=\"pool-card-header\">");
            builder.AppendLine($"            <a class=\"pool-card-nav-link\" href=\"/bones/pool/{encodedId}/view\"><h2 class=\"pool-card-title\">{encodedId}</h2></a>");
            builder.AppendLine($"            <span class=\"pool-card-seat badge badge-seat-{entry.Seat}\">Seat {entry.Seat}</span>");
            builder.AppendLine($"            <span class=\"pool-card-kind badge badge-kind\">{WebUtility.HtmlEncode(entry.Kind)}</span>");
            if (entry.IsElite)
            {
                builder.AppendLine("            <span class=\"pool-card-elite badge badge-elite\">\u2605 Elite</span>");
            }
            builder.AppendLine("          </header>");
            builder.AppendLine("          <div class=\"pool-card-stats\">");
            builder.AppendLine($"            <div class=\"pool-stat\"><span class=\"stat-label\">ELO</span><span class=\"stat-value\">{entry.Rating:F0}</span></div>");
            builder.AppendLine($"            <div class=\"pool-stat\"><span class=\"stat-label\">Win Rate</span><span class=\"stat-value\">{entry.WinRate:P1}</span></div>");
            builder.AppendLine($"            <div class=\"pool-stat\"><span class=\"stat-label\">Matches</span><span class=\"stat-value\">{entry.MatchesPlayed}</span></div>");
            builder.AppendLine($"            <div class=\"pool-stat\"><span class=\"stat-label\">W/L</span><span class=\"stat-value\">{entry.Wins}/{entry.Losses}</span></div>");
            builder.AppendLine($"            <div class=\"pool-stat\"><span class=\"stat-label\">Score Diff</span><span class=\"stat-value\">{entry.CumulativeScoreDifferential:+#;-#;0}</span></div>");
            builder.AppendLine($"            <div class=\"pool-stat\"><span class=\"stat-label\">Iterations</span><span class=\"stat-value\">{entry.IterationCount}</span></div>");
            builder.AppendLine("          </div>");
            builder.AppendLine("          <div class=\"pool-card-detail hidden\">");
            builder.AppendLine($"            <pre class=\"pool-card-source\"><code class=\"language-csharp\">{WebUtility.HtmlEncode(GetSourcePreview(entry.Id))}</code></pre>");
            builder.AppendLine("            <div class=\"pool-card-lineage\"></div>");
            builder.AppendLine("            <div class=\"pool-card-matches\">");
            builder.AppendLine($"              <a class=\"pool-card-link\" href=\"/bones/pool/{encodedId}/view\">View full detail \u2192</a>");
            builder.AppendLine("            </div>");
            builder.AppendLine("          </div>");
            builder.AppendLine("        </article>");
        }

        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");
        builder.AppendLine("  </main>");
        builder.AppendLine("  <script src=\"/viewer/pool.js\"></script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    /// <summary>
    /// Renders a page when the pool is uninitialized.
    /// </summary>
    public static string RenderUninitialized()
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <title>Strategy Pool — Uninitialized</title>");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/viewer.css\" />");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"/viewer/pool.css\" />");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main id=\"pool-dashboard\" class=\"pool-dashboard\">");
        builder.AppendLine("    <header class=\"viewer-header\">");
        builder.AppendLine("      <h1>Strategy Pool Dashboard</h1>");
        builder.AppendLine("      <p class=\"pool-uninitialized\">The strategy pool has not been initialized. Start the learning loop to seed the pool.</p>");
        builder.AppendLine("    </header>");
        builder.AppendLine("    <p class=\"pool-nav\"><a href=\"/viewer/index.html\">\u2190 Back to match viewer</a></p>");
        builder.AppendLine("  </main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendLearningLoopStatus(StringBuilder builder, string indent)
    {
        builder.AppendLine($"{indent}<section id=\"learning-loop-status\" class=\"learning-loop-status\" aria-label=\"Learning loop status\"");
        builder.AppendLine($"{indent}  data-is-running=\"\"");
        builder.AppendLine($"{indent}  data-iteration-count=\"\"");
        builder.AppendLine($"{indent}  data-current-stage=\"\">");
        builder.AppendLine($"{indent}  <p id=\"loop-running-status\" class=\"loop-running-status\" data-is-running=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Loop</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-running-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-iteration\" class=\"loop-iteration\" data-iteration-count=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Iteration</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-iteration-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}  <p id=\"loop-stage\" class=\"loop-stage\" data-current-stage=\"\">");
        builder.AppendLine($"{indent}    <span class=\"status-label\">Stage</span>");
        builder.AppendLine($"{indent}    <span class=\"status-value\" id=\"loop-stage-value\">{Placeholder}</span>");
        builder.AppendLine($"{indent}  </p>");
        builder.AppendLine($"{indent}</section>");
    }

    private static string FormatBoolAttr(bool value) => value ? "true" : "false";

    private static string GetSourcePreview(string strategyId)
    {
        // Source preview is filled in by client JavaScript from the API
        return $"-- Loaded via /bones/pool/{strategyId} --";
    }
}
