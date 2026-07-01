using System.Text.Json;
using Microsoft.Playwright;
using Wip.Bones.Web.Tests;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests.Viewer;

public sealed class BonesViewerDominoRenderingScriptTests
{
    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.StandardPipLayoutSync)]
    public async Task BonesViewerScript_GivenPipPositionsTable_ExpectedMatchesStandardDominoPipLayout()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        var parsed = BonesViewerScriptPipPositionsParser.Parse(script);
        Assert.Equal(7, parsed.Count);

        for (var pipCount = 0; pipCount <= 6; pipCount++)
        {
            Assert.True(parsed.ContainsKey(pipCount), $"viewer.js pipPositions must define count {pipCount}.");
            Assert.Equal(
                BonesStandardDominoPipLayout.GetGridPositions(pipCount),
                parsed[pipCount]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.StandardPipLayoutSync)]
    public void BonesStandardDominoPipLayout_GivenCountsZeroThroughSix_ExpectedStandardGridPositions(int pipCount)
    {
        var positions = BonesStandardDominoPipLayout.GetGridPositions(pipCount);
        Assert.Equal(pipCount, positions.Count);
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.BlankTileRenderFix)]
    public async Task BonesViewerScript_GivenStringPipInputs_ExpectedCreateDominoHalfRendersVisiblePips()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("function coercePipCount", script, StringComparison.Ordinal);
        Assert.Contains("function resolveFacingPip", script, StringComparison.Ordinal);
        Assert.Contains("pipPositions[pipCount] ?? []", script, StringComparison.Ordinal);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await browser.NewPageAsync();
        try
        {
            await page.GotoAsync("about:blank");
            var result = await page.EvaluateAsync<JsonElement>(
                @"async (scriptText) => {
                    const pipPositionsMatch = scriptText.match(/const pipPositions = (\{[\s\S]*?\n  \});/);
                    if (!pipPositionsMatch) {
                        throw new Error('pipPositions table missing');
                    }

                    const pipPositions = eval('(' + pipPositionsMatch[1] + ')');
                    const fnStart = scriptText.indexOf('function coercePipCount');
                    const fnEnd = scriptText.indexOf('function createSeatMarker');
                    if (fnStart < 0 || fnEnd < 0) {
                        throw new Error('pip render helpers missing');
                    }

                    const fnBlock = scriptText.slice(fnStart, fnEnd);
                    const factory = new Function('pipPositions', fnBlock + `
                        const lowHalf = createDominoHalf('domino-half-low', '3');
                        const highHalf = createDominoHalf('domino-half-high', 5);
                        return {
                            lowHalfPip: lowHalf.dataset.pip,
                            lowHalfVisiblePipCount: lowHalf.querySelectorAll('.pip').length,
                            highHalfPip: highHalf.dataset.pip,
                            highHalfVisiblePipCount: highHalf.querySelectorAll('.pip').length,
                        };
                    `);
                    return factory(pipPositions);
                }",
                script);

            Assert.Equal("3", result.GetProperty("lowHalfPip").GetString());
            Assert.Equal(3, result.GetProperty("lowHalfVisiblePipCount").GetInt32());
            Assert.Equal("5", result.GetProperty("highHalfPip").GetString());
            Assert.Equal(5, result.GetProperty("highHalfVisiblePipCount").GetInt32());
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.ClientFineGridParity)]
    public async Task BonesViewerScript_GivenFrameWithFineGridFields_ExpectedCreateBoardTileUsesFineColumnsNotLegacy()
    {
        using var factory = new BonesWebApplicationFactory();
        using var client = factory.CreateClient();
        var script = await (await client.GetAsync("/viewer/viewer.js")).Content.ReadAsStringAsync();

        Assert.Contains("fineColumnCount", script, StringComparison.Ordinal);

        const int fineColumnStart = 5;
        const int fineColumnEnd = 7;
        const int fineRowStart = 1;
        const int fineRowEnd = 3;
        const int gridX = 1;
        const int minGridX = -3;
        var legacyColumnStart = (gridX - minGridX) * 2 + 1;

        Assert.NotEqual(fineColumnStart, legacyColumnStart);

        var browser = await BonesViewerScriptTestDriver.GetSharedBrowserAsync();
        var page = await browser.NewPageAsync();
        try
        {
            await page.GotoAsync("about:blank");
            var result = await page.EvaluateAsync<JsonElement>(
                @"async (args) => {
                    const scriptText = args.scriptText;
                    const fnStart = scriptText.indexOf('function formatGridPlacementStyle');
                    const fnEnd = scriptText.indexOf('function createBoardTile');
                    if (fnStart < 0 || fnEnd < 0) {
                        throw new Error('formatGridPlacementStyle missing');
                    }

                    const fnBlock = scriptText.slice(fnStart, fnEnd);
                    const factory = new Function('placement', 'boardLayout', fnBlock + `
                        const gridPlacement = formatGridPlacementStyle(placement, boardLayout);
                        return gridPlacement;
                    `);
                    return factory(args.placement, args.boardLayout);
                }",
                new
                {
                    scriptText = script,
                    placement = new
                    {
                        gridX,
                        gridY = 0,
                        fineColumnStart,
                        fineColumnEnd,
                        fineRowStart,
                        fineRowEnd,
                        orientation = "vertical",
                        playedBySeat = 1,
                        facingLowPip = 2,
                        facingHighPip = 4,
                        lowPip = 2,
                        highPip = 4,
                        isDouble = false,
                        pipAxis = "horizontal",
                    },
                    boardLayout = new
                    {
                        minGridX,
                        minGridY = 0,
                        fineColumnCount = 8,
                        fineRowCount = 4,
                    },
                });

            Assert.Equal(fineColumnStart.ToString(), result.GetProperty("columnStart").GetString());
            Assert.Equal(fineColumnEnd.ToString(), result.GetProperty("columnEnd").GetString());
            Assert.Equal(fineRowStart.ToString(), result.GetProperty("rowStart").GetString());
            Assert.Equal(fineRowEnd.ToString(), result.GetProperty("rowEnd").GetString());
            Assert.NotEqual(legacyColumnStart.ToString(), result.GetProperty("columnStart").GetString());
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}