using Wip.Bones.Agents.Skills;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesSkillCompositorTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.T3_1_SkillDecomposition;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ComposeMove_GivenMultipleSkills_ExecutesEachSkillAndAggregatesResult()
    {
        // Arrange: configure compositor with TileEvaluation, Blocking, and Endgame skills
        var skills = new List<ISkill>
        {
            new TileEvaluationSkill(),
            new BlockingSkill(),
            new EndgameSkill(),
        };
        var compositor = new BonesSkillCompositor(skills);

        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(3), new(5))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(6))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        // Act
        var move = await compositor.ComposeMoveAsync(state, legalMoves, CancellationToken.None);

        // Assert: a move is produced
        Assert.NotNull(move);
        Assert.False(move.IsPass);
        Assert.Equal(new BonesPlayerId(2), move.PlayerId);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ComposeMove_GivenSkillReturnsNull_ContinuesToNextSkill()
    {
        // Arrange: first skill returns null, second produces a move
        var nullSkill = new StubNullSkill();
        var pickLastSkill = new StubPickLastMoveSkill();
        var skills = new List<ISkill> { nullSkill, pickLastSkill };
        var compositor = new BonesSkillCompositor(skills);

        var state = CreateSimpleState(new BonesPlayerId(1));
        var legalMoves = CreateSimpleLegalMoves(new BonesPlayerId(1));

        // Act
        var move = await compositor.ComposeMoveAsync(state, legalMoves, CancellationToken.None);

        // Assert: second skill's move was used
        Assert.True(nullSkill.WasCalled);
        Assert.True(pickLastSkill.WasCalled);
        Assert.Equal(legalMoves[^1], move);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ComposeMove_GivenNoSkillProducesMove_FallsBackToFirstLegalMove()
    {
        // Arrange: all skills return null
        var skills = new List<ISkill>
        {
            new StubNullSkill(),
            new StubNullSkill(),
        };
        var compositor = new BonesSkillCompositor(skills);

        var state = CreateSimpleState(new BonesPlayerId(1));
        var legalMoves = CreateSimpleLegalMoves(new BonesPlayerId(1));

        // Act
        var move = await compositor.ComposeMoveAsync(state, legalMoves, CancellationToken.None);

        // Assert: falls back to first legal move
        Assert.Equal(legalMoves[0], move);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ComposeMove_GivenEmptyLegalMoves_ThrowsInvalidOperationException()
    {
        var compositor = new BonesSkillCompositor(new List<ISkill>());
        var state = CreateSimpleState(new BonesPlayerId(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => compositor.ComposeMoveAsync(state, Array.Empty<BonesMove>(), CancellationToken.None));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task ComposeMove_GivenCancellationToken_PropagatesToSkills()
    {
        // Arrange: skill that checks cancellation
        var cancellationSkill = new StubCancellationCheckSkill();
        var compositor = new BonesSkillCompositor(new List<ISkill> { cancellationSkill });

        var state = CreateSimpleState(new BonesPlayerId(1));
        var legalMoves = CreateSimpleLegalMoves(new BonesPlayerId(1));

        using var cts = new CancellationTokenSource();

        // Act
        var moveTask = compositor.ComposeMoveAsync(state, legalMoves, cts.Token);
        await cts.CancelAsync();

        // Assert: task is cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => moveTask);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task TileEvaluationSkill_GivenMultiplePlayableMoves_ReturnsHighestTotalPipMove()
    {
        var skill = new TileEvaluationSkill();
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(6))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        // Should pick (3,6) over (3,4) because 9 > 7 total pips
        Assert.NotNull(move);
        Assert.Equal(new BonesTile(new(3), new(6)), move!.Tile);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task TileEvaluationSkill_GivenOnlyPassMoves_ReturnsNull()
    {
        var skill = new TileEvaluationSkill();

        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(0), new(0))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(1), new(2))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(5), new(6))]),
                [new(3)] = new([new BonesTile(new(2), new(3))]),
                [new(4)] = new([new BonesTile(new(4), new(4))]),
            });

        var legalMoves = new List<BonesMove>
        {
            BonesMove.Pass(new BonesMoveId("pass-1"), new BonesPlayerId(2)),
        };

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        Assert.Null(move);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BlockingSkill_GivenOpponentCloseToWinning_BlocksByAvoidingThreatPips()
    {
        var skill = new BlockingSkill();
        // Opponent 1 has only 2 tiles — close to winning
        // Opponent 1's tiles are (0,1) and (3,5) — their known pips: 0,1,3,5
        // Board end is pip 3; player 2 has (3,4) and (3,2)
        // Playing (3,4) exposes pip 4, not in threat set → blocking move
        // Playing (3,2) exposes pip 2, not in threat set → but lower score
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                // Threat: only 2 tiles
                [new(1)] = new([new BonesTile(new(0), new(1)), new BonesTile(new(3), new(5))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(2))]),
                [new(3)] = new([new BonesTile(new(0), new(2)), new BonesTile(new(4), new(4)), new BonesTile(new(5), new(5)), new BonesTile(new(6), new(6))]),
                [new(4)] = new([new BonesTile(new(1), new(2)), new BonesTile(new(4), new(5)), new BonesTile(new(0), new(6)), new BonesTile(new(2), new(6))]),
            });

        var legalMoves = new List<BonesMove>
        {
            BonesMove.Play(new BonesMoveId("move-1"), new BonesPlayerId(2), new BonesTile(new(3), new(4)), BonesBoardSide.Right),
            BonesMove.Play(new BonesMoveId("move-2"), new BonesPlayerId(2), new BonesTile(new(3), new(2)), BonesBoardSide.Right),
        };

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        // Should pick (3,4) — exposes pip 4 which is not in opponent's pips {0,1,3,5}
        // (3,2) exposes pip 2 — also not in threat set but lower TotalPips (5 < 7)
        Assert.NotNull(move);
        Assert.Equal(new BonesTile(new(3), new(4)), move!.Tile);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BlockingSkill_GivenAllOpponentsHaveManyTiles_ReturnsNull()
    {
        var skill = new BlockingSkill();
        // All opponents have 4+ tiles — no imminent threat
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1)), new BonesTile(new(4), new(5)), new BonesTile(new(6), new(6)), new BonesTile(new(2), new(3))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(6))]),
                [new(3)] = new([new BonesTile(new(1), new(2)), new BonesTile(new(4), new(4)), new BonesTile(new(5), new(5)), new BonesTile(new(0), new(6))]),
                [new(4)] = new([new BonesTile(new(2), new(2)), new BonesTile(new(1), new(5)), new BonesTile(new(0), new(2)), new BonesTile(new(5), new(6))]),
            });

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        Assert.Null(move);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task EndgameSkill_GivenPlayerCloseToGoingOut_ReturnsPriorityMove()
    {
        var skill = new EndgameSkill();
        // Player 2 has only 2 tiles — endgame mode activates
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1)), new BonesTile(new(4), new(5)), new BonesTile(new(6), new(6)), new BonesTile(new(2), new(3))]),
                // Only 2 tiles → endgame
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(6))]),
                [new(3)] = new([new BonesTile(new(1), new(2)), new BonesTile(new(4), new(4)), new BonesTile(new(5), new(5)), new BonesTile(new(0), new(6))]),
                [new(4)] = new([new BonesTile(new(2), new(2)), new BonesTile(new(1), new(5)), new BonesTile(new(0), new(2)), new BonesTile(new(5), new(6))]),
            });

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        // Should pick (3,6) with 9 pips over (3,4) with 7 pips (neither is double)
        Assert.NotNull(move);
        Assert.Equal(new BonesTile(new(3), new(6)), move!.Tile);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task EndgameSkill_GivenPlayerHasManyTiles_ReturnsNull()
    {
        var skill = new EndgameSkill();
        // Player 2 has 4+ tiles — endgame not activated
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(6)), new BonesTile(new(0), new(2)), new BonesTile(new(4), new(5))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        Assert.Null(move);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task OpponentModelSkill_GivenOpponentPattern_SelectsAvoidingExposedPip()
    {
        var skill = new OpponentModelSkill();
        // State with event log containing opponent plays so OpponentModelSkill has data
        var state = CreateMidgameStateWithEvents(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [new BonesTile(new(3), new(3))],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(3), new(2))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            },
            events:
            [
                new BonesEvent(0, new BonesPlayerId(1), BonesEventKind.Play, new BonesTile(new(4), new(5)), BonesBoardSide.Left),
            ]);

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        // Should prefer a move where exposed pip doesn't match opponent's recently played pips (4,5)
        // (3,4) matches pip 3 with board's right end, exposes pip 4 → in opponent set {4,5} → score=0+7=7
        // (3,6) matches pip 3 with board's right end, exposes pip 6 → NOT in opponent set → score=50+9=59
        Assert.NotNull(move);
        Assert.Equal(new BonesTile(new(3), new(6)), move!.Tile);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task OpponentModelSkill_GivenEmptyBoard_ReturnsNull()
    {
        var skill = new OpponentModelSkill();
        var state = CreateMidgameState(
            currentPlayer: new BonesPlayerId(2),
            boardTiles: [],
            hands: new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(0), new(1))]),
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            });

        var legalMoves = CreateLegalMoves(new BonesPlayerId(2));

        var move = await skill.EvaluateAsync(state, legalMoves, CancellationToken.None);

        Assert.Null(move);
    }

    // --- Test helpers ---

    private static BonesRoundState CreateSimpleState(BonesPlayerId currentPlayer) =>
        CreateMidgameState(
            currentPlayer,
            [new BonesTile(new(0), new(0))],
            new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(1), new(2))]),
                [new(2)] = new([new BonesTile(new(3), new(4))]),
                [new(3)] = new([new BonesTile(new(5), new(6))]),
                [new(4)] = new([new BonesTile(new(4), new(5))]),
            });

    private static BonesRoundState CreateMidgameState(
        BonesPlayerId currentPlayer,
        IReadOnlyList<BonesTile> boardTiles,
        IReadOnlyDictionary<BonesPlayerId, BonesHand> hands)
    {
        var board = boardTiles.Count == 0
            ? BonesBoard.Empty
            : BonesBoardChainBuilder.FromLegacyCanonicalChain(boardTiles);

        return new BonesRoundState(
            new BonesGameId("skill-test-midgame"),
            board,
            new BonesHands(hands),
            currentPlayer,
            new BonesTile(new(0), new(0)),
            passStreak: 0,
            eventLog: []);
    }

    private static BonesRoundState CreateMidgameStateWithEvents(
        BonesPlayerId currentPlayer,
        IReadOnlyList<BonesTile> boardTiles,
        IReadOnlyDictionary<BonesPlayerId, BonesHand> hands,
        IReadOnlyList<BonesEvent> events)
    {
        var board = boardTiles.Count == 0
            ? BonesBoard.Empty
            : BonesBoardChainBuilder.FromLegacyCanonicalChain(boardTiles);

        return new BonesRoundState(
            new BonesGameId("skill-test-with-events"),
            board,
            new BonesHands(hands),
            currentPlayer,
            new BonesTile(new(0), new(0)),
            passStreak: 0,
            eventLog: events);
    }

    private static IReadOnlyList<BonesMove> CreateLegalMoves(BonesPlayerId playerId) =>
    [
        BonesMove.Play(new BonesMoveId("move-1"), playerId, new BonesTile(new(3), new(4)), BonesBoardSide.Right),
        BonesMove.Play(new BonesMoveId("move-2"), playerId, new BonesTile(new(3), new(6)), BonesBoardSide.Right),
    ];

    private static IReadOnlyList<BonesMove> CreateSimpleLegalMoves(BonesPlayerId playerId) =>
    [
        BonesMove.Play(new BonesMoveId("move-a"), playerId, new BonesTile(new(0), new(2)), BonesBoardSide.Left),
        BonesMove.Play(new BonesMoveId("move-b"), playerId, new BonesTile(new(0), new(5)), BonesBoardSide.Left),
        BonesMove.Play(new BonesMoveId("move-c"), playerId, new BonesTile(new(1), new(6)), BonesBoardSide.Left),
    ];

    // --- Stub skills for testing compositor behavior ---

    private sealed class StubNullSkill : ISkill
    {
        public bool WasCalled { get; private set; }

        public Task<BonesMove?> EvaluateAsync(
            BonesRoundState state,
            IReadOnlyList<BonesMove> legalMoves,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult<BonesMove?>(null);
        }
    }

    private sealed class StubPickLastMoveSkill : ISkill
    {
        public bool WasCalled { get; private set; }

        public Task<BonesMove?> EvaluateAsync(
            BonesRoundState state,
            IReadOnlyList<BonesMove> legalMoves,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult<BonesMove?>(legalMoves[^1]);
        }
    }

    private sealed class StubCancellationCheckSkill : ISkill
    {
        public async Task<BonesMove?> EvaluateAsync(
            BonesRoundState state,
            IReadOnlyList<BonesMove> legalMoves,
            CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken);
            return null;
        }
    }
}
