(function () {
  const TILE_UNIT_PIXELS = 40;
  const MIN_READABLE_TILE_PIXELS = 28;
  const DEFAULT_CHAIN_SLOT_WIDTH_PIXELS = 400;
  const DEFAULT_CONTAINER_HEIGHT_PIXELS = 160;

  const params = new URLSearchParams(window.location.search);
  const root = document.getElementById("bones-viewer");

  if (!root) {
    return;
  }

  function resolveIdsFromPath() {
    const match = window.location.pathname.match(
      /\/bones\/sessions\/([^/]+)\/matches\/([^/]+)\/view\/?$/);
    if (!match) {
      return { sessionId: null, matchId: null };
    }

    return {
      sessionId: decodeURIComponent(match[1]),
      matchId: decodeURIComponent(match[2]),
    };
  }

  const pathIds = resolveIdsFromPath();
  let activeSessionId =
    params.get("sessionId") ||
    root.dataset.sessionId ||
    pathIds.sessionId;
  let activeMatchId =
    params.get("matchId") ||
    root.dataset.matchId ||
    pathIds.matchId;

  const leftEnd = document.getElementById("left-end-pip");
  const rightEnd = document.getElementById("right-end-pip");
  const boardChain = document.getElementById("board-chain");
  const handsContainer = document.querySelector(".hands");
  const activeSeat = document.getElementById("active-seat");
  const lastMove = document.getElementById("last-move");
  const winner = document.getElementById("winner");
  const roundScore = document.getElementById("round-score");
  const scrubber = document.getElementById("replay-scrubber");
  const turnIndexOutput = document.getElementById("turn-index");
  const matchLabel = document.querySelector(".match-id");
  const learningLoopPanel = document.getElementById("learning-loop-status");
  const loopRunningStatus = document.getElementById("loop-running-status");
  const loopRunningValue = document.getElementById("loop-running-value");
  const loopIteration = document.getElementById("loop-iteration");
  const loopIterationValue = document.getElementById("loop-iteration-value");
  const loopStage = document.getElementById("loop-stage");
  const loopStageValue = document.getElementById("loop-stage-value");
  const loopGameBudget = document.getElementById("loop-game-budget");
  const loopGameBudgetValue = document.getElementById("loop-game-budget-value");
  const loopMatchTurns = document.getElementById("loop-match-turns");
  const loopMatchTurnsValue = document.getElementById("loop-match-turns-value");
  const loopMatchComplete = document.getElementById("loop-match-complete");
  const loopMatchCompleteValue = document.getElementById("loop-match-complete-value");
  const loopLearningPlayer = document.getElementById("loop-learning-player");
  const loopLearningPlayerValue = document.getElementById("loop-learning-player-value");
  const loopPonderPending = document.getElementById("loop-ponder-pending");
  const loopLearningPlayerError = document.getElementById("loop-learning-player-error");
  const loopLearningPlayerErrorValue = document.getElementById("loop-learning-player-error-value");
  const loopLastIterationError = document.getElementById("loop-last-iteration-error");
  const loopLastIterationErrorValue = document.getElementById("loop-last-iteration-error-value");
  const loopLastPromotion = document.getElementById("loop-last-promotion");
  const loopLastPromotionValue = document.getElementById("loop-last-promotion-value");
  const loopLibraryBest = document.getElementById("loop-library-best");
  const loopLibraryBestValue = document.getElementById("loop-library-best-value");
  const loopModelProvider = document.getElementById("loop-model-provider");
  const loopModelProviderValue = document.getElementById("loop-model-provider-value");

  const LIVE_POLL_INTERVAL_MS = 1000;
  const STATUS_POLL_INTERVAL_MS = 2000;
  let snapshot = null;
  let snapshotRevision = null;
  let currentBoardLayout = null;
  let isReloadingSnapshot = false;
  let lastLearningLoopStatus = null;

  // Keep in sync with BonesStandardDominoPipLayout (3x3 grid: 0-2 top, 3-5 middle, 6-8 bottom).
  const pipPositions = {
    0: [],
    1: [4],
    2: [2, 6],
    3: [2, 4, 6],
    4: [0, 2, 6, 8],
    5: [0, 2, 4, 6, 8],
    6: [0, 3, 6, 2, 5, 8],
  };

  function formatPip(value) {
    return value === null || value === undefined || value === "" ? "\u2014" : String(value);
  }

  function getSeatColor(seat, playerColors) {
    if (playerColors && playerColors[seat]) {
      return playerColors[seat];
    }

    const defaults = {
      1: "#e63946",
      2: "#457b9d",
      3: "#2a9d8f",
      4: "#e9c46a",
    };
    return defaults[seat] || "#94a3b8";
  }

  function resolveOccupiedWidthPixels(boardLayout) {
    if (boardLayout.occupiedWidthPixels > 0) {
      return boardLayout.occupiedWidthPixels;
    }

    if (boardLayout.fineColumnCount > 0) {
      return boardLayout.fineColumnCount * TILE_UNIT_PIXELS;
    }

    const columnCount = boardLayout.columnCount || 1;
    return columnCount * TILE_UNIT_PIXELS * 2;
  }

  function resolveOccupiedHeightPixels(boardLayout) {
    if (boardLayout.occupiedHeightPixels > 0) {
      return boardLayout.occupiedHeightPixels;
    }

    if (boardLayout.fineRowCount > 0) {
      return boardLayout.fineRowCount * TILE_UNIT_PIXELS;
    }

    const rowCount = boardLayout.rowCount || 1;
    return rowCount * TILE_UNIT_PIXELS * 2;
  }

  function computeChainScale(boardLayout, containerWidth, containerHeight) {
    if (!boardLayout || !boardLayout.tiles || boardLayout.tiles.length === 0) {
      return 1;
    }

    if (containerWidth <= 0 || containerHeight <= 0) {
      return 1;
    }

    const chainWidth = resolveOccupiedWidthPixels(boardLayout);
    const chainHeight = resolveOccupiedHeightPixels(boardLayout);

    if (chainWidth <= containerWidth && chainHeight <= containerHeight) {
      return 1;
    }

    const scaleX = containerWidth / chainWidth;
    const scaleY = containerHeight / chainHeight;
    const fitScale = Math.min(scaleX, scaleY);
    const minTransformScale = MIN_READABLE_TILE_PIXELS / TILE_UNIT_PIXELS;
    return Math.min(1, Math.max(fitScale, minTransformScale));
  }

  function applyBoardChainLayout(boardLayout) {
    if (!boardLayout) {
      return;
    }

    const containerWidth = boardChain ? boardChain.clientWidth : DEFAULT_CHAIN_SLOT_WIDTH_PIXELS;
    const containerHeight = boardChain ? boardChain.clientHeight : DEFAULT_CONTAINER_HEIGHT_PIXELS;
    const scale = computeChainScale(boardLayout, containerWidth, containerHeight);
    const fineColumns = Math.max(boardLayout.fineColumnCount || (boardLayout.columnCount || 1) * 2, 2);
    const fineRows = Math.max(boardLayout.fineRowCount || (boardLayout.rowCount || 1) * 2, 2);

    boardChain.style.setProperty("--board-chain-scale", String(scale));
    boardChain.style.setProperty("--board-chain-columns", String(fineColumns));
    boardChain.style.setProperty("--board-chain-rows", String(fineRows));
    boardChain.style.setProperty(
      "--board-chain-occupied-width",
      String(resolveOccupiedWidthPixels(boardLayout)));
    boardChain.style.setProperty(
      "--board-chain-occupied-height",
      String(resolveOccupiedHeightPixels(boardLayout)));
    boardChain.dataset.minGridX = String(boardLayout.minGridX ?? 0);
    boardChain.dataset.maxGridX = String(boardLayout.maxGridX ?? 0);
    boardChain.dataset.minGridY = String(boardLayout.minGridY ?? 0);
    boardChain.dataset.maxGridY = String(boardLayout.maxGridY ?? 0);
    boardChain.dataset.columnCount = String(boardLayout.columnCount ?? 0);
    boardChain.dataset.rowCount = String(boardLayout.rowCount ?? 0);
    boardChain.dataset.boardChainScale = String(scale);
  }

  function coercePipCount(pip) {
    if (pip === null || pip === undefined || pip === "") {
      return 0;
    }

    const numeric = Number(pip);
    if (!Number.isFinite(numeric)) {
      return 0;
    }

    const integer = Math.trunc(numeric);
    if (integer < 0 || integer > 6) {
      return 0;
    }

    return integer;
  }

  function resolveFacingPip(primary, fallback) {
    if (primary !== null && primary !== undefined && primary !== "") {
      return coercePipCount(primary);
    }

    return coercePipCount(fallback);
  }

  function createPips(pip) {
    const fragment = document.createDocumentFragment();
    const pipCount = coercePipCount(pip);
    const positions = pipPositions[pipCount] ?? [];
    for (let index = 0; index < positions.length; index += 1) {
      const dot = document.createElement("span");
      dot.className = "pip";
      dot.dataset.position = String(positions[index]);
      fragment.appendChild(dot);
    }
    return fragment;
  }

  function createDominoHalf(className, pip) {
    const pipCount = coercePipCount(pip);
    const half = document.createElement("div");
    half.className = "domino-half " + className;
    half.dataset.pip = String(pipCount);
    half.appendChild(createPips(pipCount));
    return half;
  }

  function createSeatMarker(seatColor, useDividerFallback) {
    if (useDividerFallback) {
      const divider = document.createElement("span");
      divider.className = "domino-divider";
      divider.dataset.seatColor = seatColor;
      divider.style.setProperty("--seat-divider-color", seatColor);
      return divider;
    }

    const marker = document.createElement("span");
    marker.className = "seat-color-marker";
    marker.style.backgroundColor = seatColor;
    return marker;
  }

  function formatGridPlacementStyle(placement, boardLayout) {
    const columnStart = placement.fineColumnStart;
    const columnEnd = placement.fineColumnEnd;
    const rowStart = placement.fineRowStart;
    const rowEnd = placement.fineRowEnd;

    if (columnEnd > columnStart && rowEnd > rowStart) {
      return {
        columnStart: String(columnStart),
        columnEnd: String(columnEnd),
        rowStart: String(rowStart),
        rowEnd: String(rowEnd),
      };
    }

    if ((boardLayout.fineColumnCount ?? 0) > 0) {
      return {
        columnStart: String(columnStart),
        columnEnd: String(columnEnd),
        rowStart: String(rowStart),
        rowEnd: String(rowEnd),
      };
    }

    const minGridX = boardLayout.minGridX ?? 0;
    const minGridY = boardLayout.minGridY ?? 0;
    const legacyColumn = (placement.gridX - minGridX) * 2 + 1;
    const legacyRow = (placement.gridY - minGridY) * 2 + 1;
    const orientation = placement.orientation || "vertical";
    const columnSpan = orientation === "horizontal" ? 2 : 1;
    const rowSpan = orientation === "vertical" ? 2 : 1;

    return {
      columnStart: String(legacyColumn),
      columnEnd: String(legacyColumn + columnSpan),
      rowStart: String(legacyRow),
      rowEnd: String(legacyRow + rowSpan),
    };
  }

  function createBoardTile(placement, playerColors, boardLayout) {
    const tile = document.createElement("div");
    tile.className = "domino-tile board-chain-tile";
    const isDouble = placement.isDouble === true || String(placement.isDouble).toLowerCase() === "true";
    const orientation = isDouble ? "vertical" : (placement.orientation || "vertical");
    const seat = placement.playedBySeat;
    const seatColor = getSeatColor(seat, playerColors);
    const facingLow = resolveFacingPip(placement.facingLowPip, placement.lowPip);
    const facingHigh = resolveFacingPip(placement.facingHighPip, placement.highPip);
    const pipAxis = isDouble
      ? "vertical"
      : (placement.pipAxis || "horizontal").toLowerCase();
    const gridPlacement = formatGridPlacementStyle(placement, boardLayout);

    tile.dataset.orientation = orientation;
    tile.dataset.pipAxis = pipAxis;
    tile.dataset.lowPip = String(facingLow);
    tile.dataset.highPip = String(facingHigh);
    tile.dataset.facingLowPip = String(facingLow);
    tile.dataset.facingHighPip = String(facingHigh);
    tile.dataset.playedBySeat = String(seat);
    tile.dataset.seatColor = seatColor;
    tile.dataset.gridX = String(placement.gridX);
    tile.dataset.gridY = String(placement.gridY);
    tile.dataset.isDouble = String(placement.isDouble);
    tile.dataset.seatMarkerMode = "divider";
    tile.style.setProperty("--tile-grid-column-start", gridPlacement.columnStart);
    tile.style.setProperty("--tile-grid-column-end", gridPlacement.columnEnd);
    tile.style.setProperty("--tile-grid-row-start", gridPlacement.rowStart);
    tile.style.setProperty("--tile-grid-row-end", gridPlacement.rowEnd);

    tile.appendChild(createDominoHalf("domino-half-low", facingLow));
    tile.appendChild(createSeatMarker(seatColor, true));
    tile.appendChild(createDominoHalf("domino-half-high", facingHigh));
    return tile;
  }

  function createHandTile(tileView, playerColors) {
    const tile = document.createElement("div");
    tile.className = "domino-tile";
    const seatColor = getSeatColor(tileView.ownerSeat, playerColors);

    tile.dataset.orientation = "vertical";
    tile.dataset.pipAxis = "vertical";
    const lowPip = coercePipCount(tileView.lowPip);
    const highPip = coercePipCount(tileView.highPip);
    tile.dataset.lowPip = String(lowPip);
    tile.dataset.highPip = String(highPip);
    tile.dataset.ownerSeat = String(tileView.ownerSeat);
    tile.dataset.seatColor = seatColor;
    tile.dataset.seatMarkerMode = "divider";

    tile.appendChild(createDominoHalf("domino-half-low", lowPip));
    tile.appendChild(createSeatMarker(seatColor, true));
    tile.appendChild(createDominoHalf("domino-half-high", highPip));
    return tile;
  }

  function renderBoardChain(boardLayout, playerColors) {
    boardChain.innerHTML = "";
    currentBoardLayout = boardLayout;
    const tiles = (boardLayout && boardLayout.tiles) ? boardLayout.tiles.slice() : [];
    tiles.sort(function (left, right) {
      return (left.chainIndex ?? left.gridX) - (right.chainIndex ?? right.gridX);
    });

    applyBoardChainLayout(boardLayout);

    const inner = document.createElement("div");
    inner.className = "board-chain-inner";
    boardChain.appendChild(inner);

    for (let index = 0; index < tiles.length; index += 1) {
      inner.appendChild(createBoardTile(tiles[index], playerColors, boardLayout));
    }
  }

  function renderHands(handTilesBySeat, handCounts, cumulativeScores, active, playerColors) {
    handsContainer.innerHTML = "";
    for (let seat = 1; seat <= 4; seat += 1) {
      const article = document.createElement("article");
      article.className = seat === active ? "hand seat-active" : "hand";
      article.dataset.seat = String(seat);

      const title = document.createElement("h2");
      title.textContent = "Seat " + seat;

      const tilesContainer = document.createElement("div");
      tilesContainer.className = "hand-tiles";
      const seatTiles = (handTilesBySeat && handTilesBySeat[seat]) ? handTilesBySeat[seat] : [];
      for (let tileIndex = 0; tileIndex < seatTiles.length; tileIndex += 1) {
        tilesContainer.appendChild(createHandTile(seatTiles[tileIndex], playerColors));
      }

      const count = document.createElement("span");
      count.className = "tile-count";
      const tileCount = handCounts[seat] ?? seatTiles.length;
      count.dataset.count = String(tileCount);
      count.textContent = tileCount + " tiles";

      const score = document.createElement("span");
      score.className = "cumulative-score";
      const cumulative = cumulativeScores[seat] ?? 0;
      score.dataset.score = String(cumulative);
      score.textContent = "Score: " + cumulative;

      article.appendChild(title);
      article.appendChild(tilesContainer);
      article.appendChild(count);
      article.appendChild(score);
      handsContainer.appendChild(article);
    }
  }

  function renderFrameChrome(frame) {
    if (frame.isRoundComplete && frame.winnerSeat != null) {
      winner.classList.remove("hidden");
      winner.dataset.seat = String(frame.winnerSeat);
      winner.textContent = "Winner: Seat " + frame.winnerSeat;

      roundScore.classList.remove("hidden");
      roundScore.dataset.pipScore = formatPip(frame.roundPipScore);
      roundScore.textContent = "Round pip score: " + formatPip(frame.roundPipScore);
    } else {
      winner.classList.add("hidden");
      winner.dataset.seat = "";
      winner.textContent = "";

      roundScore.classList.add("hidden");
      roundScore.dataset.pipScore = "";
      roundScore.textContent = "";
    }
  }

  function resolveInitialTurnIndex(frameCount) {
    const maxTurn = Math.max(0, frameCount - 1);
    const paramTurn = params.get("turnIndex");
    if (paramTurn !== null && paramTurn !== "") {
      const parsed = Number(paramTurn);
      if (!Number.isNaN(parsed)) {
        return Math.min(Math.max(0, parsed), maxTurn);
      }
    }

    return maxTurn;
  }

  function matchSnapshotPath() {
    return "/bones/sessions/" + encodeURIComponent(activeSessionId) +
      "/matches/" + encodeURIComponent(activeMatchId);
  }

  function parseViewerUrl(viewerUrl) {
    try {
      const url = new URL(viewerUrl, window.location.origin);
      const match = url.pathname.match(
        /\/bones\/sessions\/([^/]+)\/matches\/([^/]+)\/view\/?$/);
      if (!match) {
        return null;
      }

      return {
        sessionId: decodeURIComponent(match[1]),
        matchId: decodeURIComponent(match[2]),
      };
    } catch (error) {
      return null;
    }
  }

  async function resolveLatestMatchIds() {
    try {
      const response = await fetch("/api/bones/status");
      if (!response.ok) {
        return null;
      }

      const status = await response.json();
      if (!status.viewerUrl) {
        return null;
      }

      return parseViewerUrl(status.viewerUrl);
    } catch (error) {
      return null;
    }
  }

  function applyActiveMatch(sessionId, matchId) {
    activeSessionId = sessionId;
    activeMatchId = matchId;
    root.dataset.sessionId = sessionId;
    root.dataset.matchId = matchId;
    root.dataset.followLatest = "true";

    const nextPath =
      "/bones/sessions/" + encodeURIComponent(sessionId) +
      "/matches/" + encodeURIComponent(matchId) + "/view";
    if (window.location.pathname !== nextPath) {
      window.history.replaceState(null, "", nextPath);
    }
  }

  async function waitForLatestMatchIds() {
    while (true) {
      const ids = await resolveLatestMatchIds();
      if (ids) {
        return ids;
      }

      await new Promise(function (resolve) {
        window.setTimeout(resolve, STATUS_POLL_INTERVAL_MS);
      });
    }
  }

  async function switchToMatch(sessionId, matchId) {
    if (sessionId === activeSessionId && matchId === activeMatchId) {
      return;
    }

    isReloadingSnapshot = true;
    try {
      applyActiveMatch(sessionId, matchId);
      snapshot = null;
      snapshotRevision = null;
      currentBoardLayout = null;
      root.dataset.frameError = "";
      root.dataset.frameErrorTurn = "";
      await bootstrap();
    } finally {
      isReloadingSnapshot = false;
    }
  }

  function resolveLoopRunningLabel(status) {
    if (status.capReached === true) {
      return "Stopped (cap reached)";
    }

    if (status.isRunning === true) {
      return "Running";
    }

    return "Stopped";
  }

  function formatGameBudget(gamesSimulated, maxGamesPerRun) {
    const simulated = gamesSimulated ?? 0;
    const max = maxGamesPerRun ?? 0;
    if (max === 0) {
      return String(simulated);
    }

    return simulated + " / " + max;
  }

  function resolveStageDisplay(status) {
    const stage = status.currentStage ?? "Idle";
    if (stage === "Failed" && status.failureStage) {
      return stage + " (" + status.failureStage + ")";
    }

    return stage;
  }

  function resolveMatchTurnLine(snapshotModel, turnIndex) {
    const frameCount = snapshotModel ? (snapshotModel.frameCount ?? 0) : 0;
    const y = frameCount;
    let x = turnIndex;
    if (snapshotModel && isPinnedToLatestTurn()) {
      x = y;
    }

    return {
      x: x,
      y: y,
      text: "Turn " + x + " of " + y,
    };
  }

  function formatLearningPlayerSummary(learningPlayer) {
    const seat = learningPlayer.seat ?? "\u2014";
    const activeStrategyId = learningPlayer.activeStrategyId ?? "\u2014";
    const candidateStrategyId = learningPlayer.candidateStrategyId;
    const effectiveness = learningPlayer.effectiveness ?? {};
    const wins = effectiveness.wins ?? 0;
    const losses = effectiveness.losses ?? 0;
    const scoreDiff = effectiveness.cumulativeScoreDifferential ?? 0;
    let summary = "Seat " + seat + ": " + activeStrategyId;
    if (candidateStrategyId) {
      summary += " (candidate: " + candidateStrategyId + ")";
    }

    summary += " W/L " + wins + "/" + losses + " \u0394" + scoreDiff;
    return summary;
  }

  function formatLibraryEffectivenessSummary(libraryEffectiveness) {
    const strategyId = libraryEffectiveness.strategyId ?? "\u2014";
    const wins = libraryEffectiveness.wins ?? 0;
    const losses = libraryEffectiveness.losses ?? 0;
    const scoreDiff = libraryEffectiveness.cumulativeScoreDifferential ?? 0;
    return strategyId + " W/L " + wins + "/" + losses + " \u0394" + scoreDiff;
  }

  function setLearningLoopPanelVisible(isVisible) {
    if (!learningLoopPanel) {
      return;
    }

    if (isVisible) {
      learningLoopPanel.classList.remove("hidden");
      learningLoopPanel.removeAttribute("aria-hidden");
      return;
    }

    learningLoopPanel.classList.add("hidden");
    learningLoopPanel.setAttribute("aria-hidden", "true");
  }

  function renderLearningLoopStatus(status, snapshotModel, turnIndex) {
    if (!learningLoopPanel) {
      return;
    }

    if (!status) {
      setLearningLoopPanelVisible(false);
      return;
    }

    setLearningLoopPanelVisible(true);

    const isRunning = status.isRunning === true;
    const capReached = status.capReached === true;
    const iterationCount = status.iterationCount ?? 0;
    const currentStage = status.currentStage ?? "Idle";
    const failureStage = status.failureStage ?? "";
    const gamesSimulated = status.gamesSimulated ?? 0;
    const maxGamesPerRun = status.maxGamesPerRun ?? 0;
    const learningPlayerStatus = status.learningPlayerStatus ?? "";
    const turnLine = resolveMatchTurnLine(snapshotModel, turnIndex);

    learningLoopPanel.dataset.isRunning = String(isRunning);
    learningLoopPanel.dataset.capReached = String(capReached);
    learningLoopPanel.dataset.iterationCount = String(iterationCount);
    learningLoopPanel.dataset.currentStage = currentStage;
    learningLoopPanel.dataset.failureStage = failureStage;
    learningLoopPanel.dataset.gamesSimulated = String(gamesSimulated);
    learningLoopPanel.dataset.maxGamesPerRun = String(maxGamesPerRun);
    learningLoopPanel.dataset.learningPlayerStatus = learningPlayerStatus;
    learningLoopPanel.dataset.turnIndex = String(turnLine.x);
    learningLoopPanel.dataset.frameCount = String(turnLine.y);

    if (loopRunningStatus) {
      loopRunningStatus.dataset.isRunning = String(isRunning);
      loopRunningStatus.dataset.capReached = String(capReached);
    }

    if (loopRunningValue) {
      loopRunningValue.textContent = resolveLoopRunningLabel(status);
    }

    if (loopIteration) {
      loopIteration.dataset.iterationCount = String(iterationCount);
    }

    if (loopIterationValue) {
      loopIterationValue.textContent = "Iteration " + iterationCount;
    }

    if (loopStage) {
      loopStage.dataset.currentStage = currentStage;
      loopStage.dataset.failureStage = failureStage;
    }

    if (loopStageValue) {
      loopStageValue.textContent = resolveStageDisplay(status);
    }

    if (loopGameBudget) {
      loopGameBudget.dataset.gamesSimulated = String(gamesSimulated);
      loopGameBudget.dataset.maxGamesPerRun = String(maxGamesPerRun);
    }

    if (loopGameBudgetValue) {
      loopGameBudgetValue.textContent = formatGameBudget(gamesSimulated, maxGamesPerRun);
    }

    if (loopMatchTurns) {
      loopMatchTurns.dataset.turnIndex = String(turnLine.x);
      loopMatchTurns.dataset.frameCount = String(turnLine.y);
    }

    if (loopMatchTurnsValue) {
      loopMatchTurnsValue.textContent = turnLine.text;
    }

    const isComplete = snapshotModel && snapshotModel.isComplete === true;
    const winnerSeat = snapshotModel && snapshotModel.winnerSeat != null
      ? snapshotModel.winnerSeat
      : null;
    learningLoopPanel.dataset.isComplete = String(isComplete);
    learningLoopPanel.dataset.winnerSeat = winnerSeat != null ? String(winnerSeat) : "";

    if (loopMatchComplete && loopMatchCompleteValue) {
      if (isComplete && winnerSeat != null) {
        loopMatchComplete.classList.remove("hidden");
        loopMatchComplete.dataset.isComplete = "true";
        loopMatchComplete.dataset.winnerSeat = String(winnerSeat);
        loopMatchCompleteValue.textContent = "Winner: Seat " + winnerSeat;
      } else {
        loopMatchComplete.classList.add("hidden");
        loopMatchComplete.dataset.isComplete = "false";
        loopMatchComplete.dataset.winnerSeat = "";
        loopMatchCompleteValue.textContent = "\u2014";
      }
    }

    const learningPlayer = status.learningPlayer;
    if (loopLearningPlayer && loopLearningPlayerValue) {
      if (learningPlayer) {
        loopLearningPlayer.classList.remove("hidden");
        loopLearningPlayer.dataset.seat = String(learningPlayer.seat ?? "");
        loopLearningPlayer.dataset.activeStrategyId = learningPlayer.activeStrategyId ?? "";
        loopLearningPlayer.dataset.candidateStrategyId = learningPlayer.candidateStrategyId ?? "";
        const effectiveness = learningPlayer.effectiveness ?? {};
        loopLearningPlayer.dataset.wins = String(effectiveness.wins ?? 0);
        loopLearningPlayer.dataset.losses = String(effectiveness.losses ?? 0);
        loopLearningPlayer.dataset.scoreDifferential = String(effectiveness.cumulativeScoreDifferential ?? 0);
        loopLearningPlayerValue.textContent = formatLearningPlayerSummary(learningPlayer);
      } else {
        loopLearningPlayer.classList.add("hidden");
        loopLearningPlayer.dataset.seat = "";
        loopLearningPlayer.dataset.activeStrategyId = "";
        loopLearningPlayer.dataset.candidateStrategyId = "";
        loopLearningPlayer.dataset.wins = "";
        loopLearningPlayer.dataset.losses = "";
        loopLearningPlayer.dataset.scoreDifferential = "";
        loopLearningPlayerValue.textContent = "\u2014";
      }
    }

    if (loopPonderPending) {
      loopPonderPending.dataset.learningPlayerStatus = learningPlayerStatus;
      if (!learningPlayer && learningPlayerStatus === "pending") {
        loopPonderPending.classList.remove("hidden");
      } else {
        loopPonderPending.classList.add("hidden");
      }
    }

    if (loopLearningPlayerError && loopLearningPlayerErrorValue) {
      const learningPlayerError = status.learningPlayerError ?? "";
      if (learningPlayerError) {
        loopLearningPlayerError.classList.remove("hidden");
        loopLearningPlayerError.dataset.learningPlayerError = learningPlayerError;
        loopLearningPlayerErrorValue.textContent = learningPlayerError;
      } else {
        loopLearningPlayerError.classList.add("hidden");
        loopLearningPlayerError.dataset.learningPlayerError = "";
        loopLearningPlayerErrorValue.textContent = "\u2014";
      }
    }

    if (loopLastIterationError && loopLastIterationErrorValue) {
      const lastIterationError = status.lastIterationError;
      if (lastIterationError && lastIterationError.message) {
        const errorStage = lastIterationError.stage ?? "";
        loopLastIterationError.classList.remove("hidden");
        loopLastIterationError.dataset.failureStage = errorStage;
        loopLastIterationErrorValue.textContent = lastIterationError.message +
          (errorStage ? " (" + errorStage + ")" : "");
      } else {
        loopLastIterationError.classList.add("hidden");
        loopLastIterationError.dataset.failureStage = "";
        loopLastIterationErrorValue.textContent = "\u2014";
      }
    }

    const lastPromotionOutcome = status.lastPromotionOutcome ?? "";
    learningLoopPanel.dataset.lastPromotionOutcome = lastPromotionOutcome;
    if (loopLastPromotion && loopLastPromotionValue) {
      if (lastPromotionOutcome) {
        loopLastPromotion.classList.remove("hidden");
        loopLastPromotion.dataset.lastPromotionOutcome = lastPromotionOutcome;
        loopLastPromotionValue.textContent = lastPromotionOutcome;
      } else {
        loopLastPromotion.classList.add("hidden");
        loopLastPromotion.dataset.lastPromotionOutcome = "";
        loopLastPromotionValue.textContent = "\u2014";
      }
    }

    const libraryEffectiveness = status.libraryEffectiveness;
    if (loopLibraryBest && loopLibraryBestValue) {
      if (libraryEffectiveness) {
        loopLibraryBest.classList.remove("hidden");
        loopLibraryBest.dataset.libraryStrategyId = libraryEffectiveness.strategyId ?? "";
        loopLibraryBest.dataset.libraryEffectiveness = formatLibraryEffectivenessSummary(libraryEffectiveness);
        loopLibraryBestValue.textContent = formatLibraryEffectivenessSummary(libraryEffectiveness);
      } else {
        loopLibraryBest.classList.add("hidden");
        loopLibraryBest.dataset.libraryStrategyId = "";
        loopLibraryBest.dataset.libraryEffectiveness = "";
        loopLibraryBestValue.textContent = "\u2014";
      }
    }

    const modelProvider = status.modelProvider ?? {};
    const provider = modelProvider.provider ?? "";
    const model = modelProvider.model ?? "";
    if (loopModelProvider) {
      loopModelProvider.dataset.provider = provider;
      loopModelProvider.dataset.model = model;
    }

    if (loopModelProviderValue) {
      if (provider && model) {
        loopModelProviderValue.textContent = provider + " / " + model;
      } else if (provider) {
        loopModelProviderValue.textContent = provider;
      } else if (model) {
        loopModelProviderValue.textContent = model;
      } else {
        loopModelProviderValue.textContent = "\u2014";
      }
    }
  }

  async function pollLearningLoopStatus() {
    try {
      const response = await fetch("/api/bones/status");
      if (!response.ok) {
        lastLearningLoopStatus = null;
        renderLearningLoopStatus(null, snapshot, Number(scrubber.value));
        return;
      }

      const status = await response.json();
      lastLearningLoopStatus = status;
      renderLearningLoopStatus(status, snapshot, Number(scrubber.value));
    } catch (error) {
      lastLearningLoopStatus = null;
      renderLearningLoopStatus(null, snapshot, Number(scrubber.value));
    }
  }

  function applySnapshotMetadata(model, turnIndex) {
    snapshot = model;
    snapshotRevision = model.revision;
    matchLabel.textContent = "Live match: " + model.matchId;
    root.dataset.frameCount = String(model.frameCount);
    root.dataset.revision = String(model.revision);

    scrubber.min = "0";
    scrubber.max = String(Math.max(0, model.frameCount - 1));
    scrubber.value = String(turnIndex);
    scrubber.dataset.turnIndex = scrubber.value;
    turnIndexOutput.dataset.turnIndex = scrubber.value;
    turnIndexOutput.textContent = "Turn " + scrubber.value;
    renderLearningLoopStatus(lastLearningLoopStatus, model, turnIndex);
  }

  function isPinnedToLatestTurn() {
    if (!snapshot) {
      return true;
    }

    const maxTurn = Math.max(0, snapshot.frameCount - 1);
    return Number(scrubber.value) >= maxTurn;
  }

  async function pollLiveSnapshot() {
    if (isReloadingSnapshot || snapshotRevision === null) {
      return;
    }

    try {
      const response = await fetch(matchSnapshotPath());
      if (!response.ok) {
        return;
      }

      const model = await response.json();
      if (model.revision === snapshotRevision) {
        return;
      }

      const pinnedToLatest = isPinnedToLatestTurn();
      const preferredTurnIndex = Number(scrubber.value);
      const maxTurn = Math.max(0, model.frameCount - 1);
      const turnIndex = pinnedToLatest
        ? maxTurn
        : Math.min(Math.max(0, preferredTurnIndex), maxTurn);

      applySnapshotMetadata(model, turnIndex);

      if (model.frameCount > 0) {
        await loadFrame(turnIndex);
      } else {
        renderSnapshot(model);
      }
    } catch (error) {
      // Ignore transient poll failures; scrub/load paths still recover.
    }
  }

  async function pollLiveUpdates() {
    if (isReloadingSnapshot) {
      return;
    }

    if (isPinnedToLatestTurn()) {
      const latest = await resolveLatestMatchIds();
      if (latest &&
          (latest.sessionId !== activeSessionId || latest.matchId !== activeMatchId)) {
        await switchToMatch(latest.sessionId, latest.matchId);
        return;
      }
    }

    await pollLiveSnapshot();
  }

  async function reloadSnapshot(preferredTurnIndex) {
    if (isReloadingSnapshot) {
      return null;
    }

    isReloadingSnapshot = true;
    try {
      const response = await fetch(matchSnapshotPath());
      if (!response.ok) {
        return null;
      }

      const model = await response.json();
      const maxTurn = Math.max(0, model.frameCount - 1);
      const turnIndex = Math.min(Math.max(0, preferredTurnIndex), maxTurn);
      applySnapshotMetadata(model, turnIndex);
      return { model: model, turnIndex: turnIndex };
    } finally {
      isReloadingSnapshot = false;
    }
  }

  function renderSnapshot(model) {
    const finalTurn = Math.max(0, model.frameCount - 1);
    applySnapshotMetadata(model, finalTurn);

    leftEnd.dataset.pip = formatPip(model.leftEndPip);
    leftEnd.textContent = formatPip(model.leftEndPip);
    rightEnd.dataset.pip = formatPip(model.rightEndPip);
    rightEnd.textContent = formatPip(model.rightEndPip);

    renderBoardChain(model.boardLayout, model.playerColorsBySeat);
    renderHands(
      model.handTilesBySeat,
      model.handTileCountsBySeat,
      model.cumulativeScoresBySeat,
      model.activeSeat,
      model.playerColorsBySeat);

    activeSeat.dataset.seat = String(model.activeSeat);
    activeSeat.textContent = "Active seat: " + model.activeSeat;

    if (model.isComplete && model.winnerSeat != null) {
      winner.classList.remove("hidden");
      winner.dataset.seat = String(model.winnerSeat);
      winner.textContent = "Winner: Seat " + model.winnerSeat;

      roundScore.classList.remove("hidden");
      roundScore.dataset.pipScore = formatPip(model.roundPipScore);
      roundScore.textContent = "Round pip score: " + formatPip(model.roundPipScore);
    } else {
      winner.classList.add("hidden");
      roundScore.classList.add("hidden");
    }
  }

  function updateTurnLabel(turnIndex) {
    const turnText = String(turnIndex);
    scrubber.value = turnText;
    scrubber.dataset.turnIndex = turnText;
    turnIndexOutput.dataset.turnIndex = turnText;
    turnIndexOutput.textContent = "Turn " + turnText;
    renderLearningLoopStatus(lastLearningLoopStatus, snapshot, turnIndex);
  }

  function renderFrameLoadError(turnIndex) {
    root.dataset.frameError = "true";
    root.dataset.frameErrorTurn = String(turnIndex);

    boardChain.innerHTML = "";
    currentBoardLayout = null;
    handsContainer.innerHTML = "";

    leftEnd.dataset.pip = "\u2014";
    leftEnd.textContent = "\u2014";
    rightEnd.dataset.pip = "\u2014";
    rightEnd.textContent = "\u2014";

    activeSeat.dataset.seat = "";
    activeSeat.textContent = "";

    winner.classList.add("hidden");
    winner.dataset.seat = "";
    winner.textContent = "";
    roundScore.classList.add("hidden");
    roundScore.dataset.pipScore = "";
    roundScore.textContent = "";

    lastMove.dataset.kind = "error";
    lastMove.dataset.lowPip = "";
    lastMove.dataset.highPip = "";
    lastMove.dataset.boardSide = "";
    lastMove.textContent = "Failed to load turn " + turnIndex;

    updateTurnLabel(turnIndex);
  }

  function renderFrame(frame) {
    root.dataset.frameError = "";
    root.dataset.frameErrorTurn = "";

    leftEnd.dataset.pip = formatPip(frame.leftEndPip);
    leftEnd.textContent = formatPip(frame.leftEndPip);
    rightEnd.dataset.pip = formatPip(frame.rightEndPip);
    rightEnd.textContent = formatPip(frame.rightEndPip);

    renderBoardChain(frame.boardLayout, frame.playerColorsBySeat || snapshot.playerColorsBySeat);
    renderHands(
      frame.handTilesBySeat,
      frame.handTileCountsBySeat,
      snapshot.cumulativeScoresBySeat,
      frame.activeSeat,
      frame.playerColorsBySeat || snapshot.playerColorsBySeat);

    activeSeat.dataset.seat = String(frame.activeSeat);
    activeSeat.textContent = "Active seat: " + frame.activeSeat;

    const kind = frame.eventKind ?? "none";
    lastMove.dataset.kind = kind;
    lastMove.dataset.lowPip = formatPip(frame.playedTileLowPip);
    lastMove.dataset.highPip = formatPip(frame.playedTileHighPip);
    lastMove.dataset.boardSide = frame.boardSide ?? "";

    if (kind === "pass") {
      lastMove.textContent = "Pass at turn " + frame.turnIndex;
    } else if (frame.playedTileLowPip != null && frame.playedTileHighPip != null) {
      lastMove.textContent =
        "Played " + frame.playedTileLowPip + "-" + frame.playedTileHighPip +
        " on " + frame.boardSide + " at turn " + frame.turnIndex;
    } else {
      lastMove.textContent = "Turn " + frame.turnIndex;
    }

    updateTurnLabel(frame.turnIndex);

    renderFrameChrome(frame);
  }

  async function loadFrame(turnIndex) {
    let response;
    try {
      response = await fetch(matchSnapshotPath() + "/frames/" + turnIndex);
    } catch (error) {
      const refreshed = await reloadSnapshot(turnIndex);
      if (refreshed && refreshed.model.frameCount > 0) {
        await loadFrame(refreshed.turnIndex);
        return;
      }

      renderFrameLoadError(turnIndex);
      return;
    }

    if (!response.ok) {
      const refreshed = await reloadSnapshot(turnIndex);
      if (refreshed && refreshed.model.frameCount > 0) {
        await loadFrame(refreshed.turnIndex);
        return;
      }

      renderFrameLoadError(turnIndex);
      return;
    }

    const frame = await response.json();
    if (snapshotRevision !== null && frame.revision !== snapshotRevision) {
      const refreshed = await reloadSnapshot(turnIndex);
      if (refreshed && refreshed.model.frameCount > 0) {
        await loadFrame(refreshed.turnIndex);
        return;
      }

      renderFrameLoadError(turnIndex);
      return;
    }

    renderFrame(frame);
  }

  async function bootstrap() {
    const response = await fetch(matchSnapshotPath());
    if (!response.ok) {
      matchLabel.textContent = "Failed to load match.";
      return;
    }

    const model = await response.json();
    const initialTurnIndex = resolveInitialTurnIndex(model.frameCount);

    applySnapshotMetadata(model, initialTurnIndex);

    if (model.frameCount > 0) {
      await loadFrame(initialTurnIndex);
    } else {
      renderSnapshot(model);
    }
  }

  async function initViewer() {
    if (!activeSessionId || !activeMatchId) {
      matchLabel.textContent = "Waiting for learning loop…";
      const ids = await waitForLatestMatchIds();
      applyActiveMatch(ids.sessionId, ids.matchId);
    } else {
      applyActiveMatch(activeSessionId, activeMatchId);
    }

    await bootstrap();
    window.setInterval(pollLiveUpdates, LIVE_POLL_INTERVAL_MS);
    window.setInterval(pollLearningLoopStatus, STATUS_POLL_INTERVAL_MS);
    void pollLearningLoopStatus();
  }

  scrubber.addEventListener("input", function () {
    const turnIndex = Number(scrubber.value);
    updateTurnLabel(turnIndex);
    loadFrame(turnIndex);
  });

  window.addEventListener("resize", function () {
    if (currentBoardLayout) {
      applyBoardChainLayout(currentBoardLayout);
    }
  });

  initViewer();
})();
