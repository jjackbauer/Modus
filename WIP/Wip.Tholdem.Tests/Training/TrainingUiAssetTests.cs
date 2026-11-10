using Wip.Tholdem.Tests;
using Xunit;

namespace Wip.Tholdem.Tests.Training;

/// <summary>
/// Static contract for the Training tab: the elements and endpoints exist, every dynamic value is
/// written with textContent, the promote flow asks for confirmation and there is no external asset.
/// </summary>
public sealed class TrainingUiAssetTests
{
    private static string ViewerDirectory() =>
        Path.Combine(AgentTestSupport.RepositoryRoot(), "WIP", "Wip.Tholdem.Web", "wwwroot", "viewer");

    [Fact]
    public void Index_GivenTrainingTab_ExpectedFormTimelineAndPromoteContract()
    {
        var html = File.ReadAllText(Path.Combine(ViewerDirectory(), "index.html"));
        Assert.Contains("data-view=\"training\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"view-training\"", html, StringComparison.Ordinal);
        foreach (var id in new[]
                 {
                     "training-form", "training-seed", "training-iterations", "training-hands",
                     "training-eval-seeds", "training-eval-hands", "training-margin", "training-min-seeds",
                     "training-strategy-id", "training-allow-weak", "training-start", "training-cancel",
                     "training-status", "training-timeline", "training-entries", "training-promote",
                     "training-champion", "training-message",
                     "rsi-form", "rsi-seed", "rsi-meta-iterations", "rsi-inner-iterations",
                     "rsi-meta-seeds", "rsi-eval-seeds", "rsi-margin", "rsi-min-seeds",
                     "rsi-start", "rsi-cancel", "rsi-promote", "rsi-status", "rsi-message",
                 })
        {
            Assert.Contains("id=\"" + id + "\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("<kbd>5</kbd>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_GivenTrainingFlow_ExpectedTextContentOnlyAndNoExternalAsset()
    {
        var script = File.ReadAllText(Path.Combine(ViewerDirectory(), "app.js"));
        foreach (var token in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "eval(", "document.write" })
        {
            Assert.DoesNotContain(token, script, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("http://", script, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", script, StringComparison.Ordinal);
        Assert.Contains("/api/training", script, StringComparison.Ordinal);
        Assert.Contains("/api/rsi", script, StringComparison.Ordinal);
        Assert.Contains("/api/champion", script, StringComparison.Ordinal);
        Assert.Contains("window.confirm", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
        Assert.Contains("confirm: true", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Style_GivenViewerCss_ExpectedNoExternalImport()
    {
        var css = File.ReadAllText(Path.Combine(ViewerDirectory(), "viewer.css"));
        Assert.DoesNotContain("@import", css, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", css, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", css, StringComparison.Ordinal);
        Assert.Contains(".training-", css, StringComparison.Ordinal);
    }
}
