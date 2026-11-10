using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.Hands;
using Xunit;

namespace Wip.Tholdem.Tests;

public sealed class MultiwayHoldemEngineTests
{
    // ---------------------------------------------------------------- 1. two-seat equivalence

    [Fact]
    public void Start_GivenTwoSeats_ExpectedSameStateAsHeadsUpEngineForOneHundredSeeds()
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var general = HoldemHandEngine.Start(seed, 2);
            var headsUp = HeadsUpHandEngine.Start(seed);

            AssertSameState(headsUp, general, seed, -1);
        }
    }

    [Fact]
    public void Apply_GivenTwoSeatsAndTwentyRaiseWarActions_ExpectedIdenticalStatesAfterEveryAction()
    {
        var general = HoldemHandEngine.Start(42, 2);
        var headsUp = HeadsUpHandEngine.Start(42);

        for (var step = 0; step < 20; step++)
        {
            var legal = HeadsUpHandEngine.GetLegalActions(headsUp);
            Assert.NotEmpty(legal.Actions);

            var action = legal.CanRaise && legal.MaxRaiseTo >= legal.MinRaiseTo
                ? PokerAction.RaiseTo(legal.MinRaiseTo)
                : legal.CanCall
                    ? PokerAction.Call()
                    : PokerAction.Check();

            headsUp = HeadsUpHandEngine.Apply(headsUp, action);
            general = HoldemHandEngine.Apply(general, action);

            AssertSameState(headsUp, general, 42, step);
        }
    }

    private static void AssertSameState(TableState expected, TableState actual, ulong seed, int step)
    {
        var context = $"seed {seed} step {step}";
        Assert.True(expected.SeatCount == actual.SeatCount, context + ": seat count");
        Assert.True(expected.HandNumber == actual.HandNumber, context + ": hand number");
        Assert.True(expected.Street == actual.Street, context + ": street");
        Assert.True(expected.ButtonSeat == actual.ButtonSeat, context + ": button");
        Assert.True(expected.SmallBlindSeat == actual.SmallBlindSeat, context + ": small blind seat");
        Assert.True(expected.BigBlindSeat == actual.BigBlindSeat, context + ": big blind seat");
        Assert.True(expected.ActingSeat == actual.ActingSeat, context + ": acting seat");
        Assert.True(expected.Board.SequenceEqual(actual.Board), context + ": board");
        Assert.True(expected.Stacks.SequenceEqual(actual.Stacks), context + ": stacks");
        Assert.True(
            expected.StreetCommitted.SequenceEqual(actual.StreetCommitted),
            context + ": street commitments");
        Assert.True(
            expected.TotalCommitted.SequenceEqual(actual.TotalCommitted),
            context + ": total commitments");
        Assert.True(expected.Folded.SequenceEqual(actual.Folded), context + ": folded");
        Assert.True(expected.AllIn.SequenceEqual(actual.AllIn), context + ": all-in");
        Assert.True(expected.Pot == actual.Pot, context + ": pot");
        Assert.True(expected.CurrentBet == actual.CurrentBet, context + ": current bet");
        Assert.True(expected.LastRaiseSize == actual.LastRaiseSize, context + ": last raise size");
        Assert.True(expected.IsComplete == actual.IsComplete, context + ": complete");
        Assert.True(expected.ShowdownReached == actual.ShowdownReached, context + ": showdown");
        Assert.True(expected.Winners.SequenceEqual(actual.Winners), context + ": winners");
        Assert.True(expected.AwardedPot == actual.AwardedPot, context + ": awarded pot");
        Assert.True(expected.StartingStack == actual.StartingStack, context + ": starting stack");
        Assert.True(expected.SmallBlind == actual.SmallBlind, context + ": small blind");
        Assert.True(expected.BigBlind == actual.BigBlind, context + ": big blind");
        Assert.True(
            expected.Pots.Select(pot => (pot.Amount, Winners: pot.Winners.ToArray()))
                .SequenceEqual(actual.Pots.Select(pot => (pot.Amount, Winners: pot.Winners.ToArray()))),
            context + ": pots");
        Assert.True(expected.Seed == actual.Seed, context + ": seed");
        Assert.True(expected.Deck.SequenceEqual(actual.Deck), context + ": deck");
        Assert.True(
            expected.HoleCardsSeat0.SequenceEqual(actual.HoleCardsSeat0),
            context + ": seat 0 hole cards");
        Assert.True(
            expected.HoleCardsSeat1.SequenceEqual(actual.HoleCardsSeat1),
            context + ": seat 1 hole cards");
        Assert.True(expected.DeckPosition == actual.DeckPosition, context + ": deck position");
        Assert.True(expected.ActedMask == actual.ActedMask, context + ": acted mask");
        Assert.True(
            expected.ActionLog.SequenceEqual(actual.ActionLog),
            context + ": action log");
    }

    // ---------------------------------------------------------------- 2. blinds and action order

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Start_GivenSeatCount_ExpectedBlindsAndCompleteCheckCallActionOrder(int seats)
    {
        for (var button = 0; button < seats; button++)
        {
            var state = HoldemHandEngine.Start(1234, seats, buttonSeat: button);

            Assert.Equal(seats, state.SeatCount);
            Assert.Equal(button, state.ButtonSeat);
            Assert.Equal(button, state.SmallBlindSeat);
            Assert.Equal((button + 1) % seats, state.BigBlindSeat);
            Assert.Equal((button + 2) % seats, state.ActingSeat);
            Assert.Equal(SmallBlindAmount(), state.StreetCommitted[button]);
            Assert.Equal(BigBlindAmount(), state.StreetCommitted[(button + 1) % seats]);
            Assert.Equal(SmallBlindAmount() + BigBlindAmount(), state.Pot);

            var preflopOrder = new List<int>();
            while (state.Street == Street.Preflop)
            {
                preflopOrder.Add(state.ActingSeat);
                var legal = HoldemHandEngine.GetLegalActions(state);
                state = HoldemHandEngine.Apply(
                    state,
                    legal.CanCheck ? PokerAction.Check() : PokerAction.Call());
            }

            var expectedPreflop = Enumerable.Range(2, seats)
                .Select(offset => (button + offset) % seats)
                .ToArray();
            Assert.Equal(expectedPreflop, preflopOrder.ToArray());
            Assert.Equal(Street.Flop, state.Street);
            Assert.Equal((button + 1) % seats, state.ActingSeat);

            var flopOrder = new List<int>();
            while (state.Street == Street.Flop)
            {
                Assert.True(HoldemHandEngine.GetLegalActions(state).CanCheck);
                flopOrder.Add(state.ActingSeat);
                state = HoldemHandEngine.Apply(state, PokerAction.Check());
            }

            var expectedFlop = Enumerable.Range(1, seats)
                .Select(offset => (button + offset) % seats)
                .ToArray();
            Assert.Equal(expectedFlop, flopOrder.ToArray());
            Assert.Equal(Street.Turn, state.Street);
            Assert.Equal((button + 1) % seats, state.ActingSeat);
        }
    }

    [Fact]
    public void Start_GivenSixSeats_ExpectedSmallAndBigBlindRotateWithButton()
    {
        var observed = new List<(int Button, int SmallBlind, int BigBlind, int FirstToAct)>();
        for (var button = 0; button < 6; button++)
        {
            var state = HoldemHandEngine.Start(77, 6, buttonSeat: button);
            observed.Add((state.ButtonSeat, state.SmallBlindSeat, state.BigBlindSeat, state.ActingSeat));
        }

        Assert.Equal(
            new[]
            {
                (0, 0, 1, 2),
                (1, 1, 2, 3),
                (2, 2, 3, 4),
                (3, 3, 4, 5),
                (4, 4, 5, 0),
                (5, 5, 0, 1),
            },
            observed.ToArray());
    }

    private static long SmallBlindAmount() => 5;

    private static long BigBlindAmount() => 10;

    // ---------------------------------------------------------------- 3. per-seat legality

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void GetLegalActions_GivenEverySeatCount_ExpectedPerSeatCallAndRaiseShape(int seats)
    {
        for (var button = 0; button < seats; button++)
        {
            var state = HoldemHandEngine.Start(9876, seats, buttonSeat: button);
            while (state.Street == Street.Preflop)
            {
                var acting = state.ActingSeat;
                var legal = HoldemHandEngine.GetLegalActions(state);
                var expectedCall = Math.Max(0, state.CurrentBet - state.StreetCommitted[acting]);

                Assert.Equal(expectedCall, legal.CallAmount);
                Assert.Equal(expectedCall > 0, legal.CanFold);
                Assert.Equal(expectedCall == 0, legal.CanCheck);
                Assert.Equal(expectedCall > 0 && state.Stacks[acting] > 0, legal.CanCall);
                Assert.Equal(
                    state.CurrentBet == 0 ? state.BigBlind : state.CurrentBet + state.LastRaiseSize,
                    legal.MinRaiseTo);
                Assert.Equal(state.StreetCommitted[acting] + state.Stacks[acting], legal.MaxRaiseTo);
                Assert.Equal(state.Stacks[acting] > 0, legal.CanAllIn);
                Assert.Equal(legal.CanAllIn, legal.Actions.Any(action => action.Kind == ActionKind.AllIn));

                state = HoldemHandEngine.Apply(
                    state,
                    legal.CanCheck ? PokerAction.Check() : PokerAction.Call());
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Apply_GivenEverySeatCount_ExpectedFoldOnlyFacingBetAndCheckOnlyWithoutCall(int seats)
    {
        var facingBet = HoldemHandEngine.Start(321, seats, buttonSeat: seats - 1);
        var facingLegal = HoldemHandEngine.GetLegalActions(facingBet);
        Assert.True(facingLegal.CanFold);
        Assert.False(facingLegal.CanCheck);
        AssertIllegalReason(
            "check-facing-bet",
            () => HoldemHandEngine.Apply(facingBet, PokerAction.Check()));
        AssertIllegalReason(
            "fold-not-facing-bet",
            () => HoldemHandEngine.Apply(CheckDownToFlop(facingBet, seats), PokerAction.Fold()));
        AssertIllegalReason(
            "call-not-facing-bet",
            () => HoldemHandEngine.Apply(CheckDownToFlop(facingBet, seats), PokerAction.Call()));
        Assert.True(HoldemHandEngine.GetLegalActions(CheckDownToFlop(facingBet, seats)).CanCheck);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Apply_GivenShortStack_ExpectedBelowMinimumRaiseOnlyAsAllIn(int seats)
    {
        var state = HoldemHandEngine.Start(42, seats, startingStack: 18);
        var legal = HoldemHandEngine.GetLegalActions(state);

        Assert.False(legal.CanRaise);
        Assert.False(legal.CanBet);
        Assert.True(legal.CanAllIn);
        Assert.True(legal.CanCall);
        Assert.Equal(20L, legal.MinRaiseTo);
        Assert.Equal(18L, legal.MaxRaiseTo);

        AssertIllegalReason(
            "raise-below-min",
            () => HoldemHandEngine.Apply(state, PokerAction.RaiseTo(18)));

        var allIn = HoldemHandEngine.Apply(state, PokerAction.AllIn());
        Assert.Equal(18L, allIn.CurrentBet);
        Assert.Equal(10L, allIn.LastRaiseSize);
        Assert.Equal((long)seats * 18, allIn.Stacks.Sum() + allIn.Pot);

        if (!allIn.IsComplete)
        {
            var next = HoldemHandEngine.GetLegalActions(allIn);
            Assert.Equal(28L, next.MinRaiseTo);
        }
    }

    [Fact]
    public void Apply_GivenShortAllInRaise_ExpectedLastRaiseSizeNotReset()
    {
        var state = HoldemHandEngine.Start(42, 4, startingStack: 18);
        var raised = HoldemHandEngine.Apply(state, PokerAction.AllIn());

        Assert.Equal(18L, raised.CurrentBet);
        Assert.Equal(10L, raised.LastRaiseSize);

        var next = HoldemHandEngine.GetLegalActions(raised);
        Assert.Equal(28L, next.MinRaiseTo);
        Assert.Equal(18L, next.CallAmount);
        AssertIllegalReason(
            "raise-below-min",
            () => HoldemHandEngine.Apply(raised, PokerAction.RaiseTo(27)));
    }

    private static TableState CheckDownToFlop(TableState state, int seats)
    {
        while (state.Street == Street.Preflop)
        {
            var legal = HoldemHandEngine.GetLegalActions(state);
            state = HoldemHandEngine.Apply(
                state,
                legal.CanCheck ? PokerAction.Check() : PokerAction.Call());
        }

        Assert.Equal(Street.Flop, state.Street);
        Assert.Equal((state.ButtonSeat + 1) % seats, state.ActingSeat);
        return state;
    }

    private static void AssertIllegalReason(string expectedReason, Func<TableState> action)
    {
        var exception = Assert.Throws<IllegalActionException>(() => { _ = action(); });
        Assert.Equal(expectedReason, exception.Reason);
    }

    // ---------------------------------------------------------------- 4. per-seat starting stacks

    [Fact]
    public void Start_GivenPerSeatStartingStacks_ExpectedPerSeatStacksAndScalarIgnored()
    {
        var startingStacks = new long[] { 1000, 600, 300 };
        var state = HoldemHandEngine.Start(
            12345,
            3,
            handNumber: 1,
            startingStack: 7,
            smallBlind: 5,
            bigBlind: 10,
            buttonSeat: 0,
            startingStacks: startingStacks);

        Assert.Equal(startingStacks, state.StartingStacks.ToArray());
        Assert.Equal(1000L, state.StartingStack);
        Assert.Equal(new long[] { 995, 590, 300 }, state.Stacks.ToArray());
        Assert.Equal(new long[] { 5, 10, 0 }, state.StreetCommitted.ToArray());
        Assert.Equal(15L, state.Pot);
        Assert.Equal(1900L, state.StartingStacks.Sum());
    }

    [Fact]
    public void Start_GivenNullStartingStacks_ExpectedEverySeatToUseScalarStartingStack()
    {
        var state = HoldemHandEngine.Start(12345, 4);

        Assert.Equal(new long[] { 1000, 1000, 1000, 1000 }, state.StartingStacks.ToArray());
        Assert.Equal(1000L, state.StartingStack);
    }

    [Fact]
    public void Start_GivenInvalidStartingStacks_ExpectedSpecificArgumentException()
    {
        var countMismatch = Assert.Throws<ArgumentException>(() => HoldemHandEngine.Start(
            1,
            3,
            startingStacks: new long[] { 1000, 500 }));
        Assert.Equal("startingStacks", countMismatch.ParamName);
        Assert.Contains("3", countMismatch.Message);

        var belowBlind = Assert.Throws<ArgumentOutOfRangeException>(() => HoldemHandEngine.Start(
            1,
            3,
            startingStacks: new long[] { 1000, 500, 5 }));
        Assert.Equal("startingStacks", belowBlind.ParamName);
        Assert.Contains("big blind", belowBlind.Message);
    }

    // ---------------------------------------------------------------- 5. side pots through Start

    private static TableState StartWithStacks(ulong seed, long[] startingStacks, int buttonSeat = 0)
        => HoldemHandEngine.Start(
            seed,
            startingStacks.Length,
            handNumber: 1,
            startingStack: 1000, // ignored: the per-seat stacks below are authoritative
            smallBlind: 5,
            bigBlind: 10,
            buttonSeat: buttonSeat,
            startingStacks: startingStacks);

    private static TableState ApplyAllIn(TableState state) =>
        HoldemHandEngine.Apply(state, PokerAction.AllIn());

    [Fact]
    public void Start_GivenThreeSeatsWithStacks1000_600_300_ExpectedAllInLayers900_600_400()
    {
        var state = StartWithStacks(424242, [1000, 600, 300]);

        Assert.Equal(new long[] { 1000, 600, 300 }, state.StartingStacks.ToArray());
        Assert.Equal(2, state.ActingSeat);
        state = ApplyAllIn(state); // seat 2 commits its 300
        Assert.Equal(0, state.ActingSeat);
        state = ApplyAllIn(state); // seat 0 commits its 1000
        Assert.Equal(1, state.ActingSeat);
        state = ApplyAllIn(state); // seat 1 short-calls all-in for its 600

        Assert.True(state.IsComplete);
        Assert.True(state.ShowdownReached);
        Assert.Equal(new long[] { 1000, 600, 300 }, state.TotalCommitted.ToArray());

        // 300 x 3 = 900, 300 x 2 = 600, 400 x 1 = 400.
        Assert.Equal(new long[] { 900, 600, 400 }, state.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(1900L, state.AwardedPot);
        Assert.Equal(0L, state.Pot);
        Assert.Equal(1900L, state.Stacks.Sum());

        var ranks = Enumerable.Range(0, state.SeatCount)
            .Select(seat => HandEvaluator.Evaluate(
                MultiwayTestSupport.Combine(state.HoleCards[seat], state.Board)))
            .ToArray();
        var bestRank = ranks.Max();
        var expectedMainWinners = Enumerable.Range(0, state.SeatCount)
            .Where(seat => ranks[seat].CompareTo(bestRank) == 0)
            .ToArray();
        Assert.Equal(expectedMainWinners, state.Pots[0].Winners.ToArray());

        var independentPots = MultiwayTestSupport.BruteForcePots(state);
        Assert.Equal(3, independentPots.Count);
        for (var pot = 0; pot < independentPots.Count; pot++)
        {
            Assert.Equal(independentPots[pot].Winners, state.Pots[pot].Winners.ToArray());
        }

        Assert.Equal(MultiwayTestSupport.ExpectedFinalStacks(state), state.Stacks.ToArray());
    }

    [Fact]
    public void Start_GivenFoldedSmallBlindWithUnequalStacks_ExpectedDeadChipsStayAndSeatNeverWins()
    {
        var state = StartWithStacks(777, [1000, 600, 300]);

        state = ApplyAllIn(state);                                  // seat 2 commits its 300
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());  // seat 0 folds its 5
        state = ApplyAllIn(state);                                  // seat 1 commits its 600

        Assert.True(state.IsComplete);
        Assert.True(state.ShowdownReached);
        Assert.True(state.Folded[0]);
        Assert.Equal(5L, state.TotalCommitted[0]);
        Assert.Equal(new long[] { 15, 590, 300 }, state.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(905L, state.Pots.Sum(pot => pot.Amount));
        Assert.DoesNotContain(0, state.Winners);
        foreach (var pot in state.Pots)
        {
            Assert.DoesNotContain(0, pot.Winners);
        }

        Assert.Equal(MultiwayTestSupport.ExpectedFinalStacks(state), state.Stacks.ToArray());
        Assert.Equal(1900L, state.Stacks.Sum());
    }

    [Fact]
    public void Start_GivenFourSeatsAndHugeAllIn_ExpectedUncalledExcessReturnedInFinalStack()
    {
        var state = StartWithStacks(1600, [1000, 300, 200, 100]);

        state = ApplyAllIn(state); // seat 2 commits its 200
        state = ApplyAllIn(state); // seat 3 short-calls all-in for its 100
        state = ApplyAllIn(state); // seat 0 commits its 1000
        state = ApplyAllIn(state); // seat 1 short-calls all-in for its 300

        Assert.True(state.IsComplete);
        Assert.True(state.ShowdownReached);
        Assert.Equal(new long[] { 1000, 300, 200, 100 }, state.TotalCommitted.ToArray());
        Assert.Equal(new long[] { 400, 300, 200, 700 }, state.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(new[] { 0 }, state.Pots[3].Winners.ToArray());
        Assert.Equal(700L, state.Pots[3].Amount);
        Assert.Equal(1600L, state.Pots.Sum(pot => pot.Amount));
        Assert.Equal(1600L, state.StartingStacks.Sum());
        Assert.Equal(1600L, state.Stacks.Sum());

        Assert.Equal(MultiwayTestSupport.ExpectedFinalStacks(state), state.Stacks.ToArray());
        Assert.True(
            state.Stacks[0] >= 700L,
            "the 700-chip uncalled excess must be returned into seat 0's final stack");
    }

    [Fact]
    public void Start_GivenOddChipSplitAtMainPot_ExpectedOddChipToFirstEligibleClockwiseFromButton()
    {
        var seed = FindBoardPlaysSeed();
        var state = StartWithStacks(seed, [1000, 600, 300]);

        state = ApplyAllIn(state);                                  // seat 2 commits its 300
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());  // seat 0's 5 stays as dead money
        state = ApplyAllIn(state);                                  // seat 1 commits its 600

        Assert.True(state.ShowdownReached);
        Assert.Equal(new long[] { 15, 590, 300 }, state.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(new[] { 1, 2 }, state.Pots[0].Winners.ToArray());
        Assert.Equal(new[] { 1, 2 }, state.Pots[1].Winners.ToArray());
        Assert.Equal(new[] { 1 }, state.Pots[2].Winners.ToArray());

        // The board is four of a kind plus an ace, so seats 1 and 2 tie at both contested layers.
        // Main layer 15 / 2 = 7 remainder 1: seat 1 is the first eligible seat clockwise from the
        // button (button 0), so it gets 8 and seat 2 gets 7. Side layer 590 / 2 = 295 each; the top
        // 300-chip layer is the uncalled excess returned to seat 1.
        Assert.Equal(new long[] { 995, 603, 302 }, state.Stacks.ToArray());
        Assert.Equal(1900L, state.Stacks.Sum());
    }

    [Fact]
    public void Start_GivenSixSeatsWithTwoAllInLevels_ExpectedLayeredAwards()
    {
        var state = StartWithStacks(4100, [1000, 1000, 600, 600, 600, 600]);

        for (var allIn = 0; allIn < 4; allIn++)
        {
            state = ApplyAllIn(state); // seats 2, 3, 4 and 5 each commit 600
        }

        state = ApplyAllIn(state); // seat 0 commits 1000 as a short all-in raise
        state = ApplyAllIn(state); // seat 1 short-calls all-in for its 1000

        Assert.True(state.IsComplete);
        Assert.True(state.ShowdownReached);
        Assert.Equal(
            new long[] { 1000, 1000, 600, 600, 600, 600 },
            state.TotalCommitted.ToArray());
        Assert.Equal(new long[] { 3600, 800 }, state.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(4400L, state.AwardedPot);
        Assert.Equal(state.StartingStacks.Sum(), state.Stacks.Sum());
        Assert.Equal(MultiwayTestSupport.ExpectedFinalStacks(state), state.Stacks.ToArray());
    }

    [Fact]
    public void Start_GivenConsecutiveLayersWithSameWinners_ExpectedSeparatePotAwards()
    {
        var state = StartWithStacks(1010, [1000, 600, 300], buttonSeat: 2);

        // Button seat 2 posts 5 and folds; the other two seats check down to showdown with total
        // commitments 10 and 10, leaving the folded 5 as dead money in the lower layer.
        Assert.Equal(1, state.ActingSeat);
        state = HoldemHandEngine.Apply(state, PokerAction.Call());
        Assert.Equal(2, state.ActingSeat);
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());
        Assert.Equal(0, state.ActingSeat);
        state = HoldemHandEngine.Apply(state, PokerAction.Check());

        while (!state.IsComplete)
        {
            Assert.True(HoldemHandEngine.GetLegalActions(state).CanCheck);
            state = HoldemHandEngine.Apply(state, PokerAction.Check());
        }

        Assert.True(state.ShowdownReached);
        Assert.Equal(new long[] { 10, 10, 5 }, state.TotalCommitted.ToArray());

        // Layers are deliberately not merged: 5 x 3 = 15 and (10 - 5) x 2 = 10.
        Assert.Equal(new long[] { 15, 10 }, state.Pots.Select(pot => pot.Amount).ToArray());
        Assert.Equal(state.Pots[0].Winners.ToArray(), state.Pots[1].Winners.ToArray());
        Assert.Equal(MultiwayTestSupport.ExpectedFinalStacks(state), state.Stacks.ToArray());
        Assert.Equal(1900L, state.Stacks.Sum());
    }

    /// <summary>
    /// Finds a seed whose five-card board for a three-seat table is four of a kind plus an ace
    /// (the quad rank therefore cannot be an ace). Such a board is the best hand for every player,
    /// which forces a tie without crafting a state or a private deck.
    /// </summary>
    private static ulong FindBoardPlaysSeed()
    {
        for (ulong seed = 1; seed <= 1_000_000; seed++)
        {
            var deck = SeededDeck.Create(seed);
            var board = new[] { deck[6], deck[7], deck[8], deck[9], deck[10] };
            var quads = board.GroupBy(card => card.Rank).FirstOrDefault(group => group.Count() == 4);
            if (quads is not null
                && quads.Key != Rank.Ace
                && board.Any(card => card.Rank == Rank.Ace))
            {
                return seed;
            }
        }

        throw new InvalidOperationException(
            "No seed with a three-seat board of four of a kind plus an ace was found.");
    }

    // ---------------------------------------------------------------- 6. property test

    [Fact]
    public void Apply_GivenThreeHundredRandomMultiwayHandsWithPerSeatStacks_ExpectedConservationTerminationAndMatchingBruteForcePots()
    {
        var multiPotHands = 0;
        var foldHands = 0;
        for (var index = 0; index < 300; index++)
        {
            var seats = 2 + (index % 5);
            var seed = 1000UL + (ulong)index;
            var startingStacks = new long[seats];
            for (var seat = 0; seat < seats; seat++)
            {
                startingStacks[seat] = 100L + (100L * ((index + (seat * 3)) % 9));
            }

            var state = HoldemHandEngine.Start(
                seed,
                seats,
                1,
                1000, // ignored: the per-seat starting stacks below are authoritative
                MultiwayTestSupport.SmallBlind,
                MultiwayTestSupport.BigBlind,
                index % seats,
                startingStacks);
            var random = new Xorshift64Star(seed ^ 0x9E3779B97F4A7C15UL);
            var total = startingStacks.Sum();
            var actions = 0;

            Assert.Equal(startingStacks, state.StartingStacks.ToArray());
            Assert.Equal(startingStacks[0], state.StartingStack);
            Assert.Equal(total, state.Stacks.Sum() + state.Pot);
            while (!state.IsComplete)
            {
                var legal = HoldemHandEngine.GetLegalActions(state);
                Assert.NotEmpty(legal.Actions);
                state = HoldemHandEngine.Apply(state, MultiwayTestSupport.Choose(legal, ref random));
                actions++;
                Assert.True(actions <= 500, $"seed {seed} seats {seats} exceeded 500 actions");
                Assert.Equal(total, state.Stacks.Sum() + state.Pot);
            }

            Assert.Equal(0L, state.Pot);
            Assert.Equal(total, state.Stacks.Sum());

            var expectedPots = MultiwayTestSupport.BruteForcePots(state);
            Assert.Equal(
                expectedPots.Select(pot => pot.Amount).ToArray(),
                state.Pots.Select(pot => pot.Amount).ToArray());
            for (var potIndex = 0; potIndex < expectedPots.Count; potIndex++)
            {
                Assert.Equal(expectedPots[potIndex].Winners, state.Pots[potIndex].Winners.ToArray());
            }

            Assert.Equal(state.TotalCommitted.Sum(), state.Pots.Sum(pot => pot.Amount));
            Assert.Equal(state.AwardedPot, state.Pots.Sum(pot => pot.Amount));

            var awards = MultiwayTestSupport.AwardTotals(state);
            for (var seat = 0; seat < seats; seat++)
            {
                Assert.Equal(
                    startingStacks[seat] - state.TotalCommitted[seat] + awards[seat],
                    state.Stacks[seat]);
            }

            Assert.Equal(MultiwayTestSupport.ExpectedFinalStacks(state), state.Stacks.ToArray());

            if (state.Pots.Count > 1)
            {
                multiPotHands++;
            }

            if (!state.ShowdownReached)
            {
                foldHands++;
            }
        }

        Assert.True(multiPotHands > 0, "the property hands never produced layered side pots");
        Assert.True(foldHands > 0, "the property hands never ended by fold");
    }

    [Fact]
    public void Apply_GivenMultiwayRandomHands_ExpectedShowdownsAndFoldsBothOccur()
    {
        var showdowns = 0;
        var folds = 0;
        for (var index = 0; index < 60; index++)
        {
            var state = MultiwayTestSupport.PlayRandom(2000UL + (ulong)index, 2 + (index % 5));
            if (state.ShowdownReached)
            {
                showdowns++;
                Assert.True(state.Pots.Sum(pot => pot.Amount) > 0);
            }
            else
            {
                folds++;
                Assert.Single(state.Winners);
                Assert.Single(state.Pots);
            }
        }

        Assert.True(showdowns > 0, "no random hand reached showdown");
        Assert.True(folds > 0, "no random hand ended by fold");
    }

    [Fact]
    public void GetLegalActions_GivenNonZeroAmountOnNonBettingAction_ExpectedAmountNotAllowed()
    {
        var state = HoldemHandEngine.Start(42, 3);
        AssertIllegalReason(
            "amount-not-allowed",
            () => HoldemHandEngine.Apply(state, new PokerAction(ActionKind.Fold, 5)));
        AssertIllegalReason(
            "amount-not-allowed",
            () => HoldemHandEngine.Apply(state, new PokerAction(ActionKind.Call, 5)));
        AssertIllegalReason(
            "amount-not-allowed",
            () => HoldemHandEngine.Apply(state, new PokerAction(ActionKind.AllIn, 5)));
        AssertIllegalReason(
            "amount-not-allowed",
            () => HoldemHandEngine.Apply(state, new PokerAction((ActionKind)99, 5)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Start_GivenUnsupportedSeatCount_ExpectedArgumentOutOfRange(int seats)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HoldemHandEngine.Start(1, seats));
    }

    [Fact]
    public void Start_GivenButtonOutsideTable_ExpectedArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HoldemHandEngine.Start(1, 4, buttonSeat: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => HoldemHandEngine.Start(1, 4, buttonSeat: -1));
    }

    [Fact]
    public void Start_GivenThreeSeatsAndFoldedSeat_ExpectedBoardPlaysForRemainingSeatsAndFoldedSeatNeverWins()
    {
        var seed = FindBoardPlaysSeed();
        var state = StartWithStacks(seed, [1000, 1000, 1000]);

        Assert.Equal(2, state.ActingSeat);
        state = ApplyAllIn(state);                                  // seat 2 commits 1000
        Assert.Equal(0, state.ActingSeat);
        state = ApplyAllIn(state);                                  // seat 0 commits 1000
        Assert.Equal(1, state.ActingSeat);
        state = HoldemHandEngine.Apply(state, PokerAction.Fold());  // seat 1's big blind is dead

        Assert.True(state.ShowdownReached);
        Assert.True(state.Folded[1]);

        var boardRank = HandEvaluator.Evaluate(state.Board);
        Assert.Equal(
            boardRank,
            HandEvaluator.Evaluate(MultiwayTestSupport.Combine(state.HoleCards[0], state.Board)));
        Assert.Equal(
            boardRank,
            HandEvaluator.Evaluate(MultiwayTestSupport.Combine(state.HoleCards[2], state.Board)));

        // The board plays for both remaining seats; the folded seat's 10 chips stay in the lower
        // 30-chip layer but seat 1 never wins any layer.
        Assert.Equal(new[] { 0, 2 }, state.Winners.ToArray());
        Assert.Equal(new long[] { 30, 1980 }, state.Pots.Select(pot => pot.Amount).ToArray());
        foreach (var pot in state.Pots)
        {
            Assert.Equal(new[] { 0, 2 }, pot.Winners.ToArray());
            Assert.DoesNotContain(1, pot.Winners);
        }

        Assert.Equal(new long[] { 1005, 990, 1005 }, state.Stacks.ToArray());
        Assert.Equal(3000L, state.Stacks.Sum());
    }
}
