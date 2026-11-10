using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.Hands;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class HeadsUpHandEngineTests
{
    [Fact]
    public void Start_GivenDefaultSetup_ExpectedBlindsHoleCardsAndConservation()
    {
        var state = HeadsUpHandEngine.Start(42);

        Assert.Equal(1, state.HandNumber);
        Assert.Equal(Street.Preflop, state.Street);
        Assert.Equal(0, state.ButtonSeat);
        Assert.Equal(0, state.ActingSeat);
        Assert.Empty(state.Board);
        Assert.Equal(new long[] { 995, 990 }, state.Stacks.ToArray());
        Assert.Equal(new long[] { 5, 10 }, state.StreetCommitted.ToArray());
        Assert.Equal(15L, state.Pot);
        Assert.Equal(10L, state.CurrentBet);
        Assert.Equal(10L, state.LastRaiseSize);
        Assert.False(state.IsComplete);
        Assert.False(state.ShowdownReached);
        Assert.Empty(state.Winners);
        Assert.Equal(1000L, state.StartingStack);
        Assert.Equal(5L, state.SmallBlind);
        Assert.Equal(10L, state.BigBlind);
        Assert.Equal(2000L, state.Stacks.Sum() + state.Pot);

        Assert.Equal(new[] { Card.Parse("7d"), Card.Parse("9c") }, state.HoleCardsSeat0.ToArray());
        Assert.Equal(new[] { Card.Parse("Qc"), Card.Parse("4s") }, state.HoleCardsSeat1.ToArray());
    }

    [Fact]
    public void GetLegalActions_GivenSmallBlindFacingBigBlind_ExpectedFoldCallRaiseAllIn()
    {
        var state = HeadsUpHandEngine.Start(42);
        var legal = HeadsUpHandEngine.GetLegalActions(state);

        Assert.False(legal.CanCheck);
        Assert.True(legal.CanFold);
        Assert.True(legal.CanCall);
        Assert.Equal(5L, legal.CallAmount);
        Assert.False(legal.CanBet);
        Assert.True(legal.CanRaise);
        Assert.Equal(20L, legal.MinRaiseTo);
        Assert.Equal(1000L, legal.MaxRaiseTo);
        Assert.True(legal.CanAllIn);
        Assert.Equal(
            new[] { ActionKind.Fold, ActionKind.Call, ActionKind.Raise, ActionKind.AllIn },
            legal.Actions.Select(a => a.Kind).ToArray());

        AssertIllegalReason("check-facing-bet", () => HeadsUpHandEngine.Apply(state, PokerAction.Check()));
    }

    [Fact]
    public void Apply_GivenSmallBlindCallAndBigBlindCheck_ExpectedFlopAndPotTwenty()
    {
        var state = HeadsUpHandEngine.Start(42);
        var afterCall = HeadsUpHandEngine.Apply(state, PokerAction.Call());

        Assert.Equal(Street.Preflop, afterCall.Street);
        Assert.Equal(20L, afterCall.Pot);
        Assert.Equal(1, afterCall.ActingSeat);
        Assert.Equal(new long[] { 10, 10 }, afterCall.StreetCommitted.ToArray());
        Assert.Equal(new long[] { 990, 990 }, afterCall.Stacks.ToArray());
        Assert.True(HeadsUpHandEngine.GetLegalActions(afterCall).CanCheck);

        var flop = HeadsUpHandEngine.Apply(afterCall, PokerAction.Check());

        Assert.Equal(Street.Flop, flop.Street);
        Assert.Equal(3, flop.Board.Count);
        Assert.Equal(20L, flop.Pot);
        Assert.Equal(1, flop.ActingSeat);
        Assert.Equal(new[] { Card.Parse("8c"), Card.Parse("7h"), Card.Parse("2s") }, flop.Board.ToArray());
        Assert.Equal(2000L, flop.Stacks.Sum() + flop.Pot);
    }

    [Fact]
    public void Apply_GivenRaiseBelowMinimum_ExpectedRaiseBelowMin()
    {
        var state = HeadsUpHandEngine.Start(42);

        AssertIllegalReason("raise-below-min", () => HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(19)));

        var raised = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(20));

        Assert.Equal(20L, raised.CurrentBet);
        Assert.Equal(30L, raised.Pot);
        Assert.Equal(new long[] { 20, 10 }, raised.StreetCommitted.ToArray());
        Assert.Equal(10L, raised.LastRaiseSize);
        Assert.Equal(30L, HeadsUpHandEngine.GetLegalActions(raised).MinRaiseTo);
        Assert.Equal(2000L, raised.Stacks.Sum() + raised.Pot);
    }

    [Fact]
    public void Apply_GivenRaiseOfThirtyPreflop_ExpectedNextMinimumFifty()
    {
        var state = HeadsUpHandEngine.Apply(HeadsUpHandEngine.Start(42), PokerAction.RaiseTo(30));

        Assert.Equal(30L, state.CurrentBet);
        Assert.Equal(20L, state.LastRaiseSize);
        Assert.Equal(40L, state.Pot);
        Assert.Equal(new long[] { 970, 990 }, state.Stacks.ToArray());

        var legal = HeadsUpHandEngine.GetLegalActions(state);

        Assert.Equal(50L, legal.MinRaiseTo);
        Assert.Equal(1000L, legal.MaxRaiseTo);
        Assert.Equal(20L, legal.CallAmount);
    }

    [Fact]
    public void Apply_GivenPostflopBets_ExpectedBigBlindMinimumAndStackCap()
    {
        var state = EngineTestSupport.LimpAndFlop();
        var legal = HeadsUpHandEngine.GetLegalActions(state);

        Assert.Equal(0L, state.CurrentBet);
        Assert.Equal(10L, legal.MinRaiseTo);
        Assert.Equal(990L, legal.MaxRaiseTo);
        Assert.True(legal.CanBet);
        Assert.False(legal.CanRaise);

        var bet = HeadsUpHandEngine.Apply(state, PokerAction.BetTo(10));
        Assert.Equal(10L, bet.CurrentBet);
        Assert.Equal(30L, bet.Pot);
        Assert.Equal(10L, bet.LastRaiseSize);

        AssertIllegalReason("raise-below-min", () => HeadsUpHandEngine.Apply(state, PokerAction.BetTo(9)));
        AssertIllegalReason("raise-above-stack", () => HeadsUpHandEngine.Apply(state, PokerAction.BetTo(991)));
    }

    [Fact]
    public void Apply_GivenFoldWhenCheckingIsLegal_ExpectedFoldNotFacingBet()
    {
        var state = HeadsUpHandEngine.Apply(HeadsUpHandEngine.Start(42), PokerAction.Call());

        AssertIllegalReason("fold-not-facing-bet", () => HeadsUpHandEngine.Apply(state, PokerAction.Fold()));
    }

    [Fact]
    public void Apply_GivenCallWhenNothingToCall_ExpectedCallNotFacingBet()
    {
        var state = HeadsUpHandEngine.Apply(HeadsUpHandEngine.Start(42), PokerAction.Call());

        AssertIllegalReason("call-not-facing-bet", () => HeadsUpHandEngine.Apply(state, PokerAction.Call()));
    }

    [Fact]
    public void Apply_GivenFoldFacingBet_ExpectedOpponentWinsWithoutShowdown()
    {
        var state = HeadsUpHandEngine.Apply(HeadsUpHandEngine.Start(42), PokerAction.Fold());

        Assert.True(state.IsComplete);
        Assert.Equal(Street.Complete, state.Street);
        Assert.False(state.ShowdownReached);
        Assert.Equal(new[] { 1 }, state.Winners.ToArray());
        Assert.Equal(0L, state.Pot);
        Assert.Equal(new long[] { 995, 1005 }, state.Stacks.ToArray());
        Assert.Equal(2000L, state.Stacks.Sum());
    }

    [Fact]
    public void Apply_GivenStackBelowMinimumRaise_ExpectedAllInLegalAndRaiseRejected()
    {
        var state = HeadsUpHandEngine.Start(42, startingStack: 18);
        var legal = HeadsUpHandEngine.GetLegalActions(state);

        Assert.Equal(20L, legal.MinRaiseTo);
        Assert.Equal(18L, legal.MaxRaiseTo);
        Assert.False(legal.CanRaise);
        Assert.False(legal.CanBet);
        Assert.True(legal.CanAllIn);
        Assert.True(legal.CanCall);
        Assert.Equal(5L, legal.CallAmount);

        AssertIllegalReason("raise-below-min", () => HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(18)));

        var allIn = HeadsUpHandEngine.Apply(state, PokerAction.AllIn());

        Assert.Equal(18L, allIn.CurrentBet);
        Assert.Equal(10L, allIn.LastRaiseSize);
        Assert.Equal(28L, allIn.Pot);
        Assert.Equal(new long[] { 0, 8 }, allIn.Stacks.ToArray());
        Assert.Equal(8L, HeadsUpHandEngine.GetLegalActions(allIn).CallAmount);
        Assert.Equal(36L, allIn.Stacks.Sum() + allIn.Pot);

        var called = HeadsUpHandEngine.Apply(allIn, PokerAction.Call());

        Assert.True(called.IsComplete);
        Assert.True(called.ShowdownReached);
        Assert.Equal(5, called.Board.Count);
        Assert.Equal(Street.Complete, called.Street);
        Assert.Equal(36L, called.Stacks.Sum());
        Assert.Equal(0L, called.Pot);
    }

    [Fact]
    public void Apply_GivenCheckCheckFlop_ExpectedTurnWithFourBoardCards()
    {
        var flop = EngineTestSupport.LimpAndFlop();
        var turn = HeadsUpHandEngine.Apply(
            HeadsUpHandEngine.Apply(flop, PokerAction.Check()),
            PokerAction.Check());

        Assert.Equal(Street.Turn, turn.Street);
        Assert.Equal(4, turn.Board.Count);
        Assert.Equal(1, turn.ActingSeat);
        Assert.Equal(20L, turn.Pot);
        Assert.Equal(
            new[]
            {
                Card.Parse("8c"), Card.Parse("7h"), Card.Parse("2s"), Card.Parse("3d"),
            },
            turn.Board.ToArray());
    }

    [Fact]
    public void Apply_GivenCheckCheckTurn_ExpectedRiverWithFiveBoardCards()
    {
        var turn = HeadsUpHandEngine.Apply(
            EngineTestSupport.LimpAndFlop(),
            PokerAction.Check());
        turn = HeadsUpHandEngine.Apply(turn, PokerAction.Check());
        turn = HeadsUpHandEngine.Apply(turn, PokerAction.Check());
        turn = HeadsUpHandEngine.Apply(turn, PokerAction.Check());

        Assert.Equal(Street.River, turn.Street);
        Assert.Equal(5, turn.Board.Count);
        Assert.Equal(1, turn.ActingSeat);
        Assert.Equal(20L, turn.Pot);
        Assert.Equal(
            new[]
            {
                Card.Parse("8c"), Card.Parse("7h"), Card.Parse("2s"), Card.Parse("3d"), Card.Parse("5c"),
            },
            turn.Board.ToArray());
    }

    [Fact]
    public void Apply_GivenCheckCheckRiver_ExpectedShowdownComplete()
    {
        var river = EngineTestSupport.CheckDown();

        Assert.Equal(Street.Complete, river.Street);
        Assert.True(river.IsComplete);
        Assert.True(river.ShowdownReached);
        Assert.Equal(5, river.Board.Count);
        Assert.Equal(0L, river.Pot);
        Assert.Equal(-1, river.ActingSeat);
    }

    [Fact]
    public void Apply_GivenSeatZeroStrongerHand_ExpectedSeatZeroWinsPot()
    {
        var state = EngineTestSupport.CheckDown();

        Assert.True(state.ShowdownReached);
        Assert.Equal(new[] { 0 }, state.Winners.ToArray());
        Assert.Equal(new long[] { 1010, 990 }, state.Stacks.ToArray());
        Assert.Equal(0L, state.Pot);
        Assert.Equal(Street.Complete, state.Street);
        Assert.Equal(2000L, state.Stacks.Sum());

        var seat0 = HandEvaluator.Evaluate(EngineTestSupport.SevenCards(state, 0));
        var seat1 = HandEvaluator.Evaluate(EngineTestSupport.SevenCards(state, 1));

        Assert.Equal(HandCategory.OnePair, seat0.Category);
        Assert.Equal(HandCategory.HighCard, seat1.Category);
        Assert.True(seat0.Score > seat1.Score);
    }

    [Fact]
    public void Apply_GivenBoardPlays_ExpectedSplitPotAndTwoWinners()
    {
        var state = EngineTestSupport.CheckDown(206);

        Assert.Equal(new[] { Card.Parse("4c"), Card.Parse("2s") }, state.HoleCardsSeat0.ToArray());
        Assert.Equal(new[] { Card.Parse("Qh"), Card.Parse("3c") }, state.HoleCardsSeat1.ToArray());
        Assert.Equal(
            new[]
            {
                Card.Parse("8s"), Card.Parse("7c"), Card.Parse("6h"), Card.Parse("5s"), Card.Parse("4s"),
            },
            state.Board.ToArray());

        Assert.True(state.ShowdownReached);
        Assert.Equal(new[] { 0, 1 }, state.Winners.ToArray());
        Assert.Equal(new long[] { 1000, 1000 }, state.Stacks.ToArray());
        Assert.Equal(0L, state.Pot);
        Assert.Equal(2000L, state.Stacks.Sum());

        var board = HandEvaluator.Evaluate(state.Board);
        var seat0 = HandEvaluator.Evaluate(EngineTestSupport.SevenCards(state, 0));
        var seat1 = HandEvaluator.Evaluate(EngineTestSupport.SevenCards(state, 1));

        Assert.Equal(HandCategory.Straight, board.Category);
        Assert.Equal(board.Score, seat0.Score);
        Assert.Equal(board.Score, seat1.Score);
    }

    [Fact]
    public void SplitPot_GivenOddPot_ExpectedExtraChipToNonButton()
    {
        Assert.Equal(new long[] { 10, 11 }, HeadsUpHandEngine.SplitPot(21, 0).ToArray());
        Assert.Equal(new long[] { 11, 10 }, HeadsUpHandEngine.SplitPot(21, 1).ToArray());
        Assert.Equal(new long[] { 10, 10 }, HeadsUpHandEngine.SplitPot(20, 0).ToArray());
        Assert.Equal(new long[] { 0, 1 }, HeadsUpHandEngine.SplitPot(1, 0).ToArray());
    }

    [Fact]
    public void Apply_GivenBothAllInPreflop_ExpectedFullBoardAndShowdown()
    {
        var allIn = HeadsUpHandEngine.Apply(HeadsUpHandEngine.Start(42), PokerAction.AllIn());

        Assert.Equal(1000L, allIn.CurrentBet);
        Assert.Equal(990L, allIn.LastRaiseSize);
        Assert.Equal(1010L, allIn.Pot);
        Assert.Equal(new long[] { 0, 990 }, allIn.Stacks.ToArray());

        var called = HeadsUpHandEngine.Apply(allIn, PokerAction.Call());

        Assert.True(called.IsComplete);
        Assert.True(called.ShowdownReached);
        Assert.Equal(Street.Complete, called.Street);
        Assert.Equal(5, called.Board.Count);
        Assert.Equal(new long[] { 2000, 0 }, called.Stacks.ToArray());
        Assert.Equal(new[] { 0 }, called.Winners.ToArray());
        Assert.Empty(HeadsUpHandEngine.GetLegalActions(called).Actions);

        AssertIllegalReason("hand-complete", () => HeadsUpHandEngine.Apply(called, PokerAction.Check()));
    }

    [Fact]
    public void Apply_GivenFiveHundredSeedsAndRandomPolicy_ExpectedChipConservationAndCompletion()
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var state = HeadsUpHandEngine.Start(seed);
            var random = new Xorshift64Star(seed ^ 0x9E3779B97F4A7C15UL);
            var actionCount = 0;

            Assert.Equal(2000L, state.Stacks.Sum() + state.Pot);

            while (!state.IsComplete)
            {
                var legal = HeadsUpHandEngine.GetLegalActions(state);
                Assert.NotEmpty(legal.Actions);

                var next = legal.Actions[(int)(random.Next() % (ulong)legal.Actions.Count)];
                state = HeadsUpHandEngine.Apply(state, next);
                actionCount++;

                Assert.True(actionCount <= 200, $"seed {seed} exceeded 200 actions");
                Assert.Equal(2000L, state.Stacks.Sum() + state.Pot);

                if (state.Street != Street.Complete)
                {
                    var expectedBoard = state.Street switch
                    {
                        Street.Preflop => 0,
                        Street.Flop => 3,
                        Street.Turn => 4,
                        Street.River => 5,
                        Street.Showdown => 5,
                        _ => -1,
                    };

                    Assert.Equal(expectedBoard, state.Board.Count);
                }
                else if (state.ShowdownReached)
                {
                    Assert.Equal(5, state.Board.Count);
                }
            }

            Assert.Equal(0L, state.Pot);
            Assert.Equal(2000L, state.Stacks.Sum());
        }
    }

    [Fact]
    public void Apply_GivenSameSeedAndPolicy_ExpectedIdenticalJsonAndHashAndDifferentSeedDiffers()
    {
        var first = EngineTestSupport.RandomRecord(42);
        var second = EngineTestSupport.RandomRecord(42);
        var other = EngineTestSupport.RandomRecord(43);

        Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEqual(first.Hash, other.Hash);
        Assert.Equal(64, first.Hash.Length);
        Assert.Equal(first.Hash.ToLowerInvariant(), first.Hash);
    }

    [Fact]
    public void Apply_GivenAction_ExpectedInputStateUnchanged()
    {
        var state = HeadsUpHandEngine.Start(42);
        var before = Wip.Tholdem.History.PlayerObservation.ForSeat(state, 0).ToCanonicalJson();

        var next = HeadsUpHandEngine.Apply(state, PokerAction.RaiseTo(20));

        Assert.NotSame(state, next);
        Assert.Equal(15L, state.Pot);
        Assert.Equal(995L, state.Stacks[0]);
        Assert.Equal(990L, state.Stacks[1]);
        Assert.Equal(new long[] { 5, 10 }, state.StreetCommitted.ToArray());
        Assert.Equal(10L, state.CurrentBet);
        Assert.Equal(0, state.ActingSeat);
        Assert.Equal(Street.Preflop, state.Street);
        Assert.Empty(state.Board);
        Assert.False(state.IsComplete);
        Assert.Equal(before, Wip.Tholdem.History.PlayerObservation.ForSeat(state, 0).ToCanonicalJson());
    }

    [Fact]
    public void Apply_GivenCompleteHand_ExpectedHandComplete()
    {
        var complete = EngineTestSupport.PlayAllInHand(42);

        AssertIllegalReason("hand-complete", () => HeadsUpHandEngine.Apply(complete, PokerAction.Check()));
    }

    [Fact]
    public void Apply_GivenNonZeroAmountOnNonBettingAction_ExpectedAmountNotAllowed()
    {
        var facingBet = HeadsUpHandEngine.Start(42);
        AssertIllegalReason(
            "amount-not-allowed",
            () => HeadsUpHandEngine.Apply(facingBet, new PokerAction(ActionKind.Fold, 5)));
        AssertIllegalReason(
            "amount-not-allowed",
            () => HeadsUpHandEngine.Apply(facingBet, new PokerAction(ActionKind.Call, 5)));
        AssertIllegalReason(
            "amount-not-allowed",
            () => HeadsUpHandEngine.Apply(facingBet, new PokerAction(ActionKind.AllIn, 5)));

        var checking = HeadsUpHandEngine.Apply(facingBet, PokerAction.Call());
        AssertIllegalReason(
            "amount-not-allowed",
            () => HeadsUpHandEngine.Apply(checking, new PokerAction(ActionKind.Check, 1)));
    }

    [Fact]
    public void Replay_GivenRecord_ExpectedSameFinalStacksWinnersAndHash()
    {
        var record = EngineTestSupport.CheckDownRecord(42);
        var replayed = HeadsUpHandEngine.Replay(record);

        Assert.Equal(record.FinalStacks.ToArray(), replayed.Stacks.ToArray());
        Assert.Equal(record.Winners.ToArray(), replayed.Winners.ToArray());
        Assert.Equal(record.Board.ToArray(), replayed.Board.ToArray());
        Assert.Equal(record.Showdown, replayed.ShowdownReached);
        Assert.Equal(record.PotAwarded, replayed.AwardedPot);

        var rebuilt = Wip.Tholdem.History.HandRecord.From(replayed);
        Assert.Equal(record.ToCanonicalJson(), rebuilt.ToCanonicalJson());
        Assert.Equal(record.Hash, rebuilt.Hash);
    }

    [Fact]
    public void Replay_GivenTamperedAction_ExpectedInvalidOperationException()
    {
        var record = EngineTestSupport.CheckDownRecord(42);
        var actions = record.Actions.ToArray();
        Assert.Equal("Call", actions[0].Kind);
        Assert.Equal("Check", actions[1].Kind);
        actions[1] = actions[1] with { Kind = "AllIn", Amount = 0 };
        var tampered = record with { Actions = actions };

        var exception = Assert.Throws<InvalidOperationException>(
            () => { _ = HeadsUpHandEngine.Replay(tampered); });

        Assert.Contains("pot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(record.Hash, tampered.Hash);
    }

    private static void AssertIllegalReason(string expectedReason, Func<TableState> action)
    {
        var exception = Assert.Throws<IllegalActionException>(() => { _ = action(); });
        Assert.Equal(expectedReason, exception.Reason);
    }
}
