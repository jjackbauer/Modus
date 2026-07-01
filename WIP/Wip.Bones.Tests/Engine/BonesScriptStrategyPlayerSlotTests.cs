using System.Collections.Immutable;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Engine;

public sealed class BonesScriptStrategyPlayerSlotTests
{
    private const string ChecklistItem =
        BonesRequirementsChecklistItems.ScriptStrategyPlayerSlot;

    private const string ScriptStrategiesCoverageItem =
        BonesScriptStrategiesRequirementsChecklistItems.TestsCoverage;

    private static readonly BonesStrategyId StrategyId = new("seat-2-script-v1");

    private const string LastLegalMoveScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[^1];
            }
        }
        """;

    private const string IllegalMoveScriptSource =
        """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;
        using Wip.Bones.Identifiers;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return BonesMove.Play(
                    new BonesMoveId("illegal-move"),
                    new BonesPlayerId(2),
                    new BonesTile(new(0), new(0)),
                    BonesBoardSide.Right);
            }
        }
        """;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesScriptStrategyPlayerSlot_GivenScriptSelectsLegalMove_ExpectedReturnsThatMove()
    {
        var host = new BonesStrategyScriptHost();
        var warningSink = new CollectingWarningSink();
        var playerSlot = new BonesScriptStrategyPlayerSlot(host, StrategyId, LastLegalMoveScriptSource, warningSink);

        var state = CreateMidgameState();
        var legalMoves = CreateLegalMoves();

        var selectedMove = playerSlot.ChooseMove(state, legalMoves);

        Assert.Equal(legalMoves[^1], selectedMove);
        Assert.Empty(warningSink.Warnings);
        Assert.Equal(1, host.CompileInvocationCount);

        var secondSelection = playerSlot.ChooseMove(state, legalMoves);

        Assert.Equal(legalMoves[^1], secondSelection);
        Assert.Equal(1, host.CompileInvocationCount);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", ScriptStrategiesCoverageItem)]
    public void BonesScriptStrategyPlayerSlot_GivenScriptSelectsIllegalMove_ExpectedFallsBackToFirstLegalMove()
    {
        var host = new BonesStrategyScriptHost();
        var warningSink = new CollectingWarningSink();
        var playerSlot = new BonesScriptStrategyPlayerSlot(host, StrategyId, IllegalMoveScriptSource, warningSink);

        var state = CreateMidgameState();
        var legalMoves = CreateLegalMoves();

        var selectedMove = playerSlot.ChooseMove(state, legalMoves);

        Assert.Equal(legalMoves[0], selectedMove);

        var warning = Assert.Single(warningSink.Warnings);
        Assert.Equal(StrategyId, warning.StrategyId);
        Assert.Equal(legalMoves[0], warning.AppliedMove);
        Assert.NotEqual(legalMoves[0], warning.RejectedMove);
        Assert.Contains("not in the legal set", warning.Message, StringComparison.Ordinal);
    }

    private static BonesRoundState CreateMidgameState() =>
        new(
            new BonesGameId("script-player-slot-test"),
            BonesBoardChainBuilder.FromLegacyCanonicalChain([new BonesTile(new(3), new(3))]),
            new BonesHands(new Dictionary<BonesPlayerId, BonesHand>
            {
                [new(1)] = new([new BonesTile(new(3), new(5))]),
                [new(2)] = new([new BonesTile(new(3), new(4)), new BonesTile(new(0), new(1))]),
                [new(3)] = new([new BonesTile(new(1), new(2))]),
                [new(4)] = new([new BonesTile(new(2), new(2))]),
            }.ToImmutableDictionary()),
            new BonesPlayerId(2),
            new BonesTile(new(3), new(3)),
            passStreak: 0,
            eventLog: []);

    private static IReadOnlyList<BonesMove> CreateLegalMoves() =>
    [
        BonesMove.Play(new BonesMoveId("move-1"), new BonesPlayerId(2), new BonesTile(new(3), new(4)), BonesBoardSide.Right),
        BonesMove.Play(new BonesMoveId("move-2"), new BonesPlayerId(2), new BonesTile(new(3), new(4)), BonesBoardSide.Left),
    ];

    private sealed class CollectingWarningSink : IBonesScriptStrategyExecutionWarningSink
    {
        public List<BonesScriptStrategyExecutionWarning> Warnings { get; } = [];

        public void Emit(BonesScriptStrategyExecutionWarning warning) => Warnings.Add(warning);
    }
}
