using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

internal static class BonesViewerScriptTestDriver
{
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;

    public static async Task<IBrowser> GetSharedBrowserAsync()
    {
        if (_browser is not null)
        {
            return _browser;
        }

        await BrowserGate.WaitAsync();
        try
        {
            if (_browser is not null)
            {
                return _browser;
            }

            _playwright = await Playwright.CreateAsync();
            _browser = await TryLaunchChromiumAsync(_playwright);
            return _browser;
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    private static async Task<IBrowser> TryLaunchChromiumAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException)
        {
            Microsoft.Playwright.Program.Main(["install", "chromium"]);
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
    }

    public static async Task<BonesViewerBootstrapState> ReadBootstrapStateAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const root = document.getElementById('bones-viewer');
            const scrubber = document.getElementById('replay-scrubber');
            if (!root || !scrubber) {
                return {
                    sessionId: null,
                    matchId: null,
                    frameCount: null,
                    scrubberMax: null,
                };
            }

            return {
                sessionId: root.dataset.sessionId ?? null,
                matchId: root.dataset.matchId ?? null,
                frameCount: root.dataset.frameCount ?? null,
                scrubberMax: scrubber.max ?? null,
            };
        }");

        return new BonesViewerBootstrapState(
            json.TryGetProperty("sessionId", out var sessionId) ? sessionId.GetString() : null,
            json.TryGetProperty("matchId", out var matchId) ? matchId.GetString() : null,
            json.TryGetProperty("frameCount", out var frameCount) ? frameCount.GetString() : null,
            json.TryGetProperty("scrubberMax", out var scrubberMax) ? scrubberMax.GetString() : null);
    }

    public static async Task<bool> ScrubToTurnAndAwaitFrameAsync(IPage page, int turnIndex)
    {
        var frameResponse = page.WaitForResponseAsync(
            response => response.Url.Contains($"/frames/{turnIndex}", StringComparison.Ordinal)
                && response.Ok,
            new PageWaitForResponseOptions { Timeout = 10_000 });

        await page.EvaluateAsync(
            @"turnIndex => {
                const scrubber = document.getElementById('replay-scrubber');
                scrubber.value = String(turnIndex);
                scrubber.dispatchEvent(new Event('input', { bubbles: true }));
            }",
            turnIndex);

        await frameResponse;

        var labelTurn = await page.Locator("#turn-index").GetAttributeAsync("data-turn-index");
        return labelTurn == turnIndex.ToString(CultureInfo.InvariantCulture);
    }

    public static async Task WaitForBootstrapFetchAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            @"() => {
                const root = document.getElementById('bones-viewer');
                const scrubber = document.getElementById('replay-scrubber');
                return Boolean(
                    root?.dataset?.sessionId &&
                    root?.dataset?.matchId &&
                    scrubber &&
                    Number(scrubber.max) > 0);
            }",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    public static async Task WaitForFrameAtTurnAsync(IPage page, int turnIndex)
    {
        var expectedTurn = turnIndex.ToString(CultureInfo.InvariantCulture);
        await page.WaitForFunctionAsync(
            $@"() => document.getElementById('turn-index')?.dataset?.turnIndex === '{expectedTurn}'",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    public static async Task<int> GetBoardTileCountAsync(IPage page) =>
        await page.Locator("#board-chain .board-chain-tile").CountAsync();

    public static async Task<IReadOnlyList<BonesViewerBoardTileDomLayoutState>> GetBoardTileDomLayoutAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const tiles = document.querySelectorAll('#board-chain .board-chain-tile');
            return Array.from(tiles, tile => {
                const readHalf = half => {
                    if (!half) {
                        return { pip: 0, positions: [] };
                    }

                    return {
                        pip: Number(half.dataset.pip),
                        positions: Array.from(half.querySelectorAll('.pip'), pipNode =>
                            Number(pipNode.dataset.position)),
                    };
                };

                const lowHalf = tile.querySelector('.domino-half-low');
                const highHalf = tile.querySelector('.domino-half-high');
                return {
                    gridX: Number(tile.dataset.gridX),
                    gridY: Number(tile.dataset.gridY),
                    orientation: tile.dataset.orientation ?? '',
                    pipAxis: tile.dataset.pipAxis ?? '',
                    isDouble: tile.dataset.isDouble === 'true',
                    lowHalf: readHalf(lowHalf),
                    highHalf: readHalf(highHalf),
                };
            });
        }");

        var result = new List<BonesViewerBoardTileDomLayoutState>();
        foreach (var tileJson in json.EnumerateArray())
        {
            result.Add(new BonesViewerBoardTileDomLayoutState(
                tileJson.GetProperty("gridX").GetInt32(),
                tileJson.GetProperty("gridY").GetInt32(),
                tileJson.GetProperty("orientation").GetString() ?? string.Empty,
                tileJson.GetProperty("pipAxis").GetString() ?? string.Empty,
                tileJson.GetProperty("isDouble").GetBoolean(),
                ReadHalf(tileJson.GetProperty("lowHalf")),
                ReadHalf(tileJson.GetProperty("highHalf"))));
        }

        return result;
    }

    public static async Task<IReadOnlyDictionary<(int GridX, int GridY), BonesViewerBoardTileDomPipState>>
        GetBoardDomPipStateByGridAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const tiles = document.querySelectorAll('#board-chain .board-chain-tile');
            return Array.from(tiles, tile => {
                const lowHalf = tile.querySelector('.domino-half-low');
                const highHalf = tile.querySelector('.domino-half-high');
                return {
                    gridX: Number(tile.dataset.gridX),
                    gridY: Number(tile.dataset.gridY),
                    lowHalfPip: Number(lowHalf?.dataset.pip),
                    highHalfPip: Number(highHalf?.dataset.pip),
                };
            });
        }");

        var board = new Dictionary<(int GridX, int GridY), BonesViewerBoardTileDomPipState>();
        foreach (var tileJson in json.EnumerateArray())
        {
            var gridX = tileJson.GetProperty("gridX").GetInt32();
            var gridY = tileJson.GetProperty("gridY").GetInt32();
            var lowHalfPip = tileJson.GetProperty("lowHalfPip").GetInt32();
            var highHalfPip = tileJson.GetProperty("highHalfPip").GetInt32();
            board[(gridX, gridY)] = new BonesViewerBoardTileDomPipState(lowHalfPip, highHalfPip);
        }

        return board;
    }

    public static async Task<IReadOnlyDictionary<int, BonesViewerHandDomState>> GetHandDomStateBySeatAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const result = {};
            for (let seat = 1; seat <= 4; seat += 1) {
                const hand = document.querySelector('.hand[data-seat=""' + seat + '""]');
                const tileNodes = hand
                    ? Array.from(hand.querySelectorAll('.hand-tiles .domino-tile'))
                    : [];
                const countNode = hand?.querySelector('.tile-count');
                result[String(seat)] = {
                    tileCount: countNode ? Number(countNode.dataset.count) : tileNodes.length,
                    tiles: tileNodes.map(tile => {
                        const lowHalf = tile.querySelector('.domino-half-low');
                        const highHalf = tile.querySelector('.domino-half-high');
                        return {
                            lowPip: Number(tile.dataset.lowPip),
                            highPip: Number(tile.dataset.highPip),
                            ownerSeat: Number(tile.dataset.ownerSeat),
                            lowHalfPip: Number(lowHalf?.dataset.pip),
                            highHalfPip: Number(highHalf?.dataset.pip),
                            lowHalfVisiblePipCount: lowHalf
                                ? lowHalf.querySelectorAll('.pip').length
                                : 0,
                            highHalfVisiblePipCount: highHalf
                                ? highHalf.querySelectorAll('.pip').length
                                : 0,
                        };
                    }),
                };
            }

            return result;
        }");

        var hands = new Dictionary<int, BonesViewerHandDomState>();
        for (var seat = 1; seat <= 4; seat++)
        {
            var seatKey = seat.ToString(CultureInfo.InvariantCulture);
            if (!json.TryGetProperty(seatKey, out var seatJson))
            {
                hands[seat] = new BonesViewerHandDomState(0, []);
                continue;
            }

            var tileCount = seatJson.TryGetProperty("tileCount", out var countJson)
                ? countJson.GetInt32()
                : 0;
            var tiles = new List<BonesViewerHandTileDomState>();
            if (seatJson.TryGetProperty("tiles", out var tilesJson))
            {
                foreach (var tileJson in tilesJson.EnumerateArray())
                {
                    tiles.Add(new BonesViewerHandTileDomState(
                        tileJson.GetProperty("lowPip").GetInt32(),
                        tileJson.GetProperty("highPip").GetInt32(),
                        tileJson.GetProperty("ownerSeat").GetInt32(),
                        tileJson.GetProperty("lowHalfPip").GetInt32(),
                        tileJson.GetProperty("highHalfPip").GetInt32(),
                        tileJson.GetProperty("lowHalfVisiblePipCount").GetInt32(),
                        tileJson.GetProperty("highHalfVisiblePipCount").GetInt32()));
                }
            }

            hands[seat] = new BonesViewerHandDomState(tileCount, tiles);
        }

        return hands;
    }

    private static BonesViewerDominoHalfDomLayoutState ReadHalf(JsonElement halfJson) =>
        new(
            halfJson.GetProperty("pip").GetInt32(),
            halfJson.GetProperty("positions").EnumerateArray()
                .Select(position => position.GetInt32())
                .ToArray());


    public static async Task<string> BoardTilePipBoundsDiagnosticAsync(IPage page, int gridX, int gridY) =>
        await page.EvaluateAsync<string>(
            @"({ gridX, gridY }) => {
                const tile = Array.from(document.querySelectorAll('#board-chain .board-chain-tile'))
                    .find(node => Number(node.dataset.gridX) === gridX && Number(node.dataset.gridY) === gridY)
                    ?? Array.from(document.querySelectorAll('#board-chain .board-chain-tile'))
                        .find(node => node.dataset.isDouble === 'true'
                            && Number(node.dataset.gridY) > 0
                            && node.dataset.pipAxis === 'vertical');
                if (!tile) return 'tile-missing';
                const summary = {
                    halfCount: tile.querySelectorAll('.domino-half').length,
                    pipCount: tile.querySelectorAll('.pip').length,
                    gridX: tile.dataset.gridX,
                    gridY: tile.dataset.gridY,
                };
                const halves = tile.querySelectorAll('.domino-half');
                let visiblePipCount = 0;
                for (const half of halves) {
                    const halfRect = half.getBoundingClientRect();
                    const pips = half.querySelectorAll('.pip');
                    for (const pip of pips) {
                        const pipRect = pip.getBoundingClientRect();
                        if (pipRect.width <= 0 || pipRect.height <= 0) return 'pip-zero-size';
                        visiblePipCount += 1;
                        if (pipRect.left < halfRect.left - 2.5
                            || pipRect.right > halfRect.right + 2.5
                            || pipRect.top < halfRect.top - 2.5
                            || pipRect.bottom > halfRect.bottom + 2.5) {
                            return JSON.stringify({ reason: 'pip-outside-half', half: half.className, pipRect, halfRect });
                        }
                    }
                }
                return visiblePipCount > 0 ? 'ok' : JSON.stringify(summary);
            }",
            new { gridX, gridY });
    public static async Task<bool> BoardTilePipsContainedInTileBoundsAsync(IPage page, int gridX, int gridY) =>
        await page.EvaluateAsync<bool>(
            @"({ gridX, gridY }) => {
                const tile = Array.from(document.querySelectorAll('#board-chain .board-chain-tile'))
                    .find(node => Number(node.dataset.gridX) === gridX && Number(node.dataset.gridY) === gridY)
                    ?? Array.from(document.querySelectorAll('#board-chain .board-chain-tile'))
                        .find(node => node.dataset.isDouble === 'true'
                            && Number(node.dataset.gridY) > 0
                            && node.dataset.pipAxis === 'vertical');
                if (!tile) {
                    return false;
                }

                const halves = tile.querySelectorAll('.domino-half');
                if (halves.length === 0) {
                    return false;
                }

                let visiblePipCount = 0;
                for (const half of halves) {
                    const halfRect = half.getBoundingClientRect();
                    const pips = half.querySelectorAll('.pip');
                    for (const pip of pips) {
                        const pipRect = pip.getBoundingClientRect();
                        if (pipRect.width <= 0 || pipRect.height <= 0) {
                            return false;
                        }

                        visiblePipCount += 1;
                        if (pipRect.left < halfRect.left - 2.5
                            || pipRect.right > halfRect.right + 2.5
                            || pipRect.top < halfRect.top - 2.5
                            || pipRect.bottom > halfRect.bottom + 2.5) {
                            return false;
                        }
                    }
                }

                return visiblePipCount > 0;
            }",
            new { gridX, gridY });

    public static async Task<bool> OpeningDoubleDividerIsHorizontalAsync(IPage page, int gridX, int gridY) =>
        await page.EvaluateAsync<bool>(
            @"({ gridX, gridY }) => {
                const tile = Array.from(document.querySelectorAll('#board-chain .board-chain-tile'))
                    .find(node => Number(node.dataset.gridX) === gridX && Number(node.dataset.gridY) === gridY);
                if (!tile || tile.dataset.isDouble !== 'true' || tile.dataset.pipAxis !== 'vertical') {
                    return false;
                }

                const divider = tile.querySelector('.domino-divider');
                if (!divider) {
                    return false;
                }

                const dividerRect = divider.getBoundingClientRect();
                const tileRect = tile.getBoundingClientRect();
                return dividerRect.height <= 6
                    && dividerRect.width >= tileRect.width * 0.5;
            }",
            new { gridX, gridY });

    public static async Task DispatchScrubberInputAsync(IPage page, int turnIndex) =>
        await page.EvaluateAsync(
            @"turnIndex => {
                const scrubber = document.getElementById('replay-scrubber');
                scrubber.value = String(turnIndex);
                scrubber.dispatchEvent(new Event('input', { bubbles: true }));
            }",
            turnIndex);

    public static async Task<string?> GetTurnLabelTurnIndexAsync(IPage page) =>
        await page.Locator("#turn-index").GetAttributeAsync("data-turn-index");

    public static async Task RouteBonesStatusStubAsync(
        IPage page,
        object? statusPayload,
        bool returnNotFound = false)
    {
        await page.RouteAsync("**/api/bones/status", async route =>
        {
            if (returnNotFound)
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 404,
                    ContentType = "text/plain",
                    Body = "Not Found",
                });
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(statusPayload ?? new { }),
            });
        });
    }

    public static async Task WaitForLearningLoopPanelPopulatedAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            @"() => {
                const panel = document.getElementById('learning-loop-status');
                return Boolean(
                    panel
                    && !panel.classList.contains('hidden')
                    && panel.dataset.iterationCount !== ''
                    && panel.dataset.currentStage !== '');
            }",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    public static async Task WaitForLearningLoopFrameCountAsync(IPage page, int expectedFrameCount)
    {
        var expected = expectedFrameCount.ToString(CultureInfo.InvariantCulture);
        await page.WaitForFunctionAsync(
            $@"() => document.getElementById('loop-match-turns')?.dataset?.frameCount === '{expected}'",
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public static async Task<BonesViewerLearningLoopStatusDomState> ReadLearningLoopStatusDomAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const panel = document.getElementById('learning-loop-status');
            const matchTurns = document.getElementById('loop-match-turns');
            const ponderPending = document.getElementById('loop-ponder-pending');
            const learningPlayer = document.getElementById('loop-learning-player');
            const learningPlayerError = document.getElementById('loop-learning-player-error');
            const lastIterationError = document.getElementById('loop-last-iteration-error');
            const modelProvider = document.getElementById('loop-model-provider');
            const gameBudget = document.getElementById('loop-game-budget-value');
            const promotion = document.getElementById('loop-last-promotion');
            return {
                panelHidden: panel?.classList.contains('hidden') ?? true,
                iterationCount: panel?.dataset?.iterationCount ?? null,
                currentStage: panel?.dataset?.currentStage ?? null,
                gamesSimulated: panel?.dataset?.gamesSimulated ?? null,
                turnIndex: matchTurns?.dataset?.turnIndex ?? null,
                frameCount: matchTurns?.dataset?.frameCount ?? null,
                matchTurnsText: document.getElementById('loop-match-turns-value')?.textContent ?? null,
                ponderPendingHidden: ponderPending?.classList.contains('hidden') ?? true,
                learningPlayerHidden: learningPlayer?.classList.contains('hidden') ?? true,
                learningPlayerErrorHidden: learningPlayerError?.classList.contains('hidden') ?? true,
                learningPlayerErrorText: document.getElementById('loop-learning-player-error-value')?.textContent ?? null,
                lastIterationErrorHidden: lastIterationError?.classList.contains('hidden') ?? true,
                lastIterationErrorText: document.getElementById('loop-last-iteration-error-value')?.textContent ?? null,
                failureStage: lastIterationError?.dataset?.failureStage ?? null,
                promotionText: document.getElementById('loop-last-promotion-value')?.textContent ?? null,
                promotionHidden: promotion?.classList.contains('hidden') ?? true,
                gameBudgetText: gameBudget?.textContent ?? null,
                modelProviderText: document.getElementById('loop-model-provider-value')?.textContent ?? null,
                provider: modelProvider?.dataset?.provider ?? null,
                model: modelProvider?.dataset?.model ?? null,
                pageHtml: document.documentElement.outerHTML,
            };
        }");

        return new BonesViewerLearningLoopStatusDomState(
            json.GetProperty("panelHidden").GetBoolean(),
            json.TryGetProperty("iterationCount", out var iterationCount) ? iterationCount.GetString() : null,
            json.TryGetProperty("currentStage", out var currentStage) ? currentStage.GetString() : null,
            json.TryGetProperty("gamesSimulated", out var gamesSimulated) ? gamesSimulated.GetString() : null,
            json.TryGetProperty("turnIndex", out var turnIndex) ? turnIndex.GetString() : null,
            json.TryGetProperty("frameCount", out var frameCount) ? frameCount.GetString() : null,
            json.TryGetProperty("matchTurnsText", out var matchTurnsText) ? matchTurnsText.GetString() : null,
            json.GetProperty("ponderPendingHidden").GetBoolean(),
            json.GetProperty("learningPlayerHidden").GetBoolean(),
            json.GetProperty("learningPlayerErrorHidden").GetBoolean(),
            json.TryGetProperty("learningPlayerErrorText", out var learningPlayerErrorText) ? learningPlayerErrorText.GetString() : null,
            json.GetProperty("lastIterationErrorHidden").GetBoolean(),
            json.TryGetProperty("lastIterationErrorText", out var lastIterationErrorText) ? lastIterationErrorText.GetString() : null,
            json.TryGetProperty("failureStage", out var failureStage) ? failureStage.GetString() : null,
            json.TryGetProperty("promotionText", out var promotionText) ? promotionText.GetString() : null,
            json.GetProperty("promotionHidden").GetBoolean(),
            json.TryGetProperty("gameBudgetText", out var gameBudgetText) ? gameBudgetText.GetString() : null,
            json.TryGetProperty("modelProviderText", out var modelProviderText) ? modelProviderText.GetString() : null,
            json.TryGetProperty("provider", out var provider) ? provider.GetString() : null,
            json.TryGetProperty("model", out var model) ? model.GetString() : null,
            json.GetProperty("pageHtml").GetString() ?? string.Empty);
    }

    public static async Task<BonesViewerScrubberChromeState> ReadScrubberChromeSyncAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const scrubber = document.getElementById('replay-scrubber');
            const turnIndex = document.getElementById('turn-index');
            return {
                scrubberValue: scrubber?.value ?? null,
                scrubberDataTurnIndex: scrubber?.dataset?.turnIndex ?? null,
                turnLabelDataTurnIndex: turnIndex?.dataset?.turnIndex ?? null,
                turnLabelText: turnIndex?.textContent ?? null,
            };
        }");

        return new BonesViewerScrubberChromeState(
            json.TryGetProperty("scrubberValue", out var scrubberValue) ? scrubberValue.GetString() : null,
            json.TryGetProperty("scrubberDataTurnIndex", out var scrubberData) ? scrubberData.GetString() : null,
            json.TryGetProperty("turnLabelDataTurnIndex", out var labelData) ? labelData.GetString() : null,
            json.TryGetProperty("turnLabelText", out var labelText) ? labelText.GetString() : null);
    }

    public static void AssertScrubberChromeInSync(BonesViewerScrubberChromeState state, int expectedTurnIndex)
    {
        var expected = expectedTurnIndex.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(expected, state.ScrubberValue);
        Assert.Equal(expected, state.ScrubberDataTurnIndex);
        Assert.Equal(expected, state.TurnLabelDataTurnIndex);
        Assert.Equal("Turn " + expected, state.TurnLabelText);
    }

    public static async Task<bool> HasFrameLoadErrorAsync(IPage page) =>
        await page.EvaluateAsync<bool>(
            @"() => document.getElementById('bones-viewer')?.dataset?.frameError === 'true'");

    public static async Task<string?> GetLastMoveKindAsync(IPage page) =>
        await page.Locator("#last-move").GetAttributeAsync("data-kind");


    public static async Task<IPage> OpenBoardLayoutRenderedPageAsync(
        IBrowser browser,
        Uri baseAddress,
        BonesBoardVisualLayout layout)
    {
        var geometryBuilder = new BonesBoardChainGeometryBuilder();
        var resolvedLayout = geometryBuilder.AssignFineGridCells(layout);
        var snapshot = new BonesMatchViewModel
        {
            SessionId = "session-end-chain-double-six-layout",
            MatchId = "match-end-chain-double-six-layout",
            LeftEndPip = 1,
            RightEndPip = 6,
            HandTileCountsBySeat = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0 },
            BoardLayout = resolvedLayout,
            HandTilesBySeat = new Dictionary<int, IReadOnlyList<BonesHandTileView>>(),
            PlayerColorsBySeat = BonesPlayerColorPalette.PlayerColorsBySeat,
            ActiveSeat = 1,
            CumulativeScoresBySeat = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0, [4] = 0 },
            FrameCount = resolvedLayout.Tiles.Count,
            Revision = 1,
            IsComplete = false,
        };

        var bodyHtml = BonesViewerPageRenderer.Render(snapshot, null, resolvedLayout.Tiles.Count - 1);
        var cssHref = new Uri(baseAddress, "/viewer/viewer.css").ToString();
        var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"" + cssHref + "\"></head><body>" + bodyHtml + "</body></html>";
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.NetworkIdle });
        return page;
    }

    public static async Task RouteBonesStatusFromHandlerAsync(
        IPage page,
        HttpMessageHandler handler,
        Uri serverBaseAddress)
    {
        using var statusHttpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = serverBaseAddress,
        };

        await page.RouteAsync("**/api/bones/status", async route =>
        {
            var response = await statusHttpClient.GetAsync(new Uri(serverBaseAddress, "/api/bones/status"));
            var body = await response.Content.ReadAsByteArrayAsync();
            var fulfillOptions = new RouteFulfillOptions
            {
                Status = (int)response.StatusCode,
                BodyBytes = body,
            };

            var responseContentType = response.Content.Headers.ContentType?.ToString();
            if (!string.IsNullOrWhiteSpace(responseContentType))
            {
                fulfillOptions.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = responseContentType,
                };
            }

            await route.FulfillAsync(fulfillOptions);
        });
    }

    public static async Task RouteBonesStatusFromClientAsync(IPage page, HttpClient client)
    {
        await page.RouteAsync("**/api/bones/status", async route =>
        {
            var response = await client.GetAsync("/api/bones/status");
            var body = await response.Content.ReadAsByteArrayAsync();
            var fulfillOptions = new RouteFulfillOptions
            {
                Status = (int)response.StatusCode,
                BodyBytes = body,
            };

            var responseContentType = response.Content.Headers.ContentType?.ToString();
            if (!string.IsNullOrWhiteSpace(responseContentType))
            {
                fulfillOptions.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = responseContentType,
                };
            }

            await route.FulfillAsync(fulfillOptions);
        });
    }

    public static async Task<IPage> OpenViewRoutePageViaHttpHandlerAsync(
        IBrowser browser,
        HttpMessageHandler handler,
        Uri publicBaseAddress,
        Uri serverBaseAddress,
        string sessionId,
        string matchId,
        bool routeLiveStatusApi = false,
        int? turnIndex = null)
    {
        var page = await browser.NewPageAsync();
        using var httpClient = new HttpClient(handler, disposeHandler: false);

        await page.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            var requestUri = new Uri(request.Url);
            var targetUri = new Uri(serverBaseAddress, requestUri.PathAndQuery);
            var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

            if (!string.IsNullOrWhiteSpace(request.PostData))
            {
                var contentType = request.Headers.TryGetValue("content-type", out var values)
                    ? values
                    : "application/octet-stream";
                httpRequest.Content = new StringContent(request.PostData, System.Text.Encoding.UTF8, contentType);
            }

            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var httpResponse = await httpClient.SendAsync(httpRequest);
            var body = await httpResponse.Content.ReadAsByteArrayAsync();
            var fulfillOptions = new RouteFulfillOptions
            {
                Status = (int)httpResponse.StatusCode,
                BodyBytes = body,
            };

            var responseContentType = httpResponse.Content.Headers.ContentType?.ToString();
            if (!string.IsNullOrWhiteSpace(responseContentType))
            {
                fulfillOptions.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = responseContentType,
                };
            }

            await route.FulfillAsync(fulfillOptions);
        });

        if (routeLiveStatusApi)
        {
            await RouteBonesStatusFromHandlerAsync(page, handler, serverBaseAddress);
        }

        var viewPath =
            $"/bones/sessions/{Uri.EscapeDataString(sessionId)}/matches/{Uri.EscapeDataString(matchId)}/view";
        if (turnIndex is not null)
        {
            viewPath += $"?turnIndex={turnIndex.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        var viewUrl = new Uri(publicBaseAddress, viewPath);
        var response = await page.GotoAsync(
            viewUrl.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        if (response is null || !response.Ok)
        {
            await page.CloseAsync();
            throw new InvalidOperationException($"Viewer route returned {(response?.Status ?? 0)} for {viewUrl}.");
        }

        return page;
    }

    public static async Task<IPage> OpenViewRoutePageAsync(
        IBrowser browser,
        Uri baseAddress,
        string sessionId,
        string matchId,
        bool stripSessionMatchDataAttributes = false,
        int? turnIndex = null)
    {
        var page = await browser.NewPageAsync();
        var viewPath =
            $"/bones/sessions/{Uri.EscapeDataString(sessionId)}/matches/{Uri.EscapeDataString(matchId)}/view";
        if (turnIndex is not null)
        {
            viewPath += $"?turnIndex={turnIndex.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        var viewUrl = new Uri(baseAddress, viewPath);

        if (stripSessionMatchDataAttributes)
        {
            var viewDocumentPattern = $"**/bones/sessions/{Uri.EscapeDataString(sessionId)}/matches/{Uri.EscapeDataString(matchId)}/view";
            await page.RouteAsync(
                viewDocumentPattern,
                async route =>
                {
                    if (!string.Equals(route.Request.ResourceType, "document", StringComparison.Ordinal))
                    {
                        await route.ContinueAsync();
                        return;
                    }

                    var upstream = await route.FetchAsync();
                    var body = await upstream.TextAsync();
                    body = Regex.Replace(body, @"\sdata-session-id=""[^""]*""", string.Empty);
                    body = Regex.Replace(body, @"\sdata-match-id=""[^""]*""", string.Empty);
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Response = upstream,
                        Body = body,
                    });
                });
        }

        var response = await page.GotoAsync(
            viewUrl.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        if (response is null || !response.Ok)
        {
            await page.CloseAsync();
            throw new InvalidOperationException($"Viewer route returned {(response?.Status ?? 0)} for {viewUrl}.");
        }

        return page;
    }

    public static async Task<BonesViewerDominoTileUnitCssState> GetDominoTileUnitCssVariablesAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const styles = getComputedStyle(document.documentElement);
            return {
                dominoTileUnit: styles.getPropertyValue('--domino-tile-unit').trim(),
                boardTileUnit: styles.getPropertyValue('--board-tile-unit').trim(),
                handTileUnit: styles.getPropertyValue('--hand-tile-unit').trim(),
            };
        }");

        return new BonesViewerDominoTileUnitCssState(
            json.GetProperty("dominoTileUnit").GetString() ?? string.Empty,
            json.GetProperty("boardTileUnit").GetString() ?? string.Empty,
            json.GetProperty("handTileUnit").GetString() ?? string.Empty);
    }

    public static async Task<BonesViewerHandBoardPipMarkupParityState> GetHandBoardPipMarkupParityAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const handTiles = Array.from(document.querySelectorAll('.hand-tiles .domino-tile'));
            const boardTiles = Array.from(document.querySelectorAll('#board-chain .board-chain-tile'));

            function samplePipPattern(containerSelector) {
                for (const half of document.querySelectorAll(containerSelector + ' .domino-half')) {
                    const pipCount = Number(half.getAttribute('data-pip') || '0');
                    if (pipCount <= 0) {
                        continue;
                    }

                    const pipEl = half.querySelector('.pip[data-position]');
                    if (pipEl) {
                        return pipEl.tagName + '.' + pipEl.className;
                    }
                }

                const fallbackPip = document.querySelector(containerSelector + ' .pip[data-position]');
                return fallbackPip ? fallbackPip.tagName + '.' + fallbackPip.className : '';
            }

            const halves = [];
            for (let seat = 1; seat <= 4; seat += 1) {
                const hand = document.querySelector('.hand[data-seat=""' + seat + '""]');
                if (!hand) continue;
                const panelRect = hand.getBoundingClientRect();
                const tileNodes = Array.from(hand.querySelectorAll('.hand-tiles .domino-tile'));
                tileNodes.forEach((tile, tileIndex) => {
                    tile.querySelectorAll('.domino-half').forEach(half => {
                        const halfRect = half.getBoundingClientRect();
                        halves.push({
                            seat,
                            tileIndex,
                            halfContainedInHandPanel:
                                halfRect.left >= panelRect.left - 1
                                && halfRect.right <= panelRect.right + 1
                                && halfRect.top >= panelRect.top - 1
                                && halfRect.bottom <= panelRect.bottom + 1,
                        });
                    });
                });
            }

            return {
                handTileCount: handTiles.length,
                boardTileCount: boardTiles.length,
                handPipSelectorPattern: samplePipPattern('.hand-tiles'),
                boardPipSelectorPattern: samplePipPattern('#board-chain'),
                handHalves: halves,
            };
        }");

        var halves = new List<BonesViewerHandHalfContainmentState>();
        foreach (var halfJson in json.GetProperty("handHalves").EnumerateArray())
        {
            halves.Add(new BonesViewerHandHalfContainmentState(
                halfJson.GetProperty("seat").GetInt32(),
                halfJson.GetProperty("tileIndex").GetInt32(),
                halfJson.GetProperty("halfContainedInHandPanel").GetBoolean()));
        }

        return new BonesViewerHandBoardPipMarkupParityState(
            json.GetProperty("handTileCount").GetInt32(),
            json.GetProperty("boardTileCount").GetInt32(),
            json.GetProperty("handPipSelectorPattern").GetString() ?? string.Empty,
            json.GetProperty("boardPipSelectorPattern").GetString() ?? string.Empty,
            halves);
    }

    public static async Task<IReadOnlyDictionary<(int GridX, int GridY), BonesViewerDomBoundingRect>>
        GetBoardTileBoundingBoxesByGridAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const tiles = Array.from(document.querySelectorAll('#board-chain .board-chain-tile'));
            return tiles.map(tile => {
                const rect = tile.getBoundingClientRect();
                return {
                    gridX: Number(tile.dataset.gridX),
                    gridY: Number(tile.dataset.gridY),
                    left: rect.left,
                    top: rect.top,
                    width: rect.width,
                    height: rect.height,
                };
            });
        }");

        var boxes = new Dictionary<(int GridX, int GridY), BonesViewerDomBoundingRect>();
        foreach (var entry in json.EnumerateArray())
        {
            var gridX = entry.GetProperty("gridX").GetInt32();
            var gridY = entry.GetProperty("gridY").GetInt32();
            boxes[(gridX, gridY)] = new BonesViewerDomBoundingRect(
                entry.GetProperty("left").GetDouble(),
                entry.GetProperty("top").GetDouble(),
                entry.GetProperty("width").GetDouble(),
                entry.GetProperty("height").GetDouble());
        }

        return boxes;
    }

    public static async Task<BonesViewerDomBoundingRect> GetBoardChainSlotRectAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const boardChain = document.getElementById('board-chain');
            const rect = boardChain ? boardChain.getBoundingClientRect() : { left: 0, top: 0, width: 0, height: 0 };
            return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
        }");

        return new BonesViewerDomBoundingRect(
            json.GetProperty("left").GetDouble(),
            json.GetProperty("top").GetDouble(),
            json.GetProperty("width").GetDouble(),
            json.GetProperty("height").GetDouble());
    }

    public static bool IsStrictlyRightOf(BonesViewerDomBoundingRect left, BonesViewerDomBoundingRect right, double tolerancePx = 1.0) =>
        right.Left > left.Right - tolerancePx;

    public static bool IsStrictlyLeftOf(BonesViewerDomBoundingRect reference, BonesViewerDomBoundingRect candidate, double tolerancePx = 1.0) =>
        candidate.Right < reference.Left + tolerancePx;

    public static bool BoundingRectsIntersect(BonesViewerDomBoundingRect left, BonesViewerDomBoundingRect right, double tolerancePx = 0.5)
    {
        var leftRight = left.Left + left.Width;
        var leftBottom = left.Top + left.Height;
        var rightRight = right.Left + right.Width;
        var rightBottom = right.Top + right.Height;

        var separated =
            leftRight <= right.Left + tolerancePx
            || rightRight <= left.Left + tolerancePx
            || leftBottom <= right.Top + tolerancePx
            || rightBottom <= left.Top + tolerancePx;

        return !separated;
    }

    public static async Task<IReadOnlyDictionary<int, IReadOnlyList<BonesViewerHandTileDimensionState>>>
        GetHandTileDimensionsBySeatAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>(@"() => {
            const result = {};
            for (let seat = 1; seat <= 4; seat += 1) {
                const hand = document.querySelector('.hand[data-seat=""' + seat + '""]');
                const tileNodes = hand
                    ? Array.from(hand.querySelectorAll('.hand-tiles .domino-tile'))
                    : [];
                result[String(seat)] = tileNodes.map(tile => {
                    const rect = tile.getBoundingClientRect();
                    return { width: rect.width, height: rect.height };
                });
            }

            return result;
        }");

        var dimensions = new Dictionary<int, IReadOnlyList<BonesViewerHandTileDimensionState>>();
        for (var seat = 1; seat <= 4; seat++)
        {
            var seatKey = seat.ToString(CultureInfo.InvariantCulture);
            var tiles = new List<BonesViewerHandTileDimensionState>();
            if (json.TryGetProperty(seatKey, out var seatJson))
            {
                foreach (var tileJson in seatJson.EnumerateArray())
                {
                    tiles.Add(new BonesViewerHandTileDimensionState(
                        tileJson.GetProperty("width").GetDouble(),
                        tileJson.GetProperty("height").GetDouble()));
                }
            }

            dimensions[seat] = tiles;
        }

        return dimensions;
    }

    internal sealed record BonesViewerBootstrapState(
        string? SessionId,
        string? MatchId,
        string? FrameCount,
        string? ScrubberMax);

    internal sealed record BonesViewerBoardTileDomPipState(int LowHalfPip, int HighHalfPip);

    internal sealed record BonesViewerDominoHalfDomLayoutState(int Pip, IReadOnlyList<int> Positions);

    internal sealed record BonesViewerBoardTileDomLayoutState(
        int GridX,
        int GridY,
        string Orientation,
        string PipAxis,
        bool IsDouble,
        BonesViewerDominoHalfDomLayoutState LowHalf,
        BonesViewerDominoHalfDomLayoutState HighHalf);

    internal sealed record BonesViewerHandTileDomState(
        int LowPip,
        int HighPip,
        int OwnerSeat,
        int LowHalfPip,
        int HighHalfPip,
        int LowHalfVisiblePipCount,
        int HighHalfVisiblePipCount);

    internal sealed record BonesViewerHandDomState(
        int TileCount,
        IReadOnlyList<BonesViewerHandTileDomState> Tiles);

    internal sealed record BonesViewerScrubberChromeState(
        string? ScrubberValue,
        string? ScrubberDataTurnIndex,
        string? TurnLabelDataTurnIndex,
        string? TurnLabelText);

    internal sealed record BonesViewerLearningLoopStatusDomState(
        bool PanelHidden,
        string? IterationCount,
        string? CurrentStage,
        string? GamesSimulated,
        string? TurnIndex,
        string? FrameCount,
        string? MatchTurnsText,
        bool PonderPendingHidden,
        bool LearningPlayerHidden,
        bool LearningPlayerErrorHidden,
        string? LearningPlayerErrorText,
        bool LastIterationErrorHidden,
        string? LastIterationErrorText,
        string? FailureStage,
        string? PromotionText,
        bool PromotionHidden,
        string? GameBudgetText,
        string? ModelProviderText,
        string? Provider,
        string? Model,
        string PageHtml);

    internal sealed record BonesViewerDominoTileUnitCssState(
        string DominoTileUnit,
        string BoardTileUnit,
        string HandTileUnit);

    internal sealed record BonesViewerHandHalfContainmentState(
        int Seat,
        int TileIndex,
        bool HalfContainedInHandPanel);

    internal sealed record BonesViewerHandBoardPipMarkupParityState(
        int HandTileCount,
        int BoardTileCount,
        string HandPipSelectorPattern,
        string BoardPipSelectorPattern,
        IReadOnlyList<BonesViewerHandHalfContainmentState> HandHalves);

    internal sealed record BonesViewerHandTileDimensionState(double Width, double Height);

    internal sealed record BonesViewerDomBoundingRect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;

        public double Bottom => Top + Height;
    }
}
