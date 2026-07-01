using AngleSharp;
using AngleSharp.Dom;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerLearningLoopStatusDomTests : IClassFixture<BonesWebApplicationFactory>
{
    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerLearningLoopStatusDomTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.SidebarHtmlSkeleton)]
    public async Task BonesViewerSpaIndex_GivenStaticAssets_ExpectedLearningLoopStatusAtTopOfSidebar()
    {
        using var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/viewer/index.html")).Content.ReadAsStringAsync();
        var document = await ParseHtmlAsync(html);

        var sidebar = document.QuerySelector(".viewer-sidebar");
        Assert.NotNull(sidebar);

        var panel = sidebar!.QuerySelector("#learning-loop-status");
        Assert.NotNull(panel);
        Assert.Contains("learning-loop-status", panel!.ClassName, StringComparison.Ordinal);
        Assert.Equal("Learning loop status", panel.GetAttribute("aria-label"));

        var hands = sidebar.QuerySelector(".hands");
        Assert.NotNull(hands);
        Assert.Same(panel, sidebar.FirstElementChild);
        Assert.True((panel.CompareDocumentPosition(hands!) & DocumentPositions.Following) != 0);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.SidebarHtmlSkeleton)]
    public async Task BonesViewerSpaIndex_GivenStaticAssets_ExpectedPanelAggregateDataHooks()
    {
        using var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/viewer/index.html")).Content.ReadAsStringAsync();
        var panel = GetLearningLoopStatusPanel(await ParseHtmlAsync(html));

        Assert.NotNull(panel.GetAttribute("data-is-running"));
        Assert.NotNull(panel.GetAttribute("data-cap-reached"));
        Assert.NotNull(panel.GetAttribute("data-iteration-count"));
        Assert.NotNull(panel.GetAttribute("data-current-stage"));
        Assert.NotNull(panel.GetAttribute("data-failure-stage"));
        Assert.NotNull(panel.GetAttribute("data-games-simulated"));
        Assert.NotNull(panel.GetAttribute("data-max-games-per-run"));
        Assert.NotNull(panel.GetAttribute("data-learning-player-status"));
        Assert.NotNull(panel.GetAttribute("data-turn-index"));
        Assert.NotNull(panel.GetAttribute("data-frame-count"));
        Assert.NotNull(panel.GetAttribute("data-is-complete"));
        Assert.NotNull(panel.GetAttribute("data-winner-seat"));
        Assert.NotNull(panel.GetAttribute("data-last-promotion-outcome"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.SidebarHtmlSkeleton)]
    public async Task BonesViewerSpaIndex_GivenStaticAssets_ExpectedFieldGroupHooksForSidebarStatusBaseline()
    {
        using var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/viewer/index.html")).Content.ReadAsStringAsync();
        var panel = GetLearningLoopStatusPanel(await ParseHtmlAsync(html));

        AssertFieldGroup(
            panel,
            elementId: "loop-running-status",
            requiredDataAttributes: ["data-is-running", "data-cap-reached"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-iteration",
            requiredDataAttributes: ["data-iteration-count"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-stage",
            requiredDataAttributes: ["data-current-stage", "data-failure-stage"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-game-budget",
            requiredDataAttributes: ["data-games-simulated", "data-max-games-per-run"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-match-turns",
            requiredDataAttributes: ["data-turn-index", "data-frame-count"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-match-complete",
            requiredDataAttributes: ["data-is-complete", "data-winner-seat"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-learning-player",
            requiredDataAttributes:
            [
                "data-seat",
                "data-active-strategy-id",
                "data-candidate-strategy-id",
                "data-wins",
                "data-losses",
                "data-score-differential",
            ]);

        AssertFieldGroup(
            panel,
            elementId: "loop-ponder-pending",
            requiredDataAttributes: ["data-learning-player-status"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-learning-player-error",
            requiredDataAttributes: ["data-learning-player-error"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-last-iteration-error",
            requiredDataAttributes: ["data-failure-stage"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-last-promotion",
            requiredDataAttributes: ["data-last-promotion-outcome"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-library-best",
            requiredDataAttributes: ["data-library-strategy-id", "data-library-effectiveness"]);

        AssertFieldGroup(
            panel,
            elementId: "loop-model-provider",
            requiredDataAttributes: ["data-provider", "data-model"]);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.CssPanel)]
    public async Task BonesViewerCss_GivenLearningLoopPanel_ExpectedRequiredSelectorsAndSidebarScroll()
    {
        using var client = _factory.CreateClient();
        var css = await (await client.GetAsync("/viewer/viewer.css")).Content.ReadAsStringAsync();

        Assert.Contains(".learning-loop-status", css, StringComparison.Ordinal);
        Assert.Contains(".learning-loop-status .status-label", css, StringComparison.Ordinal);
        Assert.Contains(".learning-loop-status .status-value", css, StringComparison.Ordinal);
        Assert.Contains(".learning-loop-status .status-error", css, StringComparison.Ordinal);
        Assert.Contains(".learning-loop-status .loop-last-promotion", css, StringComparison.Ordinal);
        Assert.Contains(".learning-loop-status.hidden", css, StringComparison.Ordinal);
        Assert.Contains(".learning-loop-status[aria-hidden=\"true\"]", css, StringComparison.Ordinal);
        Assert.Contains(".viewer-sidebar", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", css, StringComparison.Ordinal);

        var panelBlockStart = css.IndexOf(".learning-loop-status {", StringComparison.Ordinal);
        Assert.True(panelBlockStart >= 0);
        var panelBlockEnd = css.IndexOf('}', panelBlockStart);
        var panelBlock = css[panelBlockStart..panelBlockEnd];
        Assert.DoesNotContain("overflow", panelBlock, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.CssPanel)]
    public async Task BonesViewerSpaIndex_GivenStaticAssets_ExpectedPanelCssHookClasses()
    {
        using var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/viewer/index.html")).Content.ReadAsStringAsync();
        var panel = GetLearningLoopStatusPanel(await ParseHtmlAsync(html));

        Assert.Contains("learning-loop-status", panel.ClassName, StringComparison.Ordinal);
        Assert.Contains("hidden", panel.ClassName, StringComparison.Ordinal);

        var labels = panel.QuerySelectorAll(".status-label");
        Assert.True(labels.Length >= 10);

        var values = panel.QuerySelectorAll(".status-value");
        Assert.Equal(labels.Length, values.Length);

        var errorNodes = panel.QuerySelectorAll(".status-error");
        Assert.Equal(2, errorNodes.Length);
        Assert.Equal("loop-learning-player-error", errorNodes[0].Id);
        Assert.Equal("loop-last-iteration-error", errorNodes[1].Id);

        var promotion = panel.QuerySelector(".loop-last-promotion");
        Assert.NotNull(promotion);
        Assert.Contains("hidden", promotion!.ClassName, StringComparison.Ordinal);
        Assert.NotNull(promotion.QuerySelector(".status-label"));
        Assert.NotNull(promotion.QuerySelector(".status-value"));
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.SsrSpaParity)]
    public async Task BonesViewerPage_GivenSnapshotRender_ExpectedLearningLoopStatusSkeletonInsideSidebar()
    {
        const string sessionId = "session-loop-status-ssr";
        var matchId = new BonesGameId("match-loop-status-ssr");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 442);

        using var client = _factory.CreateClient();
        var document = await ParseHtmlAsync(
            await (await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/view"))
                .Content.ReadAsStringAsync());

        var sidebar = document.QuerySelector(".viewer-sidebar");
        Assert.NotNull(sidebar);

        var panel = sidebar!.QuerySelector("#learning-loop-status");
        Assert.NotNull(panel);
        Assert.Contains("learning-loop-status", panel!.ClassName, StringComparison.Ordinal);
        Assert.Contains("hidden", panel.ClassName, StringComparison.Ordinal);
        Assert.Equal("Learning loop status", panel.GetAttribute("aria-label"));
        Assert.Same(panel, sidebar.FirstElementChild);

        var hands = sidebar.QuerySelector(".hands");
        Assert.NotNull(hands);
        Assert.True((panel.CompareDocumentPosition(hands!) & DocumentPositions.Following) != 0);

        Assert.Equal("—", panel.QuerySelector("#loop-running-value")?.TextContent);
        Assert.Equal("—", panel.QuerySelector("#loop-iteration-value")?.TextContent);
        Assert.Equal("—", panel.QuerySelector("#loop-stage-value")?.TextContent);
        Assert.Equal("—", panel.QuerySelector("#loop-game-budget-value")?.TextContent);
        Assert.Equal("—", panel.QuerySelector("#loop-match-turns-value")?.TextContent);
        Assert.Equal("Strategy pending", panel.QuerySelector("#loop-ponder-pending-value")?.TextContent);
        Assert.Equal("—", panel.QuerySelector("#loop-model-provider-value")?.TextContent);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerLearningLoopStatusRequirementsChecklistItems.SsrSpaParity)]
    public async Task BonesViewerPage_GivenStaticSpaIndex_ExpectedLearningLoopStatusSkeletonMatchesSsrStructure()
    {
        const string sessionId = "session-loop-status-parity";
        var matchId = new BonesGameId("match-loop-status-parity");
        RegisterSeededMatch(_factory.Catalog, sessionId, matchId, 9001);

        using var client = _factory.CreateClient();
        var spaDocument = await ParseHtmlAsync(
            await (await client.GetAsync("/viewer/index.html")).Content.ReadAsStringAsync());
        var ssrDocument = await ParseHtmlAsync(
            await (await client.GetAsync($"/bones/sessions/{sessionId}/matches/{matchId.Value}/view"))
                .Content.ReadAsStringAsync());

        var spaPanel = GetLearningLoopStatusPanel(spaDocument);
        var ssrPanel = GetLearningLoopStatusPanel(ssrDocument);

        Assert.Equal(
            CollectPanelStructureSignature(spaPanel),
            CollectPanelStructureSignature(ssrPanel));
    }

    private static IReadOnlyList<string> CollectPanelStructureSignature(IElement panel)
    {
        var signatures = new List<string>
        {
            FormatElementHookSignature(panel),
        };

        foreach (var element in panel.QuerySelectorAll("[id]"))
        {
            if (element.Id == panel.Id)
                continue;

            signatures.Add(FormatElementHookSignature(element));
        }

        return signatures;
    }

    private static string FormatElementHookSignature(IElement element)
    {
        var dataAttributes = element.Attributes
            .Where(attribute => attribute.Name.StartsWith("data-", StringComparison.Ordinal))
            .Select(attribute => attribute.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return $"{element.Id}|{string.Join(',', dataAttributes)}";
    }

    private static void RegisterSeededMatch(IBonesMatchCatalog catalog, string sessionId, BonesGameId matchId, int seed)
    {
        catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            CreateSeededMatch(matchId, seed, 15)));
    }

    private static BonesMatchResult CreateSeededMatch(BonesGameId matchId, int seed, int targetScore)
    {
        var simulator = new BonesMatchSimulator();
        return simulator.RunMatch(new BonesMatchConfig(matchId, seed, targetScore, CreateFirstLegalMoveSlots()));
    }

    private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreateFirstLegalMoveSlots()
    {
        var slot = new BonesFirstLegalMovePlayerSlot();
        return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
        {
            [new BonesPlayerId(1)] = slot,
            [new BonesPlayerId(2)] = slot,
            [new BonesPlayerId(3)] = slot,
            [new BonesPlayerId(4)] = slot,
        };
    }

    private static IElement GetLearningLoopStatusPanel(IDocument document)
    {
        var panel = document.QuerySelector(".viewer-sidebar #learning-loop-status");
        Assert.NotNull(panel);
        return panel!;
    }

    private static void AssertFieldGroup(
        IElement panel,
        string elementId,
        IReadOnlyList<string> requiredDataAttributes)
    {
        var field = panel.QuerySelector($"#{elementId}");
        Assert.NotNull(field);
        Assert.Equal(elementId, field!.Id);

        foreach (var attributeName in requiredDataAttributes)
        {
            Assert.NotNull(field.GetAttribute(attributeName));
        }
    }

    private static async Task<IDocument> ParseHtmlAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(request => request.Content(html));
    }
}
