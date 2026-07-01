using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.Web.Viewer;
using Xunit;

namespace Wip.Bones.Web.Tests;

public sealed class BonesMatchViewerServiceReplayScalingTests
{
    private const string ChecklistItem =
        BonesViewerReplayScalingRequirementsChecklistItems.FrameCompletionFields;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchViewerService_GivenMidTurnFrame_ExpectedIsRoundCompleteFalseAndNoWinner()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("replay-frame-mid"), shuffleSeed: 442);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (state.EventLog.Length < 3)
        {
            var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
            if (legalMoves.Count == 0)
                break;

            state = engine.ApplyMove(state, legalMoves[0]);
        }

        Assert.True(state.EventLog.Length >= 2, "Seeded replay must reach a mid-turn frame.");
        Assert.Null(state.Outcome);

        var frames = service.BuildTimeline(state.EventLog);
        var midFrame = frames[^2];

        Assert.False(midFrame.IsRoundComplete);
        Assert.Null(midFrame.WinnerSeat);
        Assert.Null(midFrame.RoundPipScore);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchViewerService_GivenFinalTurnFrame_ExpectedIsRoundCompleteTrueWithWinnerAndPipScore()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("replay-frame-final"), shuffleSeed: 9001);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (!engine.IsRoundComplete(state))
        {
            var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
            var move = legalMoves.Count == 0
                ? BonesMove.Pass(new BonesMoveId($"pass-{state.EventLog.Length}"), state.CurrentPlayer)
                : legalMoves[0];
            state = engine.ApplyMove(state, move);
        }

        var frames = service.BuildTimeline(state.EventLog);
        var finalFrame = frames[^1];
        var expectedScore = engine.ScoreRound(state).Points;

        Assert.True(finalFrame.IsRoundComplete);
        Assert.Equal(state.Winner?.Seat, finalFrame.WinnerSeat);
        Assert.Equal(expectedScore, finalFrame.RoundPipScore);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesMatchViewerService_GivenMidTurnFrame_ExpectedBoardLayoutTileCountLessThanFinalSnapshot()
    {
        var engine = new BonesGameEngine();
        var config = new BonesRoundConfig(new BonesGameId("replay-frame-board-count"), shuffleSeed: 442);
        var service = new BonesMatchViewerService(engine, config);
        var state = engine.StartRound(config);

        while (!engine.IsRoundComplete(state))
        {
            var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
            var move = legalMoves.Count == 0
                ? BonesMove.Pass(new BonesMoveId($"pass-{state.EventLog.Length}"), state.CurrentPlayer)
                : legalMoves[0];
            state = engine.ApplyMove(state, move);
        }

        var frames = service.BuildTimeline(state.EventLog);
        var midFrame = frames[frames.Count / 2];
        var finalFrame = frames[^1];
        var snapshot = service.BuildSnapshot(state);

        Assert.False(midFrame.IsRoundComplete);
        Assert.True(finalFrame.IsRoundComplete);
        Assert.True(midFrame.BoardLayout.Tiles.Count < snapshot.BoardLayout.Tiles.Count);
        Assert.Equal(snapshot.BoardLayout.Tiles.Count, finalFrame.BoardLayout.Tiles.Count);
    }
}