using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesViewerTransformCssDomTests : IClassFixture<BonesWebApplicationFactory>
{
    private const string ChecklistItem =
        BonesViewerBoardChainLayoutRequirementsChecklistItems.TransformCssInnerWrapper;

    private readonly BonesWebApplicationFactory _factory;

    public BonesViewerTransformCssDomTests(BonesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerCss_GivenBoardChainInner_ExpectedTransformScaleNotCellShrink()
    {
        using var client = _factory.CreateClient();
        var css = await (await client.GetAsync("/viewer/viewer.css")).Content.ReadAsStringAsync();

        Assert.Contains(".board-chain-inner", css, StringComparison.Ordinal);
        Assert.Contains("transform: scale(var(--board-chain-scale))", css, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "calc(var(--board-tile-unit) * var(--board-chain-scale))",
            css,
            StringComparison.Ordinal);

        var innerCss = ExtractCssBlock(css, ".board-chain-inner");
        Assert.Contains("--board-cell-size: var(--board-tile-unit)", innerCss, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerPage_GivenLongChain_ExpectedBoardChainInnerWrapperWithOccupiedExtent()
    {
        var (_, matchId, expected) = await RegisterLongChainMatchAsync();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/bones/sessions/{expected.SessionId}/matches/{matchId.Value}/view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ParseHtmlAsync(await response.Content.ReadAsStringAsync());
        var boardChain = document.GetElementById("board-chain");
        Assert.NotNull(boardChain);

        var inner = boardChain!.QuerySelector(".board-chain-inner");
        Assert.NotNull(inner);
        Assert.True(inner!.Children.Length >= 12);

        var style = boardChain.GetAttribute("style") ?? string.Empty;
        Assert.Contains("--board-chain-occupied-width", style, StringComparison.Ordinal);
        Assert.Contains("--board-chain-occupied-height", style, StringComparison.Ordinal);
        Assert.True(expected.BoardLayout.OccupiedWidthPixels > 0);
        Assert.Contains(
            expected.BoardLayout.OccupiedWidthPixels.ToString(),
            style,
            StringComparison.Ordinal);

        var scaleText = boardChain.GetAttribute("data-board-chain-scale");
        Assert.NotNull(scaleText);
        Assert.True(double.Parse(scaleText!) < 1.0);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesViewerScript_GivenRenderBoardChain_ExpectedBoardChainInnerWrapper()
    {
        using var client = _factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("board-chain-inner", script, StringComparison.Ordinal);
        Assert.Contains("--board-chain-occupied-width", script, StringComparison.Ordinal);
        Assert.Contains("--board-chain-occupied-height", script, StringComparison.Ordinal);
    }

    private async Task<(SessionId SessionId, BonesGameId MatchId, BonesMatchViewModel Expected)> RegisterLongChainMatchAsync()
    {
        const string sessionId = "session-transform-css";
        var matchId = new BonesGameId("match-transform-css");
        var (seed, matchResult) = CreateLongChainSeededMatch(matchId, minimumTileCount: 12);

        _factory.Catalog.Register(new BonesRegisteredMatch(
            new SessionId(sessionId),
            matchId,
            seed,
            matchResult));

        var viewer = _factory.Services.GetRequiredService<BonesMatchViewerService>();
        Assert.True(_factory.Catalog.TryGet(new SessionId(sessionId), matchId, out var registered));
        var snapshot = viewer.BuildMatchSnapshot(new SessionId(sessionId), registered);
        Assert.True(snapshot.BoardLayout.Tiles.Count >= 12);

        return (new SessionId(sessionId), matchId, snapshot);
    }

    private static (int Seed, BonesMatchResult MatchResult) CreateLongChainSeededMatch(
        BonesGameId matchId,
        int minimumTileCount)
    {
        for (var seed = 1; seed <= 250; seed++)
        {
            var matchResult = CreateSeededMatch(matchId, seed, targetScore: 5);
            if (matchResult.Rounds.Length == 0)
                continue;

            var lastRound = matchResult.Rounds[^1];
            var engine = new BonesGameEngine();
            var roundConfig = new BonesRoundConfig(
                new BonesGameId($"{matchId.Value}-r{lastRound.RoundNumber}"),
                HashCode.Combine(seed, lastRound.RoundNumber));
            var state = engine.StartRound(roundConfig);

            foreach (var roundEvent in lastRound.EventLog)
            {
                var move = roundEvent.Kind == BonesEventKind.Pass
                    ? BonesMove.Pass(
                        new BonesMoveId($"{roundConfig.GameId.Value}-pass-{roundEvent.TurnIndex}"),
                        roundEvent.PlayerId)
                    : BonesMove.Play(
                        new BonesMoveId($"{roundConfig.GameId.Value}-play-{roundEvent.TurnIndex}"),
                        roundEvent.PlayerId,
                        roundEvent.Tile!.Value,
                        roundEvent.Side!.Value);
                state = engine.ApplyMove(state, move);
            }

            if (state.Board.Tiles.Length >= minimumTileCount)
                return (seed, matchResult);
        }

        throw new InvalidOperationException(
            $"Unable to locate a seeded match with at least {minimumTileCount} board tiles.");
    }

    private static string ExtractCssBlock(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Selector '{selector}' not found in CSS.");
        var braceStart = css.IndexOf('{', start);
        var depth = 0;
        for (var index = braceStart; index < css.Length; index++)
        {
            if (css[index] == '{')
                depth++;
            else if (css[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return css[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Unclosed CSS block for '{selector}'.");
    }

    private static async Task<IDocument> ParseHtmlAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(request => request.Content(html));
    }

    private static BonesMatchResult CreateSeededMatch(BonesGameId matchId, int seed, int targetScore)
    {
        var simulator = new BonesMatchSimulator();
        var config = new BonesMatchConfig(
            matchId,
            seed,
            targetScore,
            CreateFirstLegalMoveSlots());
        return simulator.RunMatch(config);
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
}
