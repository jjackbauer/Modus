using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Wip.Bones.Web;
using Wip.Bones.Web.Tests.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

[Collection("BonesViewerPlaywright")]
public sealed class BonesViewerDominoRenderingDomTests : IClassFixture<BonesPlaywrightHost>
{
	private enum ScriptedStepKind
	{
		Opening = 1,
		AnyOnSide,
		Tile
	}

	private sealed record ScriptedPlayStep(ScriptedStepKind Kind, int? LowPip, int? HighPip, BonesBoardSide? Side)
	{
		public static ScriptedPlayStep Opening => new ScriptedPlayStep(ScriptedStepKind.Opening, null, null, null);

		public static ScriptedPlayStep AnyOn(BonesBoardSide side)
		{
			return new ScriptedPlayStep(ScriptedStepKind.AnyOnSide, null, null, side);
		}

		public static ScriptedPlayStep Play(int lowPip, int highPip, BonesBoardSide side)
		{
			return new ScriptedPlayStep(ScriptedStepKind.Tile, lowPip, highPip, side);
		}
	}

	private sealed record RegisteredDominoRenderingMatch(BonesGameId MatchId, int FinalTurnIndex, BonesBoardVisualLayout Layout);

	private sealed class PreferRightLegalMovePlayerSlot : IBonesPlayerSlot
	{
		public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
		{
			ArgumentNullException.ThrowIfNull(state, "state");
			ArgumentNullException.ThrowIfNull(legalMoves, "legalMoves");
			if (legalMoves.Count == 0)
			{
				throw new InvalidOperationException("No legal moves are available for the active player.");
			}
			BonesMove bonesMove = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass && move.Side == BonesBoardSide.Right);
			if ((object)bonesMove != null)
			{
				return bonesMove;
			}
			BonesMove bonesMove2 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass);
			return bonesMove2 ?? legalMoves[0];
		}
	}

	private sealed class PreferRightDoubleSixEndLegalMovePlayerSlot : IBonesPlayerSlot
	{
		public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
		{
			ArgumentNullException.ThrowIfNull(state, "state");
			ArgumentNullException.ThrowIfNull(legalMoves, "legalMoves");
			if (legalMoves.Count == 0)
			{
				throw new InvalidOperationException("No legal moves are available for the active player.");
			}
			BonesMove bonesMove = legalMoves.FirstOrDefault(delegate(BonesMove move)
			{
				int result;
				if (!move.IsPass && move.Side == BonesBoardSide.Right)
				{
					BonesTile? tile = move.Tile;
					if (tile.HasValue)
					{
						BonesTile valueOrDefault = tile.GetValueOrDefault();
						if (valueOrDefault.IsDouble && valueOrDefault.LowPip.Value == 6)
						{
							result = ((valueOrDefault.HighPip.Value == 6) ? 1 : 0);
							goto IL_0061;
						}
					}
				}
				result = 0;
				goto IL_0061;
				IL_0061:
				return (byte)result != 0;
			});
			if ((object)bonesMove != null)
			{
				return bonesMove;
			}
			BonesMove bonesMove2 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass && move.Side == BonesBoardSide.Right);
			if ((object)bonesMove2 != null)
			{
				return bonesMove2;
			}
			BonesMove bonesMove3 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass);
			return bonesMove3 ?? legalMoves[0];
		}
	}

	private sealed class PreferBothArmsLegalMovePlayerSlot : IBonesPlayerSlot
	{
		private int _chooseCount;

		public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
		{
			ArgumentNullException.ThrowIfNull(state, "state");
			ArgumentNullException.ThrowIfNull(legalMoves, "legalMoves");
			if (legalMoves.Count == 0)
			{
				throw new InvalidOperationException("No legal moves are available for the active player.");
			}
			if ((_chooseCount++ & 1) == 0)
			{
				BonesMove bonesMove = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass && move.Side == BonesBoardSide.Right);
				if ((object)bonesMove != null)
				{
					return bonesMove;
				}
				BonesMove bonesMove2 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass && move.Side == BonesBoardSide.Left);
				if ((object)bonesMove2 != null)
				{
					return bonesMove2;
				}
			}
			else
			{
				BonesMove bonesMove3 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass && move.Side == BonesBoardSide.Left);
				if ((object)bonesMove3 != null)
				{
					return bonesMove3;
				}
				BonesMove bonesMove4 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass && move.Side == BonesBoardSide.Right);
				if ((object)bonesMove4 != null)
				{
					return bonesMove4;
				}
			}
			BonesMove bonesMove5 = legalMoves.FirstOrDefault((BonesMove move) => !move.IsPass);
			return bonesMove5 ?? legalMoves[0];
		}
	}

	private readonly BonesPlaywrightHost _playwrightHost;

	public BonesViewerDominoRenderingDomTests(BonesPlaywrightHost playwrightHost)
	{
		_playwrightHost = playwrightHost;
	}

	[Fact]
	[Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.DomJunctionParity)]
	public async Task BonesViewerDominoRenderingDomTests_GivenLearningMatchReplay_ExpectedAdjacentDomPipsMatchFrameJson()
	{
		var (finalState, _) = LearningMatch79daReplayHelper.ReplayFinalRound();
		var viewer = new BonesMatchViewerService(new BonesGameEngine());
		var frameLayout = viewer.BuildSnapshot(finalState).BoardLayout;
		Assert.True(frameLayout.Tiles.Count >= 14);

		var page = await BonesViewerScriptTestDriver.OpenBoardLayoutRenderedPageAsync(
			await BonesViewerScriptTestDriver.GetSharedBrowserAsync(),
			_playwrightHost.ListeningUri,
			frameLayout);
		try
		{
			var domBoard = await BonesViewerScriptTestDriver.GetBoardDomPipStateByGridAsync(page);
			Assert.Equal(frameLayout.Tiles.Count, domBoard.Count);

			var orderedPlacements = frameLayout.Tiles.OrderBy(static tile => tile.ChainIndex).ToArray();
			for (var index = 0; index < orderedPlacements.Length; index++)
			{
				var expected = orderedPlacements[index];
				var gridKey = (expected.GridX, expected.GridY);
				Assert.True(domBoard.TryGetValue(gridKey, out var actual));
				Assert.Equal(expected.FacingLowPip, actual.LowHalfPip);
				Assert.Equal(expected.FacingHighPip, actual.HighHalfPip);
			}

			for (var index = 0; index < orderedPlacements.Length - 1; index++)
			{
				var left = orderedPlacements[index];
				var right = orderedPlacements[index + 1];
				Assert.Equal(left.FacingHighPip, right.FacingLowPip);
			}

			var oneThreePlacement = orderedPlacements.Single(
				tile => tile.LowPip == 1 && tile.HighPip == 3);
			var oneFivePlacement = orderedPlacements.Single(
				tile => tile.LowPip == 1 && tile.HighPip == 5);
			Assert.Equal(1, oneThreePlacement.FacingHighPip);
			Assert.Equal(1, oneFivePlacement.FacingLowPip);
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add frame API + DOM parity test: for each board tile at a seeded turn, `facingLowPip`/`facingHighPip` in JSON equal `.domino-half-low`/`.domino-half-high` `data-pip` after scrub [foundation for blank-tile diagnosis] [mandatory - JSON DOM pip parity]")]
	public async Task BonesViewerPage_GivenEngineReplayedFrame_ExpectedBoardHalfDataPipMatchesFrameJson()
	{
		BonesGameId matchId = new BonesGameId("match-board-pip-parity");
		RegisterSeededMatch("session-board-pip-parity", matchId, 9001);
		BonesMatchViewerService viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
		Assert.True(_playwrightHost.Catalog.TryGet(new SessionId("session-board-pip-parity"), matchId, out BonesRegisteredMatch registered));
		BonesMatchViewModel snapshot = viewer.BuildMatchSnapshot(new SessionId("session-board-pip-parity"), registered);
		IReadOnlyList<BonesMatchFrame> frames = viewer.BuildMatchTimeline(registered);
		int targetTurn = Math.Max(0, snapshot.FrameCount / 2);
		BonesMatchFrame expectedFrame = frames[targetTurn];
		Assert.True(expectedFrame.BoardLayout.Tiles.Count > 0, "Seeded mid-turn must have at least one board tile for pip parity proof.");
		using HttpClient client = _playwrightHost.CreateListeningClient();
		HttpResponseMessage frameResponse = await client.GetAsync($"/bones/sessions/{"session-board-pip-parity"}/matches/{matchId.Value}/frames/{targetTurn}");
		Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);
		BonesMatchFrame frameApi = await frameResponse.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);
		Assert.NotNull(frameApi);
		Assert.Equal(expectedFrame.TurnIndex, frameApi.TurnIndex);
		Assert.Equal(expectedFrame.BoardLayout.Tiles.Count, frameApi.BoardLayout.Tiles.Count);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-board-pip-parity", matchId.Value);
		try
		{
			await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
			Assert.True(await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn), "Scrubber must load the seeded mid-turn frame before DOM parity assertions.");
			await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
			IReadOnlyDictionary<(int GridX, int GridY), BonesViewerScriptTestDriver.BonesViewerBoardTileDomPipState> domBoard = await BonesViewerScriptTestDriver.GetBoardDomPipStateByGridAsync(page);
			Assert.Equal(frameApi.BoardLayout.Tiles.Count, domBoard.Count);
			foreach (BonesBoardTilePlacement expected in frameApi.BoardLayout.Tiles)
			{
				(int GridX, int GridY) gridKey = (GridX: expected.GridX, GridY: expected.GridY);
				Assert.True(domBoard.TryGetValue(gridKey, out BonesViewerScriptTestDriver.BonesViewerBoardTileDomPipState actual), $"Board tile at grid ({expected.GridX}, {expected.GridY}) must be present in DOM after scrub.");
				Assert.Equal(expected.FacingLowPip, actual.LowHalfPip);
				Assert.Equal(expected.FacingHighPip, actual.HighHalfPip);
				actual = null;
			}
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add hand parity test: each hand tile JSON `lowPip`/`highPip` equals DOM half `data-pip`; non-zero halves have expected `.pip` child count [depends on pip parity foundation] [mandatory - hand pip parity]")]
	public async Task BonesViewerPage_GivenHandTiles_ExpectedHalfDataPipMatchesFrameJson()
	{
		BonesGameId matchId = new BonesGameId("match-hand-pip-parity");
		RegisterSeededMatch("session-hand-pip-parity", matchId, 9001);
		BonesMatchViewerService viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
		Assert.True(_playwrightHost.Catalog.TryGet(new SessionId("session-hand-pip-parity"), matchId, out BonesRegisteredMatch registered));
		BonesMatchViewModel snapshot = viewer.BuildMatchSnapshot(new SessionId("session-hand-pip-parity"), registered);
		IReadOnlyList<BonesMatchFrame> frames = viewer.BuildMatchTimeline(registered);
		int targetTurn = Math.Max(0, snapshot.FrameCount / 2);
		BonesMatchFrame expectedFrame = frames[targetTurn];
		Assert.True(expectedFrame.HandTilesBySeat.Values.Any((IReadOnlyList<BonesHandTileView> tiles) => tiles.Count > 0), "Seeded mid-turn must have at least one hand tile for pip parity proof.");
		using HttpClient client = _playwrightHost.CreateListeningClient();
		HttpResponseMessage frameResponse = await client.GetAsync($"/bones/sessions/{"session-hand-pip-parity"}/matches/{matchId.Value}/frames/{targetTurn}");
		Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);
		BonesMatchFrame frameApi = await frameResponse.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions);
		Assert.NotNull(frameApi);
		Assert.Equal(expectedFrame.TurnIndex, frameApi.TurnIndex);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-hand-pip-parity", matchId.Value);
		try
		{
			await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
			Assert.True(await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn), "Scrubber must load the seeded mid-turn frame before hand DOM parity assertions.");
			await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
			IReadOnlyDictionary<int, BonesViewerScriptTestDriver.BonesViewerHandDomState> domHands = await BonesViewerScriptTestDriver.GetHandDomStateBySeatAsync(page);
			for (int seat = 1; seat <= 4; seat++)
			{
				Assert.True(frameApi.HandTilesBySeat.TryGetValue(seat, out IReadOnlyList<BonesHandTileView> expectedTiles), $"Frame API must include hand tiles for seat {seat}.");
				Assert.True(domHands.TryGetValue(seat, out BonesViewerScriptTestDriver.BonesViewerHandDomState domHand), $"Seat {seat} hand must be present in DOM.");
				Assert.Equal(expectedTiles.Count, domHand.Tiles.Count);
				for (int tileIndex = 0; tileIndex < expectedTiles.Count; tileIndex++)
				{
					BonesHandTileView expected = expectedTiles[tileIndex];
					BonesViewerScriptTestDriver.BonesViewerHandTileDomState actual = domHand.Tiles[tileIndex];
					Assert.Equal(expected.LowPip, actual.LowHalfPip);
					Assert.Equal(expected.HighPip, actual.HighHalfPip);
				}
				expectedTiles = null;
				domHand = null;
			}
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add hand parity test: each hand tile JSON `lowPip`/`highPip` equals DOM half `data-pip`; non-zero halves have expected `.pip` child count [depends on pip parity foundation] [mandatory - hand pip parity]")]
	public async Task BonesViewerPage_GivenNonZeroHalfPip_ExpectedVisiblePipCountMatchesStandardLayout()
	{
		BonesGameId matchId = new BonesGameId("match-hand-pip-count");
		RegisterSeededMatch("session-hand-pip-count", matchId, 9001);
		BonesMatchViewerService viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
		Assert.True(_playwrightHost.Catalog.TryGet(new SessionId("session-hand-pip-count"), matchId, out BonesRegisteredMatch registered));
		BonesMatchViewModel snapshot = viewer.BuildMatchSnapshot(new SessionId("session-hand-pip-count"), registered);
		viewer.BuildMatchTimeline(registered);
		int targetTurn = Math.Max(0, snapshot.FrameCount / 2);
		using HttpClient client = _playwrightHost.CreateListeningClient();
		HttpResponseMessage frameResponse = await client.GetAsync($"/bones/sessions/{"session-hand-pip-count"}/matches/{matchId.Value}/frames/{targetTurn}");
		Assert.Equal(HttpStatusCode.OK, frameResponse.StatusCode);
		Assert.NotNull(await frameResponse.Content.ReadFromJsonAsync<BonesMatchFrame>(BonesWebHost.JsonOptions));
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-hand-pip-count", matchId.Value);
		try
		{
			await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
			Assert.True(await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn), "Scrubber must load the seeded mid-turn frame before hand pip count assertions.");
			await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
			IReadOnlyDictionary<int, BonesViewerScriptTestDriver.BonesViewerHandDomState> domHands = await BonesViewerScriptTestDriver.GetHandDomStateBySeatAsync(page);
			bool assertedNonZeroHalf = false;
			for (int seat = 1; seat <= 4; seat++)
			{
				Assert.True(domHands.TryGetValue(seat, out BonesViewerScriptTestDriver.BonesViewerHandDomState domHand), $"Seat {seat} hand must be present in DOM.");
				foreach (BonesViewerScriptTestDriver.BonesViewerHandTileDomState tile in domHand.Tiles)
				{
					AssertNonZeroHalfPipCount(tile.LowHalfPip, tile.LowHalfVisiblePipCount, seat, "low");
					AssertNonZeroHalfPipCount(tile.HighHalfPip, tile.HighHalfVisiblePipCount, seat, "high");
					if (tile.LowHalfPip > 0 || tile.HighHalfPip > 0)
					{
						assertedNonZeroHalf = true;
					}
				}
				domHand = null;
			}
			Assert.True(assertedNonZeroHalf, "Seeded mid-turn must include at least one hand half with non-zero pip for visible pip count proof.");
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Render standard pip grid on board tiles for all orientations and `pipAxis` values (horizontal, vertical on opening, vertical on branch doubles) [depends on pip layout sync] [mandatory - standard pip layout]")]
	public async Task BonesViewerPage_GivenBoardTileOrientations_ExpectedStandardPipPositionsInDom()
	{
		BonesBoardVisualLayout layout = BuildBoardTileOrientationsVisualLayout();
		IPage page = await BonesViewerScriptTestDriver.OpenBoardLayoutRenderedPageAsync(
			await BonesViewerScriptTestDriver.GetSharedBrowserAsync(),
			_playwrightHost.ListeningUri,
			layout);
		try
		{
			IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState> boardTiles = await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page);
			Assert.True(boardTiles.Count >= 3);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState opening = boardTiles.Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX == 0 && tile.GridY == 0);
			Assert.Equal("vertical", opening.Orientation);
			Assert.Equal("horizontal", opening.PipAxis);
			AssertBoardHalfStandardLayout(opening.LowHalf);
			AssertBoardHalfStandardLayout(opening.HighHalf);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState horizontal = boardTiles.First((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.Orientation == "horizontal" && tile.GridY == 0);
			Assert.Equal("horizontal", horizontal.PipAxis);
			AssertBoardHalfStandardLayout(horizontal.LowHalf);
			AssertBoardHalfStandardLayout(horizontal.HighHalf);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState chainDouble = boardTiles.First((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.IsDouble);
			Assert.Equal("vertical", chainDouble.Orientation);
			Assert.Equal("vertical", chainDouble.PipAxis);
			Assert.True(chainDouble.IsDouble);
			AssertBoardHalfStandardLayout(chainDouble.LowHalf);
			AssertBoardHalfStandardLayout(chainDouble.HighHalf);
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Prove left-arm plays produce negative `gridX` placements flush to opening with spatial connecting pip matching engine left end [depends on facing + geometry] [mandatory - bidirectional chain]")]
	public async Task BonesViewerPage_GivenLeftArmReplay_ExpectedNegativeGridXAndMatchingSpatialPip()
	{
		RegisteredDominoRenderingMatch registered = RegisterScriptedMatch("session-left-arm-dom", "match-left-arm-dom", new ScriptedPlayStep[]
		{
			ScriptedPlayStep.Opening,
			ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
			ScriptedPlayStep.AnyOn(BonesBoardSide.Left)
		}, (BonesBoardVisualLayout layout) => layout.MinGridX < 0);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-left-arm-dom", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState> boardTiles = await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState leftNeighbor = (from tile in boardTiles
				where tile.GridX < 0
				orderby tile.GridX descending
				select tile).First();
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState opening = boardTiles.Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX == 0 && tile.GridY == 0);
			Assert.True(leftNeighbor.GridX < 0);
			Assert.Equal(opening.LowHalf.Pip, leftNeighbor.HighHalf.Pip);
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add greedy engine-replayed DOM test: when frame `maxGridX > 0`, at least one positive `gridX` tile bbox is strictly right of opening bbox inside `#board-chain` [foundation for right-arm visibility] [mandatory - right arm spatial visibility]")]
	public async Task BonesViewerPage_GivenGreedyMatchWithRightPlays_ExpectedPositiveGridXTileRightOfOpeningBBox()
	{
		RegisteredDominoRenderingMatch registered = RegisterGreedyRightArmMatch("session-greedy-right-arm-bbox", "match-greedy-right-arm-bbox");
		Assert.True(registered.Layout.MaxGridX > 0, "Greedy match must include right-arm plays (maxGridX > 0).");
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-greedy-right-arm-bbox", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			IReadOnlyDictionary<(int GridX, int GridY), BonesViewerScriptTestDriver.BonesViewerDomBoundingRect> tileBoxes = await BonesViewerScriptTestDriver.GetBoardTileBoundingBoxesByGridAsync(page);
			(int GridX, int GridY) openingKey = tileBoxes.Keys.Single(((int GridX, int GridY) key) => key.GridX == 0 && key.GridY == 0);
			BonesViewerScriptTestDriver.BonesViewerDomBoundingRect openingBox = tileBoxes[openingKey];
			((int GridX, int GridY) Key, BonesViewerScriptTestDriver.BonesViewerDomBoundingRect Box)[] positiveGridXTiles = (from entry in tileBoxes
				where entry.Key.GridX > 0
				select (Key: entry.Key, Box: entry.Value)).ToArray();
			Assert.NotEmpty(positiveGridXTiles);
			Assert.True(positiveGridXTiles.Any((((int GridX, int GridY) Key, BonesViewerScriptTestDriver.BonesViewerDomBoundingRect Box) tile) => BonesViewerScriptTestDriver.IsStrictlyRightOf(openingBox, tile.Box)), $"At least one positive gridX tile must be strictly right of opening bbox inside #board-chain (maxGridX={registered.Layout.MaxGridX}).");
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add greedy engine-replayed DOM test: when frame `maxGridX > 0`, at least one positive `gridX` tile bbox is strictly right of opening bbox inside `#board-chain` [foundation for right-arm visibility] [mandatory - right arm spatial visibility]")]
	public async Task BonesViewerPage_GivenGreedyMatchWithRightPlays_ExpectedAllPositiveGridXTilesIntersectBoardChainSlot()
	{
		RegisteredDominoRenderingMatch registered = RegisterGreedyRightArmMatch("session-greedy-right-arm-slot", "match-greedy-right-arm-slot");
		Assert.True(registered.Layout.MaxGridX > 0, "Greedy match must include right-arm plays (maxGridX > 0).");
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-greedy-right-arm-slot", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			IReadOnlyDictionary<(int GridX, int GridY), BonesViewerScriptTestDriver.BonesViewerDomBoundingRect> tileBoxes = await BonesViewerScriptTestDriver.GetBoardTileBoundingBoxesByGridAsync(page);
			BonesViewerScriptTestDriver.BonesViewerDomBoundingRect slotRect = await BonesViewerScriptTestDriver.GetBoardChainSlotRectAsync(page);
			KeyValuePair<(int GridX, int GridY), BonesViewerScriptTestDriver.BonesViewerDomBoundingRect>[] positiveGridXTiles = tileBoxes.Where<KeyValuePair<(int, int), BonesViewerScriptTestDriver.BonesViewerDomBoundingRect>>((KeyValuePair<(int GridX, int GridY), BonesViewerScriptTestDriver.BonesViewerDomBoundingRect> entry) => entry.Key.GridX > 0).ToArray();
			Assert.NotEmpty(positiveGridXTiles);
			foreach (var (gridKey, tileBox) in positiveGridXTiles)
			{
				Assert.True(BonesViewerScriptTestDriver.BoundingRectsIntersect(tileBox, slotRect), $"Positive gridX tile ({gridKey.GridX}, {gridKey.GridY}) must intersect #board-chain client rect after transform scale.");
			}
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.BidirectionalSpatialProof)]
	public async Task BonesViewerPage_GivenGreedyBothArms_ExpectedNegativeAndPositiveGridXTilesVisible()
	{
		RegisteredDominoRenderingMatch registered = RegisterGreedyBothArmsMatch("session-greedy-both-arms-dom", "match-greedy-both-arms-dom");
		Assert.True(registered.Layout.MinGridX < 0, "Greedy match must include left-arm plays (minGridX < 0).");
		Assert.True(registered.Layout.MaxGridX > 0, "Greedy match must include right-arm plays (maxGridX > 0).");
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-greedy-both-arms-dom", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState> boardTiles = await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState opening = boardTiles.Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX == 0 && tile.GridY == 0);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState leftNeighbor = (from tile in boardTiles
				where tile.GridX < 0
				orderby tile.GridX descending
				select tile).First();
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState rightNeighbor = (from tile in boardTiles
				where tile.GridX > 0
				orderby tile.GridX
				select tile).First();
			Assert.True(leftNeighbor.GridX < 0);
			Assert.True(rightNeighbor.GridX > 0);
			Assert.Equal(opening.LowHalf.Pip, leftNeighbor.HighHalf.Pip);
			Assert.Equal(opening.HighHalf.Pip, rightNeighbor.LowHalf.Pip);
			IReadOnlyDictionary<(int GridX, int GridY), BonesViewerScriptTestDriver.BonesViewerDomBoundingRect> tileBoxes = await BonesViewerScriptTestDriver.GetBoardTileBoundingBoxesByGridAsync(page);
			(int GridX, int GridY) openingKey = tileBoxes.Keys.Single(((int GridX, int GridY) key) => key.GridX == 0 && key.GridY == 0);
			BonesViewerScriptTestDriver.BonesViewerDomBoundingRect openingBox = tileBoxes[openingKey];
			BonesViewerScriptTestDriver.BonesViewerDomBoundingRect slotRect = await BonesViewerScriptTestDriver.GetBoardChainSlotRectAsync(page);
			((int GridX, int GridY) Key, BonesViewerScriptTestDriver.BonesViewerDomBoundingRect Box)[] negativeGridXTiles = (from entry in tileBoxes
				where entry.Key.GridX < 0
				select (Key: entry.Key, Box: entry.Value)).ToArray();
			((int GridX, int GridY) Key, BonesViewerScriptTestDriver.BonesViewerDomBoundingRect Box)[] positiveGridXTiles = (from entry in tileBoxes
				where entry.Key.GridX > 0
				select (Key: entry.Key, Box: entry.Value)).ToArray();
			Assert.NotEmpty(negativeGridXTiles);
			Assert.NotEmpty(positiveGridXTiles);
			var leftmostNegative = negativeGridXTiles.OrderBy(static tile => tile.Key.GridX).First();
			var rightmostPositive = positiveGridXTiles.OrderByDescending(static tile => tile.Key.GridX).First();
			Assert.True(BonesViewerScriptTestDriver.IsStrictlyLeftOf(openingBox, leftmostNegative.Box), $"Left-arm tile ({leftmostNegative.Key.GridX}, {leftmostNegative.Key.GridY}) must be strictly left of opening bbox (minGridX={registered.Layout.MinGridX}).");
			Assert.True(BonesViewerScriptTestDriver.IsStrictlyRightOf(leftmostNegative.Box, rightmostPositive.Box), $"Right-arm tile ({rightmostPositive.Key.GridX}, {rightmostPositive.Key.GridY}) must be strictly right of left-arm bbox for bidirectional spread (maxGridX={registered.Layout.MaxGridX}).");
			if (registered.Layout.MaxGridX >= 2)
			{
				Assert.True(BonesViewerScriptTestDriver.IsStrictlyRightOf(openingBox, rightmostPositive.Box), $"Right-arm tile at max gridX must be strictly right of opening bbox when maxGridX={registered.Layout.MaxGridX}.");
			}			foreach (var (gridKey, tileBox) in negativeGridXTiles.Concat(positiveGridXTiles))
			{
				Assert.True(BonesViewerScriptTestDriver.BoundingRectsIntersect(tileBox, slotRect), $"Bidirectional arm tile ({gridKey.GridX}, {gridKey.GridY}) must intersect #board-chain client rect after transform scale.");
			}
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Prove right-arm plays produce positive `gridX` with matching junction pips [depends on geometry] [mandatory - bidirectional chain]")]
	public async Task BonesViewerPage_GivenRightArmReplay_ExpectedPositiveGridXAndMatchingJunctionPips()
	{
		RegisteredDominoRenderingMatch registered = RegisterScriptedMatch("session-right-arm-dom", "match-right-arm-dom", new ScriptedPlayStep[]
		{
			ScriptedPlayStep.Opening,
			ScriptedPlayStep.AnyOn(BonesBoardSide.Right)
		}, (BonesBoardVisualLayout layout) => layout.MaxGridX > 0);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-right-arm-dom", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState> boardTiles = await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState opening = boardTiles.Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX == 0 && tile.GridY == 0);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState rightNeighbor = (from tile in boardTiles
				where tile.GridX > 0
				orderby tile.GridX
				select tile).First();
			Assert.True(rightNeighbor.GridX > 0);
			Assert.Equal(opening.HighHalf.Pip, rightNeighbor.LowHalf.Pip);
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.OpeningDoubleSixExhibition)]
	[Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.OpeningDoubleSixExhibitionDivider)]
	public async Task BonesViewerPage_GivenSixSixOpening_ExpectedTwelveVisiblePipsInsideOpeningBounds()
	{
		RegisteredDominoRenderingMatch registered = RegisterGreedySixSixOpeningMatch("session-six-six-opening-dom", "match-six-six-opening-dom");
		BonesBoardTilePlacement openingPlacement = registered.Layout.Tiles.Single((BonesBoardTilePlacement tile) => tile.GridX == 0 && tile.GridY == 0);
		Assert.True(openingPlacement.IsDouble);
		Assert.Equal(BonesPipAxis.Vertical, openingPlacement.PipAxis);
		Assert.Equal(6, openingPlacement.FacingLowPip);
		Assert.Equal(6, openingPlacement.FacingHighPip);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-six-six-opening-dom", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState opening = (await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page)).Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX == 0 && tile.GridY == 0);
			Assert.True(opening.IsDouble);
			Assert.Equal("vertical", opening.Orientation);
			Assert.Equal("vertical", opening.PipAxis);
			Assert.Equal(6, opening.LowHalf.Pip);
			Assert.Equal(6, opening.HighHalf.Pip);
			Assert.Equal(6, opening.LowHalf.Positions.Count);
			Assert.Equal(6, opening.HighHalf.Positions.Count);
			AssertBoardHalfStandardLayout(opening.LowHalf);
			AssertBoardHalfStandardLayout(opening.HighHalf);
			Assert.True(await BonesViewerScriptTestDriver.BoardTilePipsContainedInTileBoundsAsync(page, 0, 0), "All pips on the opening 6-6 exhibition tile must render inside the tile bounds.");
			Assert.True(await BonesViewerScriptTestDriver.OpeningDoubleDividerIsHorizontalAsync(page, 0, 0), "Opening 6-6 double must use a horizontal seat divider between stacked halves per bones standard.");
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.EndChainDoubleSixRender)]
	public async Task BonesViewerPage_GivenVerticalDoubleSixAtMaxGridX_ExpectedAllPipsVisibleInsideTileBounds()
	{
		RegisteredDominoRenderingMatch registered = RegisterGreedyDoubleSixAtMaxGridXMatch("session-double-six-max-gridx-dom", "match-double-six-max-gridx-dom");
		registered.Layout.Tiles.Single((BonesBoardTilePlacement tile) => tile.IsDouble && tile.FacingLowPip == 6 && tile.FacingHighPip == 6 && tile.GridX == registered.Layout.MaxGridX);
		Assert.True(registered.Layout.MaxGridX > 0, "Greedy match must place the 6-6 double at a positive max gridX.");
		IPage page = await BonesViewerScriptTestDriver.OpenBoardLayoutRenderedPageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, registered.Layout);
		try
		{
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState endChainDouble = (await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page)).Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.IsDouble && tile.LowHalf.Pip == 6 && tile.HighHalf.Pip == 6 && tile.GridX == registered.Layout.MaxGridX);
			Assert.Equal("vertical", endChainDouble.PipAxis);
			Assert.Equal(6, endChainDouble.LowHalf.Positions.Count);
			Assert.Equal(6, endChainDouble.HighHalf.Positions.Count);
			AssertBoardHalfStandardLayout(endChainDouble.LowHalf);
			AssertBoardHalfStandardLayout(endChainDouble.HighHalf);
			Assert.True(await BonesViewerScriptTestDriver.BoardTilePipsContainedInTileBoundsAsync(page, endChainDouble.GridX, endChainDouble.GridY), "All pips on the end-chain 6-6 double at max gridX must render inside the tile bounds.");
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Fix vertical double-six (and other doubles) clipping: container overflow, fine-row extent, pip half height at chain edge [depends on geometry extent] [mandatory - double-six render]")]
	public async Task BonesViewerPage_GivenVerticalDoubleSixAtChainEnd_ExpectedAllPipsVisibleInsideTileBounds()
	{
		RegisteredDominoRenderingMatch registered = RegisterGreedyDoubleSixBranchMatch("session-double-six-dom", "match-double-six-dom");
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-double-six-dom", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState chainDouble = (await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page)).First((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.IsDouble && tile.PipAxis == "vertical" && tile.LowHalf.Pip > 0);
			Assert.Equal("vertical", chainDouble.Orientation);
			Assert.Equal("vertical", chainDouble.PipAxis);
			Assert.Equal(0, chainDouble.GridY);
			Assert.Equal(BonesStandardDominoPipLayout.GetGridPositions(chainDouble.LowHalf.Pip), chainDouble.LowHalf.Positions);
			Assert.Equal(BonesStandardDominoPipLayout.GetGridPositions(chainDouble.HighHalf.Pip), chainDouble.HighHalf.Positions);
			Assert.True(chainDouble.LowHalf.Positions.Count > 0, "Main-line double must render at least one visible pip half.");
			Assert.True(await BonesViewerScriptTestDriver.BoardTilePipsContainedInTileBoundsAsync(page, chainDouble.GridX, chainDouble.GridY), "All pips on the vertical main-line double must render inside the tile bounds.");
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", BonesViewerDominoRenderingRequirementsChecklistItems.HandBoardLayoutParity)]
	public async Task BonesViewerPage_GivenHandAndBoardTiles_ExpectedSamePipGridMarkupPattern()
	{
		BonesGameId matchId = new BonesGameId("match-hand-board-markup");
		RegisterSeededMatch("session-hand-board-markup", matchId, 9001);
		BonesMatchViewerService viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
		Assert.True(_playwrightHost.Catalog.TryGet(new SessionId("session-hand-board-markup"), matchId, out BonesRegisteredMatch registered));
		BonesMatchViewModel snapshot = viewer.BuildMatchSnapshot(new SessionId("session-hand-board-markup"), registered);
		int targetTurn = Math.Max(0, snapshot.FrameCount / 2);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-hand-board-markup", matchId.Value);
		try
		{
			await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
			Assert.True(await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn), "Scrubber must load the seeded mid-turn frame before hand/board layout parity assertions.");
			await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
			BonesViewerScriptTestDriver.BonesViewerDominoTileUnitCssState cssUnits = await BonesViewerScriptTestDriver.GetDominoTileUnitCssVariablesAsync(page);
			Assert.Equal(cssUnits.BoardTileUnit, cssUnits.HandTileUnit);
			Assert.Equal(cssUnits.DominoTileUnit, cssUnits.HandTileUnit);
			BonesViewerScriptTestDriver.BonesViewerHandBoardPipMarkupParityState parity = await BonesViewerScriptTestDriver.GetHandBoardPipMarkupParityAsync(page);
			Assert.True(parity.HandTileCount > 0, "Seeded match must render at least one hand tile.");
			Assert.True(parity.BoardTileCount > 0, "Seeded match must render at least one board tile.");
			foreach (BonesViewerScriptTestDriver.BonesViewerHandHalfContainmentState half in parity.HandHalves)
			{
				Assert.True(half.HalfContainedInHandPanel, $"Hand half for seat {half.Seat} tile {half.TileIndex} must not clip outside the seat panel.");
			}
			Assert.Contains("pip", parity.HandPipSelectorPattern, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("pip", parity.BoardPipSelectorPattern, StringComparison.OrdinalIgnoreCase);
			Assert.Equal(parity.HandPipSelectorPattern, parity.BoardPipSelectorPattern);
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add DOM test: hand `.domino-tile` width/height at or above readable floor inside `.hand-tiles` [depends on hand CSS] [mandatory - hand readability]")]
	public async Task BonesViewerPage_GivenFourSeatHands_ExpectedHandTileDimensionsAtOrAboveReadableFloor()
	{
		BonesGameId matchId = new BonesGameId("match-hand-readable-floor");
		RegisterSeededMatch("session-hand-readable-floor", matchId, 9001);
		BonesMatchViewerService viewer = _playwrightHost.Services.GetRequiredService<BonesMatchViewerService>();
		Assert.True(_playwrightHost.Catalog.TryGet(new SessionId("session-hand-readable-floor"), matchId, out BonesRegisteredMatch registered));
		BonesMatchViewModel snapshot = viewer.BuildMatchSnapshot(new SessionId("session-hand-readable-floor"), registered);
		int targetTurn = Math.Max(0, snapshot.FrameCount / 2);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-hand-readable-floor", matchId.Value);
		try
		{
			await BonesViewerScriptTestDriver.WaitForBootstrapFetchAsync(page);
			Assert.True(await BonesViewerScriptTestDriver.ScrubToTurnAndAwaitFrameAsync(page, targetTurn), "Scrubber must load the seeded mid-turn frame before hand readability assertions.");
			await BonesViewerScriptTestDriver.WaitForFrameAtTurnAsync(page, targetTurn);
			IReadOnlyDictionary<int, IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerHandTileDimensionState>> dimensions = await BonesViewerScriptTestDriver.GetHandTileDimensionsBySeatAsync(page);
			for (int seat = 1; seat <= 4; seat++)
			{
				Assert.True(dimensions.TryGetValue(seat, out IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerHandTileDimensionState> seatDimensions), $"Seat {seat} must expose hand tile dimensions.");
				Assert.True(seatDimensions.Count > 0, $"Seat {seat} must render at least one hand tile for readability proof.");
				foreach (BonesViewerScriptTestDriver.BonesViewerHandTileDimensionState tile in seatDimensions)
				{
					Assert.True(tile.Width >= 27.5 || tile.Height >= 27.5, $"Seat {seat} hand tile must meet readable floor ({28.0}px).");
				}
				seatDimensions = null;
			}
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Add engine-replayed match DOM test: left play before right play shows tile at `data-grid-x` less than opening [depends on bidirectional layout] [mandatory - left arm proof]")]
	public async Task BonesViewerPage_GivenLeftPlayReplay_ExpectedNegativeGridXTilePresent()
	{
		RegisteredDominoRenderingMatch registered = RegisterScriptedMatch("session-left-before-right", "match-left-before-right", new ScriptedPlayStep[]
		{
			ScriptedPlayStep.Opening,
			ScriptedPlayStep.AnyOn(BonesBoardSide.Left)
		}, (BonesBoardVisualLayout layout) => layout.MinGridX < 0);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-left-before-right", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState[] leftTiles = (await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page)).Where((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX < 0).ToArray();
			Assert.NotEmpty(leftTiles);
			Assert.All(leftTiles, delegate(BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile)
			{
				Assert.True(tile.GridX < 0);
			});
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	[Fact]
	[Trait("ChecklistItem", "Prove left-arm plays produce negative `gridX` placements flush to opening with spatial connecting pip matching engine left end [depends on facing + geometry] [mandatory - bidirectional chain]")]
	public async Task BonesViewerPage_GivenAdjacentBoardTiles_ExpectedSharedPipAtJunction()
	{
		RegisteredDominoRenderingMatch registered = RegisterScriptedMatch("session-junction-pip-dom", "match-junction-pip-dom", new ScriptedPlayStep[]
		{
			ScriptedPlayStep.Opening,
			ScriptedPlayStep.AnyOn(BonesBoardSide.Right)
		}, (BonesBoardVisualLayout layout) => layout.MaxGridX > 0);
		IPage page = await BonesViewerScriptTestDriver.OpenViewRoutePageAsync(await BonesViewerScriptTestDriver.GetSharedBrowserAsync(), _playwrightHost.ListeningUri, "session-junction-pip-dom", registered.MatchId.Value, stripSessionMatchDataAttributes: false, registered.FinalTurnIndex);
		try
		{
			IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState> boardTiles = await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page);
			BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState opening = boardTiles.Single((BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState tile) => tile.GridX == 0 && tile.GridY == 0);
			Assert.Equal(actual: (from tile in boardTiles
				where tile.GridX > 0
				orderby tile.GridX
				select tile).First().LowHalf.Pip, expected: opening.HighHalf.Pip);
		}
		finally
		{
			await page.CloseAsync();
		}
	}


	[Fact]
	public async Task BonesViewerPage_GivenSandwichedDoubleBranch_ExpectedJunctionPipsOnBothHalvesAndNeighbors()
	{
		BonesBoardVisualLayout layout = BuildSandwichedDoubleBranchVisualLayout();
		var chainDouble = layout.Tiles.Single(tile => tile.IsDouble);
		var junctionPip = chainDouble.FacingLowPip;
		var leftMain = layout.Tiles.Single(tile => tile.GridX < chainDouble.GridX && tile.GridY == 0);
		var rightMain = layout.Tiles.Single(tile => tile.GridX > chainDouble.GridX && tile.GridY == 0);

		IPage page = await BonesViewerScriptTestDriver.OpenBoardLayoutRenderedPageAsync(
			await BonesViewerScriptTestDriver.GetSharedBrowserAsync(),
			_playwrightHost.ListeningUri,
			layout);
		try
		{
			IReadOnlyList<BonesViewerScriptTestDriver.BonesViewerBoardTileDomLayoutState> boardTiles =
				await BonesViewerScriptTestDriver.GetBoardTileDomLayoutAsync(page);
			var domDouble = boardTiles.Single(tile => tile.IsDouble);
			Assert.Equal(junctionPip, domDouble.LowHalf.Pip);
			Assert.Equal(junctionPip, domDouble.HighHalf.Pip);
			var domLeft = boardTiles.Single(tile => tile.GridX == leftMain.GridX && tile.GridY == 0);
			var domRight = boardTiles.Single(tile => tile.GridX == rightMain.GridX && tile.GridY == 0);
			Assert.Contains(junctionPip, new[] { domLeft.LowHalf.Pip, domLeft.HighHalf.Pip });
			Assert.Contains(junctionPip, new[] { domRight.LowHalf.Pip, domRight.HighHalf.Pip });
		}
		finally
		{
			await page.CloseAsync();
		}
	}
	private static void AssertBoardHalfStandardLayout(BonesViewerScriptTestDriver.BonesViewerDominoHalfDomLayoutState half)
	{
		Assert.Equal(BonesStandardDominoPipLayout.GetGridPositions(half.Pip), half.Positions);
	}

	private RegisteredDominoRenderingMatch RegisterGreedyRightArmMatch(string sessionId, string matchIdValue)
	{
		BonesMatchViewerService bonesMatchViewerService = new BonesMatchViewerService(new BonesGameEngine());
		BonesGameId matchId = new BonesGameId(matchIdValue);
		for (int i = 1; i <= 5000; i++)
		{
			BonesGameId matchId2 = new BonesGameId($"domino-right-arm-probe-{i}");
			BonesMatchResult bonesMatchResult = CreateGreedyRightArmSeededMatch(matchId2, i, 15);
			if (bonesMatchResult.Transcript.Length == 0)
			{
				continue;
			}
			BonesRegisteredMatch bonesRegisteredMatch = new BonesRegisteredMatch(new SessionId(sessionId), matchId, i, bonesMatchResult);
			IReadOnlyList<BonesMatchFrame> readOnlyList = bonesMatchViewerService.BuildMatchTimeline(bonesRegisteredMatch);
			for (int num = readOnlyList.Count - 1; num >= 0; num--)
			{
				BonesBoardVisualLayout boardLayout = readOnlyList[num].BoardLayout;
				if (boardLayout.Tiles.Count >= 2 && boardLayout.MaxGridX > 0)
				{
					_playwrightHost.Catalog.Register(bonesRegisteredMatch);
					return new RegisteredDominoRenderingMatch(matchId, num, boardLayout);
				}
			}
		}
		throw new InvalidOperationException("Unable to locate a greedy engine-replayed match with right-arm plays (maxGridX > 0).");
	}

	private RegisteredDominoRenderingMatch RegisterGreedyBothArmsMatch(string sessionId, string matchIdValue)
	{
		var viewer = new BonesMatchViewerService(new BonesGameEngine());
		var matchId = new BonesGameId(matchIdValue);
		Func<BonesGameId, int, int, BonesMatchResult>[] factories =
		[
			CreateGreedyBothArmsSeededMatch,
			CreateGreedyRightArmSeededMatch,
			CreateSeededMatch,
		];

		var bestSeed = 0;
		BonesMatchResult? bestMatchResult = null;
		var bestTurnIndex = 0;
		BonesBoardVisualLayout? bestLayout = null;
		var bestMaxGridX = int.MinValue;

		foreach (var factory in factories)
		{
			for (var seed = 1; seed <= 5000; seed++)
			{
				var probeMatchId = new BonesGameId($"domino-both-arms-probe-{seed}");
				var matchResult = factory(probeMatchId, seed, 15);
				if (matchResult.Transcript.Length == 0)
					continue;

				var registered = new BonesRegisteredMatch(new SessionId(sessionId), matchId, seed, matchResult);
				var frames = viewer.BuildMatchTimeline(registered);
				for (var turnIndex = 0; turnIndex < frames.Count; turnIndex++)
				{
					var boardLayout = frames[turnIndex].BoardLayout;
					if (boardLayout.Tiles.Count >= 3
						&& boardLayout.MinGridX < 0
						&& boardLayout.MaxGridX > 0
						&& boardLayout.MaxGridX > bestMaxGridX)
					{
						bestMaxGridX = boardLayout.MaxGridX;
						bestSeed = seed;
						bestMatchResult = matchResult;
						bestTurnIndex = turnIndex;
						bestLayout = boardLayout;
					}
				}
			}
		}

		if (bestLayout is not null && bestMatchResult is not null)
		{
			_playwrightHost.Catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, bestSeed, bestMatchResult));
			return new RegisteredDominoRenderingMatch(matchId, bestTurnIndex, bestLayout);
		}

		ScriptedPlayStep[][] scriptedSequences =
		[
			[
				ScriptedPlayStep.Opening,
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Left),
			],
			[
				ScriptedPlayStep.Opening,
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Left),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
			],
			[
				ScriptedPlayStep.Opening,
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Left),
			],
		];

		foreach (var steps in scriptedSequences)
		{
			try
			{
				return RegisterScriptedMatch(
					sessionId,
					matchIdValue,
					steps,
					layout => layout.MinGridX < 0 && layout.MaxGridX >= 2);
			}
			catch (InvalidOperationException)
			{
			}
		}

		return RegisterScriptedMatch(
			sessionId,
			matchIdValue,
			new ScriptedPlayStep[]
			{
				ScriptedPlayStep.Opening,
				ScriptedPlayStep.AnyOn(BonesBoardSide.Right),
				ScriptedPlayStep.AnyOn(BonesBoardSide.Left),
			},
			layout => layout.MinGridX < 0 && layout.MaxGridX > 0);
	}
	private RegisteredDominoRenderingMatch RegisterGreedySixSixOpeningMatch(string sessionId, string matchIdValue)
	{
		BonesMatchViewerService bonesMatchViewerService = new BonesMatchViewerService(new BonesGameEngine());
		BonesGameId matchId = new BonesGameId(matchIdValue);
		for (int i = 1; i <= 5000; i++)
		{
			BonesGameId matchId2 = new BonesGameId($"domino-six-six-opening-probe-{i}");
			BonesMatchResult bonesMatchResult = CreateSeededMatch(matchId2, i, 15);
			if (bonesMatchResult.Transcript.Length == 0)
			{
				continue;
			}
			BonesRegisteredMatch bonesRegisteredMatch = new BonesRegisteredMatch(new SessionId(sessionId), matchId, i, bonesMatchResult);
			IReadOnlyList<BonesMatchFrame> readOnlyList = bonesMatchViewerService.BuildMatchTimeline(bonesRegisteredMatch);
			for (int j = 0; j < readOnlyList.Count; j++)
			{
				BonesBoardVisualLayout boardLayout = readOnlyList[j].BoardLayout;
				BonesBoardTilePlacement bonesBoardTilePlacement = boardLayout.Tiles.FirstOrDefault((BonesBoardTilePlacement tile) => tile.GridX == 0 && tile.GridY == 0);
				if ((object)bonesBoardTilePlacement != null && bonesBoardTilePlacement.IsDouble && bonesBoardTilePlacement.FacingLowPip == 6 && bonesBoardTilePlacement.FacingHighPip == 6)
				{
					_playwrightHost.Catalog.Register(bonesRegisteredMatch);
					return new RegisteredDominoRenderingMatch(matchId, j, boardLayout);
				}
			}
		}
		throw new InvalidOperationException("Unable to locate a greedy engine-replayed match with 6-6 opening at gridX==0.");
	}

	private RegisteredDominoRenderingMatch RegisterGreedyDoubleSixBranchMatch(string sessionId, string matchIdValue)
	{
		BonesMatchViewerService bonesMatchViewerService = new BonesMatchViewerService(new BonesGameEngine());
		BonesGameId matchId = new BonesGameId(matchIdValue);
		for (int i = 1; i <= 5000; i++)
		{
			BonesGameId matchId2 = new BonesGameId($"domino-double-six-probe-{i}");
			BonesMatchResult bonesMatchResult = CreateSeededMatch(matchId2, i, 15);
			if (bonesMatchResult.Transcript.Length == 0)
			{
				continue;
			}
			BonesRegisteredMatch bonesRegisteredMatch = new BonesRegisteredMatch(new SessionId(sessionId), matchId, i, bonesMatchResult);
			IReadOnlyList<BonesMatchFrame> readOnlyList = bonesMatchViewerService.BuildMatchTimeline(bonesRegisteredMatch);
			for (int num = readOnlyList.Count - 1; num >= 0; num--)
			{
				BonesBoardVisualLayout boardLayout = readOnlyList[num].BoardLayout;
				if (boardLayout.Tiles.Any((BonesBoardTilePlacement tile) => tile.IsDouble && tile.PipAxis == BonesPipAxis.Vertical && tile.FacingLowPip > 0))
				{
					_playwrightHost.Catalog.Register(bonesRegisteredMatch);
					return new RegisteredDominoRenderingMatch(matchId, num, boardLayout);
				}
			}
		}
		throw new InvalidOperationException("Unable to locate a seeded round that includes a vertical branch double tile.");
	}

	private RegisteredDominoRenderingMatch RegisterGreedyDoubleSixAtMaxGridXMatch(string sessionId, string matchIdValue)
	{
		BonesMatchViewerService bonesMatchViewerService = new BonesMatchViewerService(new BonesGameEngine());
		BonesGameId matchId = new BonesGameId(matchIdValue);
		Func<BonesGameId, int, int, BonesMatchResult>[] array = new Func<BonesGameId, int, int, BonesMatchResult>[3] { CreateGreedyDoubleSixAtRightEndSeededMatch, CreateGreedyRightArmSeededMatch, CreateSeededMatch };
		Func<BonesGameId, int, int, BonesMatchResult>[] array2 = array;
		foreach (Func<BonesGameId, int, int, BonesMatchResult> func in array2)
		{
			for (int j = 1; j <= 5000; j++)
			{
				BonesGameId arg = new BonesGameId($"domino-double-six-max-gridx-probe-{j}");
				BonesMatchResult bonesMatchResult = func(arg, j, 15);
				if (bonesMatchResult.Transcript.Length == 0)
				{
					continue;
				}
				BonesRegisteredMatch bonesRegisteredMatch = new BonesRegisteredMatch(new SessionId(sessionId), matchId, j, bonesMatchResult);
				IReadOnlyList<BonesMatchFrame> readOnlyList = bonesMatchViewerService.BuildMatchTimeline(bonesRegisteredMatch);
				for (int num = readOnlyList.Count - 1; num >= 0; num--)
				{
					BonesBoardVisualLayout boardLayout = readOnlyList[num].BoardLayout;
					if (TryGetVerticalDoubleSixAtMaxGridX(boardLayout, out BonesBoardTilePlacement _))
					{
						_playwrightHost.Catalog.Register(bonesRegisteredMatch);
						return new RegisteredDominoRenderingMatch(matchId, num, boardLayout);
					}
				}
			}
		}
		return RegisterConstructedEndChainDoubleSixLayoutMatch(sessionId, matchIdValue);
	}

	private RegisteredDominoRenderingMatch RegisterConstructedEndChainDoubleSixLayoutMatch(string sessionId, string matchIdValue)
	{
		BonesBoardVisualLayout layout = BuildEndChainDoubleSixVisualLayout();
		if (!TryGetVerticalDoubleSixAtMaxGridX(layout, out _))
		{
			throw new InvalidOperationException("Constructed end-chain layout must include vertical 6-6 at max gridX.");
		}

		BonesGameId matchId = new BonesGameId(matchIdValue);
		return new RegisteredDominoRenderingMatch(matchId, layout.Tiles.Count - 1, layout);
	}

	private static BonesBoardVisualLayout BuildBoardTileOrientationsVisualLayout()
	{
		var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(4));
		var horizontal = new BonesTile(new BonesPipCount(4), new BonesPipCount(6));
		var doubleTile = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
		var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, horizontal, doubleTile]);
		var events = new[]
		{
			new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
			new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, horizontal, BonesBoardSide.Right),
			new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
		};

		return new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
	}

	private static BonesBoardVisualLayout BuildSandwichedDoubleBranchVisualLayout()
	{
		var opening = new BonesTile(new BonesPipCount(2), new BonesPipCount(3));
		var doubleTile = new BonesTile(new BonesPipCount(3), new BonesPipCount(3));
		var extension = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
		var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, doubleTile, extension]);
		var events = new[]
		{
			new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
			new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, doubleTile, BonesBoardSide.Right),
			new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, extension, BonesBoardSide.Right),
		};

		return new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
	}
	private static BonesBoardVisualLayout BuildEndChainDoubleSixVisualLayout()
	{
		var opening = new BonesTile(new BonesPipCount(1), new BonesPipCount(3));
		var extension = new BonesTile(new BonesPipCount(3), new BonesPipCount(5));
		var connector = new BonesTile(new BonesPipCount(5), new BonesPipCount(6));
		var doubleSix = new BonesTile(new BonesPipCount(6), new BonesPipCount(6));
		var board = BonesBoardChainBuilder.FromLegacyCanonicalChain([opening, extension, connector, doubleSix]);
		var events = new[]
		{
			new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, opening, null),
			new BonesEvent(1, new BonesPlayerId(2), BonesEventKind.Play, extension, BonesBoardSide.Right),
			new BonesEvent(2, new BonesPlayerId(3), BonesEventKind.Play, connector, BonesBoardSide.Right),
			new BonesEvent(3, new BonesPlayerId(4), BonesEventKind.Play, doubleSix, BonesBoardSide.Right),
		};

		return new BonesBoardVisualLayoutBuilder().BuildLayout(board, events);
	}
	private static bool TryGetVerticalDoubleSixAtMaxGridX(BonesBoardVisualLayout layout, out BonesBoardTilePlacement endDouble)
	{
		endDouble = default!;
		if (layout.MaxGridX <= 0)
		{
			return false;
		}
		endDouble = layout.Tiles.FirstOrDefault((BonesBoardTilePlacement tile) => tile.IsDouble && tile.FacingLowPip == 6 && tile.FacingHighPip == 6 && tile.GridX == layout.MaxGridX && tile.PipAxis == BonesPipAxis.Vertical);
		return (object)endDouble != null;
	}

	private RegisteredDominoRenderingMatch RegisterScriptedMatch(string sessionId, string matchIdValue, IReadOnlyList<ScriptedPlayStep> steps, Func<BonesBoardVisualLayout, bool> validateLayout)
	{
		(int Seed, BonesMatchResult MatchResult, int TurnIndex, BonesBoardVisualLayout Layout) tuple = FindScriptedRoundMatch(steps, validateLayout);
		int item = tuple.Seed;
		BonesMatchResult item2 = tuple.MatchResult;
		int item3 = tuple.TurnIndex;
		BonesBoardVisualLayout item4 = tuple.Layout;
		BonesGameId matchId = new BonesGameId(matchIdValue);
		_playwrightHost.Catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, item, item2));
		return new RegisteredDominoRenderingMatch(matchId, item3, item4);
	}

	private static (int Seed, BonesMatchResult MatchResult, int TurnIndex, BonesBoardVisualLayout Layout) FindScriptedRoundMatch(IReadOnlyList<ScriptedPlayStep> steps, Func<BonesBoardVisualLayout, bool> validateLayout)
	{
		BonesGameEngine bonesGameEngine = new BonesGameEngine();
		BonesBoardVisualLayoutBuilder bonesBoardVisualLayoutBuilder = new BonesBoardVisualLayoutBuilder();
		for (int i = 1; i <= 5000; i++)
		{
			BonesRoundConfig config = new BonesRoundConfig(new BonesGameId($"domino-script-probe-{i}"), HashCode.Combine(i, 1));
			BonesRoundState bonesRoundState = bonesGameEngine.StartRound(config);
			List<BonesEvent> list = new List<BonesEvent>(steps.Count);
			bool flag = true;
			for (int j = 0; j < steps.Count; j++)
			{
				ScriptedPlayStep step = steps[j];
				BonesPlayerId currentPlayer = bonesRoundState.CurrentPlayer;
				IReadOnlyList<BonesMove> legalMoves = bonesGameEngine.GetLegalMoves(bonesRoundState, currentPlayer);
				ScriptedStepKind kind = step.Kind;
				if (1 == 0)
				{
				}
				BonesMove bonesMove = kind switch
				{
					ScriptedStepKind.Opening => legalMoves.FirstOrDefault((BonesMove candidate) => !candidate.IsPass), 
					ScriptedStepKind.AnyOnSide => legalMoves.FirstOrDefault((BonesMove candidate) => !candidate.IsPass && candidate.Side == step.Side), 
					ScriptedStepKind.Tile => legalMoves.FirstOrDefault((BonesMove candidate) => !candidate.IsPass && candidate.Side == step.Side && TileMatches(candidate.Tile.Value, step.LowPip.Value, step.HighPip.Value)), 
					_ => null, 
				};
				if (1 == 0)
				{
				}
				BonesMove bonesMove2 = bonesMove;
				if ((object)bonesMove2 == null)
				{
					flag = false;
					break;
				}
				bonesRoundState = bonesGameEngine.ApplyMove(bonesRoundState, bonesMove2);
				list.Add(new BonesEvent(j, currentPlayer, BonesEventKind.Play, bonesMove2.Tile, bonesMove2.Side));
			}
			if (!flag)
			{
				continue;
			}
			BonesBoardVisualLayout bonesBoardVisualLayout = bonesBoardVisualLayoutBuilder.BuildLayout(bonesRoundState.Board, list);
			if (validateLayout(bonesBoardVisualLayout))
			{
				BonesGameId gameId = new BonesGameId($"domino-script-{i}");
				BonesRoundScore score = new BonesRoundScore(new BonesPlayerId(1), 0, BonesRoundOutcomeKind.Blocked);
				BonesMatchRoundRecord item = new BonesMatchRoundRecord(1, score, list);
				ImmutableDictionary<BonesPlayerId, int>.Builder builder = ImmutableDictionary.CreateBuilder<BonesPlayerId, int>();
				for (int num = 1; num <= 4; num++)
				{
					builder[new BonesPlayerId(num)] = 0;
				}
				BonesMatchResult item2 = new BonesMatchResult(gameId, new BonesPlayerId(1), builder.ToImmutable(), new BonesMatchRoundRecord[] { item }, list);
				return (Seed: i, MatchResult: item2, TurnIndex: list.Count - 1, Layout: bonesBoardVisualLayout);
			}
		}
		throw new InvalidOperationException("Unable to locate a seeded round that satisfies the scripted play sequence.");
	}

	private static bool TileMatches(BonesTile tile, int lowPip, int highPip)
	{
		return (tile.LowPip.Value == lowPip && tile.HighPip.Value == highPip) || (tile.LowPip.Value == highPip && tile.HighPip.Value == lowPip);
	}

	private RegisteredDominoRenderingMatch RegisterMatchForLayout(string sessionId, string matchIdValue, Func<BonesBoardVisualLayout, bool> layoutPredicate, int minimumTileCount)
	{
		(int Seed, BonesMatchResult MatchResult, BonesBoardVisualLayout Layout, int TurnIndex) tuple = FindSeededMatchLayout(layoutPredicate, minimumTileCount);
		int item = tuple.Seed;
		BonesMatchResult item2 = tuple.MatchResult;
		BonesBoardVisualLayout item3 = tuple.Layout;
		int item4 = tuple.TurnIndex;
		BonesGameId matchId = new BonesGameId(matchIdValue);
		_playwrightHost.Catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, item, item2));
		return new RegisteredDominoRenderingMatch(matchId, item4, item3);
	}

	private static (int Seed, BonesMatchResult MatchResult, BonesBoardVisualLayout Layout, int TurnIndex) FindSeededMatchLayout(Func<BonesBoardVisualLayout, bool> layoutPredicate, int minimumTileCount)
	{
		for (int i = 1; i <= 500; i++)
		{
			BonesGameId matchId = new BonesGameId($"domino-render-probe-{i}");
			BonesMatchResult bonesMatchResult = CreateSeededMatch(matchId, i, 15);
			if (bonesMatchResult.Transcript.Length == 0)
			{
				continue;
			}
			BonesRegisteredMatch registeredMatch = new BonesRegisteredMatch(new SessionId("domino-render-probe"), matchId, i, bonesMatchResult);
			BonesMatchViewerService bonesMatchViewerService = new BonesMatchViewerService(new BonesGameEngine());
			IReadOnlyList<BonesMatchFrame> readOnlyList = bonesMatchViewerService.BuildMatchTimeline(registeredMatch);
			for (int j = 0; j < readOnlyList.Count; j++)
			{
				BonesBoardVisualLayout boardLayout = readOnlyList[j].BoardLayout;
				if (boardLayout.Tiles.Count >= minimumTileCount && layoutPredicate(boardLayout))
				{
					return (Seed: i, MatchResult: bonesMatchResult, Layout: boardLayout, TurnIndex: j);
				}
			}
		}
		throw new InvalidOperationException("Unable to locate a seeded match that satisfies the layout predicate.");
	}

	private static BonesBoardVisualLayout ReplayRoundLayout(BonesGameId matchId, int seed, BonesMatchRoundRecord round, BonesBoardVisualLayoutBuilder layoutBuilder)
	{
		BonesGameEngine bonesGameEngine = new BonesGameEngine();
		BonesRoundConfig bonesRoundConfig = new BonesRoundConfig(new BonesGameId($"{matchId.Value}-r{round.RoundNumber}"), HashCode.Combine(seed, round.RoundNumber));
		BonesRoundState bonesRoundState = bonesGameEngine.StartRound(bonesRoundConfig);
		ImmutableArray<BonesEvent>.Enumerator enumerator = round.EventLog.GetEnumerator();
		while (enumerator.MoveNext())
		{
			BonesEvent current = enumerator.Current;
			BonesMove move = ((current.Kind == BonesEventKind.Pass) ? BonesMove.Pass(new BonesMoveId($"{bonesRoundConfig.GameId.Value}-pass-{current.TurnIndex}"), current.PlayerId) : BonesMove.Play(new BonesMoveId($"{bonesRoundConfig.GameId.Value}-play-{current.TurnIndex}"), current.PlayerId, current.Tile.Value, current.Side.Value));
			bonesRoundState = bonesGameEngine.ApplyMove(bonesRoundState, move);
		}
		BonesEvent[] events = round.EventLog.Where((BonesEvent roundEvent) => roundEvent.Kind == BonesEventKind.Play).ToArray();
		return layoutBuilder.BuildLayout(bonesRoundState.Board, events);
	}

	private static void AssertNonZeroHalfPipCount(int halfPip, int visiblePipCount, int seat, string halfLabel)
	{
		if (halfPip == 0)
		{
			Assert.Equal(0, visiblePipCount);
			return;
		}
		int count = BonesStandardDominoPipLayout.GetGridPositions(halfPip).Count;
		Assert.True(visiblePipCount == count, $"Seat {seat} {halfLabel} half with data-pip={halfPip} must contain {count} .pip children.");
	}

	private void RegisterSeededMatch(string sessionId, BonesGameId matchId, int seed)
	{
		BonesMatchResult matchResult = CreateSeededMatch(matchId, seed, 15);
		_playwrightHost.Catalog.Register(new BonesRegisteredMatch(new SessionId(sessionId), matchId, seed, matchResult));
	}

	private static BonesMatchResult CreateSeededMatch(BonesGameId matchId, int seed, int targetScore)
	{
		BonesMatchSimulator bonesMatchSimulator = new BonesMatchSimulator();
		BonesMatchConfig config = new BonesMatchConfig(matchId, seed, targetScore, CreateFirstLegalMoveSlots());
		return bonesMatchSimulator.RunMatch(config);
	}

	private static BonesMatchResult CreateGreedyRightArmSeededMatch(BonesGameId matchId, int seed, int targetScore)
	{
		BonesMatchSimulator bonesMatchSimulator = new BonesMatchSimulator();
		BonesMatchConfig config = new BonesMatchConfig(matchId, seed, targetScore, CreatePreferRightLegalMoveSlots());
		return bonesMatchSimulator.RunMatch(config);
	}

	private static BonesMatchResult CreateGreedyDoubleSixAtRightEndSeededMatch(BonesGameId matchId, int seed, int targetScore)
	{
		BonesMatchSimulator bonesMatchSimulator = new BonesMatchSimulator();
		BonesMatchConfig config = new BonesMatchConfig(matchId, seed, targetScore, CreatePreferRightDoubleSixEndLegalMoveSlots());
		return bonesMatchSimulator.RunMatch(config);
	}

	private static BonesMatchResult CreateGreedyBothArmsSeededMatch(BonesGameId matchId, int seed, int targetScore)
	{
		BonesMatchSimulator bonesMatchSimulator = new BonesMatchSimulator();
		BonesMatchConfig config = new BonesMatchConfig(matchId, seed, targetScore, CreatePreferBothArmsLegalMoveSlots());
		return bonesMatchSimulator.RunMatch(config);
	}

	private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreatePreferRightLegalMoveSlots()
	{
		PreferRightLegalMovePlayerSlot value = new PreferRightLegalMovePlayerSlot();
		return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
		{
			[new BonesPlayerId(1)] = value,
			[new BonesPlayerId(2)] = value,
			[new BonesPlayerId(3)] = value,
			[new BonesPlayerId(4)] = value
		};
	}

	private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreatePreferRightDoubleSixEndLegalMoveSlots()
	{
		PreferRightDoubleSixEndLegalMovePlayerSlot value = new PreferRightDoubleSixEndLegalMovePlayerSlot();
		return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
		{
			[new BonesPlayerId(1)] = value,
			[new BonesPlayerId(2)] = value,
			[new BonesPlayerId(3)] = value,
			[new BonesPlayerId(4)] = value
		};
	}

	private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreatePreferBothArmsLegalMoveSlots()
	{
		PreferBothArmsLegalMovePlayerSlot value = new PreferBothArmsLegalMovePlayerSlot();
		return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
		{
			[new BonesPlayerId(1)] = value,
			[new BonesPlayerId(2)] = value,
			[new BonesPlayerId(3)] = value,
			[new BonesPlayerId(4)] = value
		};
	}

	private static Dictionary<BonesPlayerId, IBonesPlayerSlot> CreateFirstLegalMoveSlots()
	{
		BonesFirstLegalMovePlayerSlot value = new BonesFirstLegalMovePlayerSlot();
		return new Dictionary<BonesPlayerId, IBonesPlayerSlot>
		{
			[new BonesPlayerId(1)] = value,
			[new BonesPlayerId(2)] = value,
			[new BonesPlayerId(3)] = value,
			[new BonesPlayerId(4)] = value
		};
	}
}
