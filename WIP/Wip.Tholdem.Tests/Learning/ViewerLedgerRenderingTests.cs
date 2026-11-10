using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

/// <summary>
/// TODO-006: the promotion ledger renders as nine labelled columns and every cell is written with
/// <c>textContent</c>, so artifact content can never become markup.
/// </summary>
public sealed class ViewerLedgerRenderingTests
{
    private static readonly string[] LedgerLabels =
    [
        "Iteration",
        "Champion",
        "Challenger",
        "Promoted",
        "Reason",
        "Win rate",
        "bb/100",
        "LCB Δ",
        "Samples",
    ];

    [Fact]
    public void ViewerAssets_GivenLedgerPanel_ExpectedNineColumnLabelsAndTextOnlyCells()
    {
        var viewerDirectory = Path.Combine(
            AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Web", "wwwroot", "viewer");
        var html = File.ReadAllText(Path.Combine(viewerDirectory, "index.html"));
        var script = File.ReadAllText(Path.Combine(viewerDirectory, "app.js"));

        foreach (var label in LedgerLabels)
        {
            Assert.Contains("<th>" + label + "</th>", html);
        }

        Assert.Contains("id=\"ledger\"", html);
        Assert.Contains("id=\"strategy-detail\"", html);
        Assert.Contains("id=\"strategies\"", html);
        Assert.Equal(9, LedgerLabels.Length);

        foreach (var field in new[] { "lowerBoundBbPer100", "meanBbPer100", "seeds", "reason", "promoted", "winRate" })
        {
            Assert.Contains(field, script);
        }

        var ledgerStart = script.IndexOf("function renderLedger", StringComparison.Ordinal);
        Assert.True(ledgerStart >= 0, "app.js must expose a renderLedger function.");
        var nextFunction = script.IndexOf("\nfunction ", ledgerStart + 1, StringComparison.Ordinal);
        var ledgerBlock = nextFunction < 0 ? script[ledgerStart..] : script[ledgerStart..nextFunction];

        Assert.DoesNotContain("innerHTML", ledgerBlock);
        Assert.Contains("textContent", ledgerBlock);
        Assert.Contains("document.createElement('td')", ledgerBlock);
        Assert.Contains("appendChild", ledgerBlock);

        // The detail panel still shows the selected artifact as text, never as markup.
        var detailStart = script.IndexOf("function renderStrategyDetail", StringComparison.Ordinal);
        Assert.True(detailStart >= 0, "app.js must expose a renderStrategyDetail function.");
        var detailEnd = script.IndexOf("\nfunction ", detailStart + 1, StringComparison.Ordinal);
        var detailBlock = detailEnd < 0 ? script[detailStart..] : script[detailStart..detailEnd];
        Assert.Contains("textContent", detailBlock);
        Assert.DoesNotContain("innerHTML", detailBlock);
    }
}
