using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Engine;

/// <summary>
/// The deterministic no-limit Texas Hold'em hand engine for two to six seats. State transitions are
/// pure: <see cref="Apply"/> never mutates its input and every returned state satisfies
/// <c>sum(Stacks) + Pot == sum(StartingStacks)</c>.
/// </summary>
/// <remarks>
/// For two seats the engine reproduces the recorded heads-up behaviour exactly: the button posts the
/// small blind, the other seat posts the big blind, the button acts first preflop and the other seat
/// acts first from the flop on. <see cref="HeadsUpHandEngine"/> is a thin wrapper over this class.
/// </remarks>
public static class HoldemHandEngine
{
    public const int MinimumSeats = 2;

    public const int MaximumSeats = 6;

    private static readonly LegalActionSet NoActions = new(
        CanFold: false,
        CanCheck: false,
        CanCall: false,
        CallAmount: 0,
        CanBet: false,
        CanRaise: false,
        MinRaiseTo: 0,
        MaxRaiseTo: 0,
        CanAllIn: false,
        Actions: Array.Empty<PokerAction>());

    /// <summary>
    /// Starts a hand. When <paramref name="startingStacks"/> is null every seat starts with
    /// <paramref name="startingStack"/>; when it is supplied it must hold exactly one value per
    /// seat, every value must cover the big blind, and <paramref name="startingStack"/> is ignored.
    /// </summary>
    public static TableState Start(
        ulong seed,
        int seats,
        int handNumber = 1,
        long startingStack = 1000,
        long smallBlind = 5,
        long bigBlind = 10,
        int buttonSeat = 0,
        IReadOnlyList<long>? startingStacks = null)
    {
        if (seats is < MinimumSeats or > MaximumSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seats),
                seats,
                $"A table must have between {MinimumSeats} and {MaximumSeats} seats.");
        }

        if (handNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(handNumber), handNumber, "A hand number must be positive.");
        }

        if (smallBlind <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(smallBlind), smallBlind, "The small blind must be positive.");
        }

        if (bigBlind <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bigBlind), bigBlind, "The big blind must be positive.");
        }

        if (smallBlind > bigBlind)
        {
            throw new ArgumentOutOfRangeException(nameof(smallBlind), smallBlind, "The small blind cannot exceed the big blind.");
        }

        long[] stacks;
        long[] perSeatStartingStacks;
        if (startingStacks is null)
        {
            if (startingStack <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startingStack), startingStack, "A starting stack must be positive.");
            }

            if (startingStack < bigBlind)
            {
                throw new ArgumentOutOfRangeException(nameof(startingStack), startingStack, "A starting stack must cover the big blind.");
            }

            stacks = new long[seats];
            Array.Fill(stacks, startingStack);
            perSeatStartingStacks = (long[])stacks.Clone();
        }
        else
        {
            if (startingStacks.Count != seats)
            {
                throw new ArgumentException(
                    $"The starting-stack list must hold exactly one value per seat ({seats}); "
                    + $"received {startingStacks.Count}.",
                    nameof(startingStacks));
            }

            stacks = new long[seats];
            for (var seat = 0; seat < seats; seat++)
            {
                var value = startingStacks[seat];
                if (value < bigBlind)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(startingStacks),
                        value,
                        $"The starting stack {value} at seat {seat} must cover the big blind {bigBlind}.");
                }

                stacks[seat] = value;
            }

            perSeatStartingStacks = (long[])stacks.Clone();
        }

        if (buttonSeat < 0 || buttonSeat >= seats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buttonSeat),
                buttonSeat,
                $"The button seat must be between 0 and {seats - 1}.");
        }

        var bigBlindSeat = (buttonSeat + 1) % seats;
        var deck = SeededDeck.Create(seed);
        var committed = new long[seats];
        var total = new long[seats];
        var folded = new bool[seats];
        var holeCards = new IReadOnlyList<Card>[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            holeCards[seat] = new[] { deck[seat], deck[seats + seat] };
        }

        stacks[buttonSeat] -= smallBlind;
        committed[buttonSeat] = smallBlind;
        total[buttonSeat] = smallBlind;
        stacks[bigBlindSeat] -= bigBlind;
        committed[bigBlindSeat] = bigBlind;
        total[bigBlindSeat] = bigBlind;

        var allIn = new bool[seats];
        for (var seat = 0; seat < seats; seat++)
        {
            allIn[seat] = stacks[seat] == 0;
        }

        var state = new TableState
        {
            SeatCount = seats,
            HandNumber = handNumber,
            Street = Street.Preflop,
            ButtonSeat = buttonSeat,
            SmallBlindSeat = buttonSeat,
            BigBlindSeat = bigBlindSeat,
            ActingSeat = -1,
            Board = Array.Empty<Card>(),
            Stacks = stacks,
            StreetCommitted = committed,
            TotalCommitted = total,
            Folded = folded,
            AllIn = allIn,
            Pot = smallBlind + bigBlind,
            CurrentBet = bigBlind,
            LastRaiseSize = bigBlind,
            IsComplete = false,
            ShowdownReached = false,
            Winners = Array.Empty<int>(),
            Pots = Array.Empty<PotAward>(),
            StartingStack = perSeatStartingStacks[0],
            StartingStacks = perSeatStartingStacks,
            SmallBlind = smallBlind,
            BigBlind = bigBlind,
            AwardedPot = 0,
            Seed = seed,
            Deck = deck,
            HoleCards = holeCards,
            DeckPosition = seats * 2,
            ActedMask = 0,
            ActionLog = Array.Empty<ActionLogEntry>(),
        };

        var firstToAct = FindNextActiveSeat(state, bigBlindSeat);
        return firstToAct < 0 ? RunOutBoard(state) : state with { ActingSeat = firstToAct };
    }

    public static LegalActionSet GetLegalActions(TableState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsComplete)
        {
            return NoActions;
        }

        var seat = state.ActingSeat;
        if (seat < 0 || seat >= state.SeatCount)
        {
            throw Fail("invalid-state", $"Acting seat {seat} is outside the table.");
        }

        if (state.Folded[seat] || state.AllIn[seat])
        {
            throw Fail("invalid-state", $"Acting seat {seat} cannot act.");
        }

        var stack = state.Stacks[seat];
        var committed = state.StreetCommitted[seat];
        var callAmount = Math.Max(0L, state.CurrentBet - committed);
        var canFold = callAmount > 0;
        var canCheck = callAmount == 0;
        var canCall = callAmount > 0 && stack > 0;
        var minRaiseTo = state.CurrentBet == 0 ? state.BigBlind : state.CurrentBet + state.LastRaiseSize;
        var maxRaiseTo = committed + stack;
        var canAllIn = stack > 0;
        var canBet = canCheck && state.CurrentBet == 0 && stack > 0 && maxRaiseTo >= minRaiseTo;
        var canRaise = state.CurrentBet > 0 && stack > 0 && maxRaiseTo >= minRaiseTo;

        var actions = new List<PokerAction>(6);
        if (canFold)
        {
            actions.Add(PokerAction.Fold());
        }

        if (canCheck)
        {
            actions.Add(PokerAction.Check());
        }

        if (canCall)
        {
            actions.Add(PokerAction.Call());
        }

        if (canBet)
        {
            actions.Add(PokerAction.BetTo(minRaiseTo));
        }

        if (canRaise)
        {
            actions.Add(PokerAction.RaiseTo(minRaiseTo));
        }

        if (canAllIn)
        {
            actions.Add(PokerAction.AllIn());
        }

        return new LegalActionSet(
            canFold,
            canCheck,
            canCall,
            callAmount,
            canBet,
            canRaise,
            minRaiseTo,
            maxRaiseTo,
            canAllIn,
            actions.ToArray());
    }

    public static TableState Apply(TableState state, PokerAction action)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsComplete)
        {
            throw Fail("hand-complete", "The hand is complete; no further action is legal.");
        }

        if (action.Kind is not (ActionKind.Bet or ActionKind.Raise) && action.Amount != 0)
        {
            throw Fail("amount-not-allowed", $"Action {action.Kind} must not carry an amount.");
        }

        if (action.Kind is (ActionKind.Bet or ActionKind.Raise) && action.Amount < 0)
        {
            throw Fail("amount-not-allowed", "A bet or raise amount must not be negative.");
        }

        var seat = state.ActingSeat;
        if (seat < 0 || seat >= state.SeatCount)
        {
            throw Fail("invalid-state", $"Acting seat {seat} is outside the table.");
        }

        if (state.Folded[seat] || state.AllIn[seat])
        {
            throw Fail("invalid-state", $"Acting seat {seat} cannot act.");
        }

        var legal = GetLegalActions(state);

        return action.Kind switch
        {
            ActionKind.Fold => ApplyFold(state, seat, legal),
            ActionKind.Check => ApplyCheck(state, seat, legal),
            ActionKind.Call => ApplyCall(state, seat, legal),
            ActionKind.Bet => ApplyBetOrRaise(state, seat, legal, action, isBet: true),
            ActionKind.Raise => ApplyBetOrRaise(state, seat, legal, action, isBet: false),
            ActionKind.AllIn => ApplyAllIn(state, seat),
            _ => throw Fail("unknown-action", $"Action kind {(int)action.Kind} is not defined."),
        };
    }

    public static TableState Replay(HandRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.SeatCount is < MinimumSeats or > MaximumSeats)
        {
            throw new InvalidOperationException(
                $"The recorded seat count {record.SeatCount} is outside {MinimumSeats}..{MaximumSeats}.");
        }

        if (record.StartingStacks.Count != record.SeatCount)
        {
            throw new InvalidOperationException(
                $"The recorded starting stacks cover {record.StartingStacks.Count} seats, "
                + $"expected {record.SeatCount}.");
        }

        var state = Start(
            record.Seed,
            record.SeatCount,
            record.HandNumber,
            record.StartingStack,
            record.SmallBlind,
            record.BigBlind,
            record.ButtonSeat,
            record.StartingStacks);

        for (var index = 0; index < record.Actions.Count; index++)
        {
            var recorded = record.Actions[index];
            if (state.IsComplete)
            {
                throw new InvalidOperationException(
                    $"Replay found an action after the hand completed at action {index + 1}.");
            }

            if ((int)state.Street != recorded.Street)
            {
                throw new InvalidOperationException(
                    $"Replayed street {state.Street} does not match recorded street {recorded.Street} at action {index + 1}.");
            }

            if (state.ActingSeat != recorded.Seat)
            {
                throw new InvalidOperationException(
                    $"Replayed acting seat {state.ActingSeat} does not match recorded seat {recorded.Seat} at action {index + 1}.");
            }

            if (recorded.Kind is not ("Bet" or "Raise") && recorded.Amount != 0)
            {
                throw new InvalidOperationException(
                    $"Recorded action '{recorded.Kind}' must not carry an amount.");
            }

            state = Apply(state, ToPokerAction(recorded));

            if (state.Pot != recorded.PotAfter)
            {
                throw new InvalidOperationException(
                    $"Replayed pot {state.Pot} does not match recorded pot {recorded.PotAfter} after action {index + 1}.");
            }
        }

        if (!state.Board.SequenceEqual(record.Board))
        {
            throw new InvalidOperationException("Replayed board does not match the recorded board.");
        }

        if (record.HoleCards.Count != record.SeatCount)
        {
            throw new InvalidOperationException(
                $"Recorded hole cards cover {record.HoleCards.Count} seats, expected {record.SeatCount}.");
        }

        for (var seat = 0; seat < record.SeatCount; seat++)
        {
            if (!state.HoleCards[seat].SequenceEqual(record.HoleCards[seat]))
            {
                throw new InvalidOperationException(
                    $"Replayed hole cards for seat {seat} do not match the recorded hole cards.");
            }
        }

        if (!state.Stacks.SequenceEqual(record.FinalStacks))
        {
            throw new InvalidOperationException("Replayed final stacks do not match the recorded final stacks.");
        }

        if (!state.Winners.SequenceEqual(record.Winners)
            || state.ShowdownReached != record.Showdown
            || state.AwardedPot != record.PotAwarded)
        {
            throw new InvalidOperationException("Replayed result does not match the recorded result.");
        }

        if (record.SeatCount > 2)
        {
            if (record.Pots.Count != state.Pots.Count)
            {
                throw new InvalidOperationException("Replayed pot layers do not match the recorded pot layers.");
            }

            for (var pot = 0; pot < record.Pots.Count; pot++)
            {
                if (record.Pots[pot].Amount != state.Pots[pot].Amount
                    || !record.Pots[pot].Winners.SequenceEqual(state.Pots[pot].Winners))
                {
                    throw new InvalidOperationException(
                        $"Replayed pot layer {pot} does not match the recorded pot layer.");
                }
            }
        }

        return state;
    }

    /// <summary>
    /// Resolves the pot layers and awards them for a complete five-card board. Called by
    /// <see cref="RunOutBoard"/>; it stays internal so the hidden deck and hole cards remain
    /// outside the production surface.
    /// </summary>
    internal static TableState ResolveShowdown(TableState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Board.Count != 5)
        {
            throw Fail("invalid-state", "A showdown requires a complete five-card board.");
        }

        var levels = state.TotalCommitted
            .Where(static committed => committed > 0)
            .Distinct()
            .OrderBy(static level => level)
            .ToArray();
        var stacks = state.Stacks.ToArray();
        var pots = new List<PotAward>();
        var winners = new SortedSet<int>();
        var awarded = 0L;
        var previous = 0L;

        foreach (var level in levels)
        {
            var contributors = state.TotalCommitted.Count(committed => committed >= level);
            var amount = (level - previous) * contributors;
            var eligible = Enumerable.Range(0, state.SeatCount)
                .Where(seat => !state.Folded[seat] && state.TotalCommitted[seat] >= level)
                .ToArray();
            if (eligible.Length == 0)
            {
                throw Fail("invalid-state", $"No seat is eligible for the pot layer at contribution {level}.");
            }

            int[] layerWinners;
            if (eligible.Length == 1)
            {
                layerWinners = eligible;
            }
            else
            {
                var ranks = new HandRank[eligible.Length];
                for (var index = 0; index < eligible.Length; index++)
                {
                    ranks[index] = HandEvaluator.Evaluate(Combine(state.HoleCards[eligible[index]], state.Board));
                }

                var best = ranks[0];
                for (var index = 1; index < ranks.Length; index++)
                {
                    if (ranks[index].CompareTo(best) > 0)
                    {
                        best = ranks[index];
                    }
                }

                layerWinners = eligible
                    .Where((_, index) => ranks[index].CompareTo(best) == 0)
                    .ToArray();
            }

            var baseAward = amount / layerWinners.Length;
            var remainder = (int)(amount % layerWinners.Length);
            var awards = new long[state.SeatCount];
            foreach (var seat in layerWinners)
            {
                awards[seat] += baseAward;
            }

            foreach (var seat in ClockwiseFromButton(state, layerWinners).Take(remainder))
            {
                awards[seat]++;
            }

            for (var seat = 0; seat < state.SeatCount; seat++)
            {
                stacks[seat] += awards[seat];
            }

            awarded += amount;
            foreach (var seat in layerWinners)
            {
                winners.Add(seat);
            }

            pots.Add(new PotAward(amount, layerWinners));
            previous = level;
        }

        return state with
        {
            Stacks = stacks,
            Street = Street.Complete,
            StreetCommitted = new long[state.SeatCount],
            Pot = 0,
            CurrentBet = 0,
            LastRaiseSize = 0,
            ActingSeat = -1,
            IsComplete = true,
            ShowdownReached = true,
            Winners = winners.ToArray(),
            Pots = pots,
            AwardedPot = awarded,
        };
    }

    private static TableState ApplyFold(TableState state, int seat, LegalActionSet legal)
    {
        if (!legal.CanFold)
        {
            throw Fail("fold-not-facing-bet", "A player may fold only when facing a bet.");
        }

        var folded = state.Folded.ToArray();
        folded[seat] = true;
        var updated = state with { Folded = folded };

        var activeSeats = Enumerable.Range(0, state.SeatCount)
            .Where(candidate => !folded[candidate])
            .ToArray();
        var result = activeSeats.Length == 1
            ? AwardFold(updated, activeSeats[0])
            : ContinueAfterAction(updated, seat);
        return result with { ActionLog = AppendAction(state, seat, PokerAction.Fold(), result.Pot) };
    }

    private static TableState ApplyCheck(TableState state, int seat, LegalActionSet legal)
    {
        if (!legal.CanCheck)
        {
            throw Fail("check-facing-bet", "A player may check only when there is nothing to call.");
        }

        return AdvanceAfterChips(
            state,
            seat,
            PokerAction.Check(),
            chips: 0,
            newCurrentBet: state.CurrentBet,
            newLastRaiseSize: state.LastRaiseSize,
            aggressive: false);
    }

    private static TableState ApplyCall(TableState state, int seat, LegalActionSet legal)
    {
        if (!legal.CanCall)
        {
            throw Fail("call-not-facing-bet", "A player may call only when facing a bet.");
        }

        var chips = Math.Min(legal.CallAmount, state.Stacks[seat]);
        return AdvanceAfterChips(
            state,
            seat,
            PokerAction.Call(),
            chips,
            state.CurrentBet,
            state.LastRaiseSize,
            aggressive: false);
    }

    private static TableState ApplyBetOrRaise(
        TableState state,
        int seat,
        LegalActionSet legal,
        PokerAction action,
        bool isBet)
    {
        if (isBet && state.CurrentBet != 0)
        {
            throw Fail("bet-facing-bet", "A bet is not legal when a bet already exists; use a raise.");
        }

        if (!isBet && state.CurrentBet == 0)
        {
            throw Fail("raise-not-facing-bet", "A raise is not legal when there is no bet; use a bet.");
        }

        if (action.Amount < legal.MinRaiseTo)
        {
            throw Fail(
                "raise-below-min",
                $"A {(isBet ? "bet" : "raise")} to {action.Amount} is below the minimum {legal.MinRaiseTo}.");
        }

        if (action.Amount > legal.MaxRaiseTo)
        {
            throw Fail(
                "raise-above-stack",
                $"A {(isBet ? "bet" : "raise")} to {action.Amount} exceeds the maximum {legal.MaxRaiseTo}.");
        }

        var chips = action.Amount - state.StreetCommitted[seat];
        if (chips <= 0)
        {
            throw Fail("raise-below-min", "The bet or raise must increase the street commitment.");
        }

        return AdvanceAfterChips(
            state,
            seat,
            action,
            chips,
            newCurrentBet: action.Amount,
            newLastRaiseSize: action.Amount - state.CurrentBet,
            aggressive: true);
    }

    private static TableState ApplyAllIn(TableState state, int seat)
    {
        var stack = state.Stacks[seat];
        if (stack <= 0)
        {
            throw Fail("all-in-not-available", "The acting player has no chips behind.");
        }

        var total = state.StreetCommitted[seat] + stack;
        var oldCurrent = state.CurrentBet;

        if (total <= oldCurrent)
        {
            return AdvanceAfterChips(
                state,
                seat,
                PokerAction.AllIn(),
                stack,
                oldCurrent,
                state.LastRaiseSize,
                aggressive: false);
        }

        var minimumRaiseTo = oldCurrent == 0 ? state.BigBlind : oldCurrent + state.LastRaiseSize;
        var isFullRaise = total >= minimumRaiseTo;
        var newLastRaiseSize = isFullRaise ? total - oldCurrent : state.LastRaiseSize;

        return AdvanceAfterChips(
            state,
            seat,
            PokerAction.AllIn(),
            stack,
            newCurrentBet: total,
            newLastRaiseSize,
            aggressive: true);
    }

    private static TableState AdvanceAfterChips(
        TableState state,
        int seat,
        PokerAction action,
        long chips,
        long newCurrentBet,
        long newLastRaiseSize,
        bool aggressive)
    {
        var stacks = state.Stacks.ToArray();
        var committed = state.StreetCommitted.ToArray();
        var total = state.TotalCommitted.ToArray();
        var allIn = state.AllIn.ToArray();
        stacks[seat] -= chips;
        committed[seat] += chips;
        total[seat] += chips;
        allIn[seat] = stacks[seat] == 0;
        var pot = state.Pot + chips;
        var actedMask = aggressive ? 1 << seat : state.ActedMask | (1 << seat);

        var updated = state with
        {
            Stacks = stacks,
            StreetCommitted = committed,
            TotalCommitted = total,
            AllIn = allIn,
            Pot = pot,
            CurrentBet = newCurrentBet,
            LastRaiseSize = newLastRaiseSize,
            ActedMask = actedMask,
        };

        var result = ContinueAfterAction(updated, seat);
        return result with { ActionLog = AppendAction(state, seat, action, result.Pot) };
    }

    /// <summary>
    /// Moves the hand forward after an action has been applied: either closes the betting round and
    /// advances the street (or runs out the board), or selects the next seat that must act.
    /// </summary>
    private static TableState ContinueAfterAction(TableState state, int lastSeat)
    {
        if (!IsRoundComplete(state))
        {
            var next = NextSeatToAct(state, lastSeat);
            if (next < 0)
            {
                throw Fail("invalid-state", "The betting round is neither complete nor has a next actor.");
            }

            return state with { ActingSeat = next };
        }

        var chipCount = 0;
        for (var seat = 0; seat < state.SeatCount; seat++)
        {
            if (!state.Folded[seat] && !state.AllIn[seat])
            {
                chipCount++;
            }
        }

        if (chipCount <= 1 || state.Street == Street.River)
        {
            return RunOutBoard(state);
        }

        return AdvanceStreet(state);
    }

    private static bool IsRoundComplete(TableState state)
    {
        var anyActive = false;
        for (var seat = 0; seat < state.SeatCount; seat++)
        {
            if (state.Folded[seat])
            {
                continue;
            }

            anyActive = true;
            if (state.AllIn[seat])
            {
                continue;
            }

            if (!HasActed(state.ActedMask, seat) || state.StreetCommitted[seat] != state.CurrentBet)
            {
                return false;
            }
        }

        return anyActive;
    }

    private static int NextSeatToAct(TableState state, int fromSeat)
    {
        for (var offset = 1; offset <= state.SeatCount; offset++)
        {
            var seat = (fromSeat + offset) % state.SeatCount;
            if (state.Folded[seat] || state.AllIn[seat])
            {
                continue;
            }

            if (!HasActed(state.ActedMask, seat) || state.StreetCommitted[seat] != state.CurrentBet)
            {
                return seat;
            }
        }

        return -1;
    }

    private static int FindNextActiveSeat(TableState state, int fromSeat)
    {
        for (var offset = 1; offset <= state.SeatCount; offset++)
        {
            var seat = (fromSeat + offset) % state.SeatCount;
            if (!state.Folded[seat] && !state.AllIn[seat])
            {
                return seat;
            }
        }

        return -1;
    }

    private static TableState AdvanceStreet(TableState state)
    {
        var next = state.Street switch
        {
            Street.Preflop => Street.Flop,
            Street.Flop => Street.Turn,
            Street.Turn => Street.River,
            _ => throw Fail("invalid-state", $"Street {state.Street} cannot be advanced."),
        };

        var board = state.Board.ToList();
        var position = state.DeckPosition;
        var dealCount = next == Street.Flop ? 3 : 1;
        for (var index = 0; index < dealCount; index++)
        {
            if (position >= state.Deck.Count)
            {
                throw Fail("invalid-state", "The deck was exhausted while dealing the board.");
            }

            board.Add(state.Deck[position]);
            position++;
        }

        var advanced = state with
        {
            Street = next,
            Board = board.ToArray(),
            DeckPosition = position,
            StreetCommitted = new long[state.SeatCount],
            CurrentBet = 0,
            LastRaiseSize = state.BigBlind,
            ActedMask = 0,
            ActingSeat = -1,
        };

        var firstToAct = FindNextActiveSeat(advanced, state.ButtonSeat);
        return firstToAct < 0 ? RunOutBoard(advanced) : advanced with { ActingSeat = firstToAct };
    }

    private static TableState RunOutBoard(TableState state)
    {
        var board = state.Board.ToList();
        var position = state.DeckPosition;
        while (board.Count < 5)
        {
            if (position >= state.Deck.Count)
            {
                throw Fail("invalid-state", "The deck was exhausted while running out the board.");
            }

            board.Add(state.Deck[position]);
            position++;
        }

        return ResolveShowdown(state with { Board = board.ToArray(), DeckPosition = position });
    }

    private static TableState AwardFold(TableState state, int winner)
    {
        var stacks = state.Stacks.ToArray();
        var pot = state.Pot;
        stacks[winner] += pot;

        return state with
        {
            Stacks = stacks,
            Street = Street.Complete,
            StreetCommitted = new long[state.SeatCount],
            Pot = 0,
            CurrentBet = 0,
            LastRaiseSize = 0,
            ActingSeat = -1,
            IsComplete = true,
            ShowdownReached = false,
            Winners = new[] { winner },
            Pots = new[] { new PotAward(pot, new[] { winner }) },
            AwardedPot = pot,
        };
    }

    private static IEnumerable<int> ClockwiseFromButton(TableState state, IReadOnlyList<int> seats)
    {
        var winners = new HashSet<int>(seats);
        for (var offset = 1; offset <= state.SeatCount; offset++)
        {
            var seat = (state.ButtonSeat + offset) % state.SeatCount;
            if (winners.Contains(seat))
            {
                yield return seat;
            }
        }
    }

    private static Card[] Combine(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        var cards = new Card[hole.Count + board.Count];
        for (var index = 0; index < hole.Count; index++)
        {
            cards[index] = hole[index];
        }

        for (var index = 0; index < board.Count; index++)
        {
            cards[hole.Count + index] = board[index];
        }

        return cards;
    }

    private static IReadOnlyList<ActionLogEntry> AppendAction(
        TableState state,
        int seat,
        PokerAction action,
        long potAfter)
    {
        var entries = new ActionLogEntry[state.ActionLog.Count + 1];
        for (var index = 0; index < state.ActionLog.Count; index++)
        {
            entries[index] = state.ActionLog[index];
        }

        entries[^1] = new ActionLogEntry((int)state.Street, seat, action.Kind, action.Amount, potAfter);
        return entries;
    }

    private static bool HasActed(int mask, int seat) => (mask & (1 << seat)) != 0;

    private static PokerAction ToPokerAction(HandActionRecord recorded) => recorded.Kind switch
    {
        "Fold" => PokerAction.Fold(),
        "Check" => PokerAction.Check(),
        "Call" => PokerAction.Call(),
        "Bet" => PokerAction.BetTo(recorded.Amount),
        "Raise" => PokerAction.RaiseTo(recorded.Amount),
        "AllIn" => PokerAction.AllIn(),
        _ => throw new InvalidOperationException($"Recorded action kind '{recorded.Kind}' is not defined."),
    };

    private static IllegalActionException Fail(string reason, string message) => new(reason, message);
}
