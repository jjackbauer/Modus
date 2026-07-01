using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Play;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Domain;

public sealed class BonesBoardOrientationTests
{
    private const string BoardEndsChecklistItem = BonesBoardOrientationRequirementsChecklistItems.BoardEndsFromFacings;
    private const string ChainEdgeInvariantChecklistItem = BonesBoardOrientationRequirementsChecklistItems.ChainEdgeInvariant;
    private const string PlayAlignedAppendPrependChecklistItem = BonesBoardOrientationRequirementsChecklistItems.PlayAlignedAppendPrepend;

    private readonly BonesGameEngine _engine = new();

    [Theory]
    [Trait("ChecklistItem", BoardEndsChecklistItem)]
    [InlineData(3, 3)]
    [InlineData(1, 4)]
    public void BonesBoard_GivenSingleTile_ExpectedLeftAndRightEndEqualChainFacings(int lowPip, int highPip)
    {
        var tile = new BonesTile(new BonesPipCount(lowPip), new BonesPipCount(highPip));
        var board = new BonesBoard([BonesBoardChainBuilder.OpeningChainTile(tile)]);

        Assert.Equal(new BonesPipCount(lowPip), board.LeftEnd!.Value.Pip);
        Assert.Equal(tile.IsDouble ? new BonesPipCount(lowPip) : new BonesPipCount(highPip), board.RightEnd!.Value.Pip);
        Assert.Equal(board.LeftEnd.Value.Pip, board.Tiles[0].ChainLeftPip);
        Assert.Equal(board.RightEnd.Value.Pip, board.Tiles[0].ChainRightPip);
    }

    [Fact]
    [Trait("ChecklistItem", BoardEndsChecklistItem)]
    public void BonesBoard_GivenEmptyBoard_ExpectedNoEnds()
    {
        var board = BonesBoard.Empty;

        Assert.True(board.IsEmpty);
        Assert.Null(board.LeftEnd);
        Assert.Null(board.RightEnd);
    }

    [Fact]
    [Trait("ChecklistItem", ChainEdgeInvariantChecklistItem)]
    public void BonesBoard_GivenValidChain_ExpectedValidateChainEdgesPasses()
    {
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain(
        [
            new BonesTile(new(3), new(3)),
            new BonesTile(new(3), new(4)),
            new BonesTile(new(4), new(6)),
        ]);

        board.ValidateChainEdges();
    }

    [Fact]
    [Trait("ChecklistItem", ChainEdgeInvariantChecklistItem)]
    public void BonesBoard_GivenBrokenChain_ExpectedValidateChainEdgesThrows()
    {
        var board = new BonesBoard(
        [
            new BonesChainTile(new BonesTile(new(1), new(3)), new(1), new(3)),
            new BonesChainTile(new BonesTile(new(1), new(5)), new(1), new(5)),
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => board.ValidateChainEdges());

        Assert.Contains("exit 3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("entry 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", PlayAlignedAppendPrependChecklistItem)]
    public void BonesGameEngine_GivenRightAppend_ExpectedNewTileChainLeftMatchesPriorRightEnd()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles:
            [
                new BonesTile(new(3), new(3)),
            ],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var playMove = _engine.GetLegalMoves(state, new BonesPlayerId(2))
            .First(move => move.Tile == new BonesTile(new(3), new(4)) && move.Side == BonesBoardSide.Right);

        var updated = _engine.ApplyMove(state, playMove);

        Assert.Equal(2, updated.Board.Tiles.Length);
        Assert.Equal(new BonesPipCount(3), updated.Board.Tiles[0].ChainRightPip);
        Assert.Equal(new BonesPipCount(3), updated.Board.Tiles[1].ChainLeftPip);
        Assert.Equal(new BonesPipCount(4), updated.Board.Tiles[1].ChainRightPip);
        Assert.Equal(new BonesPipCount(4), updated.Board.RightEnd!.Value.Pip);
        updated.Board.ValidateChainEdges();
    }

    [Fact]
    [Trait("ChecklistItem", PlayAlignedAppendPrependChecklistItem)]
    public void BonesGameEngine_GivenLeftPrepend_ExpectedNewTileChainRightMatchesPriorLeftEnd()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles:
            [
                new BonesTile(new(3), new(3)),
            ],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(2)] = new([new BonesTile(new(3), new(5))]),
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var playMove = _engine.GetLegalMoves(state, new BonesPlayerId(2))
            .First(move => move.Tile == new BonesTile(new(3), new(5)) && move.Side == BonesBoardSide.Left);

        var updated = _engine.ApplyMove(state, playMove);

        Assert.Equal(2, updated.Board.Tiles.Length);
        Assert.Equal(new BonesPipCount(5), updated.Board.Tiles[0].ChainLeftPip);
        Assert.Equal(new BonesPipCount(3), updated.Board.Tiles[0].ChainRightPip);
        Assert.Equal(new BonesPipCount(3), updated.Board.Tiles[1].ChainLeftPip);
        Assert.Equal(new BonesPipCount(5), updated.Board.LeftEnd!.Value.Pip);
        updated.Board.ValidateChainEdges();
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.HandEventIdentityUnchanged)]
    public void BonesGameEngine_GivenPlay_ExpectedEventAndHandRetainCanonicalBonesTile()
    {
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var canonicalTile = new BonesTile(new(3), new(4));
        var playMove = _engine.GetLegalMoves(state, new BonesPlayerId(2))
            .First(move => move.Tile == canonicalTile);

        var updated = _engine.ApplyMove(state, playMove);

        Assert.Equal(canonicalTile, updated.EventLog[^1].Tile);
        Assert.DoesNotContain(canonicalTile, updated.Hands.GetHand(new BonesPlayerId(2)).Tiles);
        Assert.Equal(canonicalTile, updated.Board.Tiles.Single(chainTile => chainTile.Tile == canonicalTile).Tile);
    }

    [Fact]
    [Trait("ChecklistItem", BonesBoardOrientationRequirementsChecklistItems.DownstreamCompileParity)]
    public void BonesBoardOrientation_GivenAgentsPromptBuilder_ExpectedOpenEndsFromChainFacings()
    {
        var board = BonesBoardChainBuilder.FromLegacyCanonicalChain(
        [
            new BonesTile(new(3), new(3)),
            new BonesTile(new(3), new(4)),
        ]);
        var state = new BonesRoundState(
            new BonesGameId("agents-parity"),
            board,
            new BonesHands(new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(1), new(2))]),
                [new(3)] = new([new BonesTile(new(2), new(2))]),
                [new(4)] = new([new BonesTile(new(4), new(5))]),
            }),
            new BonesPlayerId(1),
            new BonesTile(new(0), new(0)),
            passStreak: 0,
            []);

        var prompt = BonesPlayMatchPromptBuilder.BuildTurnRequest(
            new BonesPlayerId(1),
            new BonesStrategyDocument(
                new BonesStrategyId("test-strategy"),
                new BonesPlayerId(1),
                "test strategy"),
            state,
            []).Messages.Single(message => message.Role == "user").Content;

        Assert.Contains($"Left={board.LeftEnd!.Value.Pip}", prompt, StringComparison.Ordinal);
        Assert.Contains($"Right={board.RightEnd!.Value.Pip}", prompt, StringComparison.Ordinal);
        Assert.Equal(board.LeftEnd.Value.Pip, board.Tiles[0].ChainLeftPip);
        Assert.Equal(board.RightEnd.Value.Pip, board.Tiles[^1].ChainRightPip);
    }

    private static BonesRoundState CreateMidgameState(
        BonesPlayerId currentPlayer,
        IReadOnlyList<BonesTile> boardTiles,
        IReadOnlyDictionary<BonesPlayerId, BonesHand> hands) =>
        new(
            new BonesGameId("orientation-midgame"),
            BonesBoardChainBuilder.FromLegacyCanonicalChain(boardTiles),
            new BonesHands(hands),
            currentPlayer,
            new BonesTile(new(0), new(0)),
            passStreak: 0,
            []);
}