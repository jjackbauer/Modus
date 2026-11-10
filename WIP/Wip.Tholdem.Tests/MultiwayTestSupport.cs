using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.Hands;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Tests;

/// <summary>
/// Shared helpers for the multiway contract: deterministic random play and an independent
/// brute-force side-pot calculator. Side-pot states are always reached through the public
/// <see cref="HoldemHandEngine.Start"/> plus legal actions; no test synthesizes a table state.
/// </summary>
internal static class MultiwayTestSupport
{
    public const long StartingStack = 1000;

    public const long SmallBlind = 5;

    public const long BigBlind = 10;

    public static TableState Start(int seats, ulong seed = 12345, int buttonSeat = 0, long startingStack = StartingStack)
        => HoldemHandEngine.Start(seed, seats, 1, startingStack, SmallBlind, BigBlind, buttonSeat);

    public static TableState PlayRandom(
        ulong seed,
        int seats,
        long startingStack = StartingStack,
        int maxActions = 500)
    {
        var state = HoldemHandEngine.Start(
            seed,
            seats,
            1,
            startingStack,
            SmallBlind,
            BigBlind,
            (int)(seed % (ulong)seats));
        var random = new Xorshift64Star(seed ^ 0xD1B54A32D192ED03UL);
        var actions = 0;
        while (!state.IsComplete)
        {
            var legal = HoldemHandEngine.GetLegalActions(state);
            if (legal.Actions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"seed {seed} seats {seats}: no legal actions on {state.Street}.");
            }

            state = HoldemHandEngine.Apply(state, Choose(legal, ref random));
            actions++;
            if (actions > maxActions)
            {
                throw new InvalidOperationException(
                    $"seed {seed} seats {seats} did not complete within {maxActions} actions.");
            }
        }

        return state;
    }

    public static PokerAction Choose(LegalActionSet legal, ref Xorshift64Star random)
    {
        var roll = random.Next() % 100;
        if (legal.CanCheck)
        {
            if (legal.CanBet && roll < 25)
            {
                return PokerAction.BetTo(legal.MinRaiseTo);
            }

            if (legal.CanAllIn && roll < 5)
            {
                return PokerAction.AllIn();
            }

            return PokerAction.Check();
        }

        if (legal.CanFold && (roll >= 92 || (legal.CallAmount >= 20 && roll >= 80)))
        {
            return PokerAction.Fold();
        }

        if (legal.CanCall && roll < 70)
        {
            return PokerAction.Call();
        }

        if (legal.CanRaise && roll < 80)
        {
            return PokerAction.RaiseTo(legal.MinRaiseTo);
        }

        if (legal.CanAllIn && roll < 90)
        {
            return PokerAction.AllIn();
        }

        return legal.CanCall ? PokerAction.Call() : PokerAction.Fold();
    }

    public static Card[] Combine(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
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

    public static long[] AwardTotals(TableState state)
    {
        var awards = new long[state.SeatCount];
        foreach (var pot in state.Pots)
        {
            var baseAward = pot.Amount / pot.Winners.Count;
            var odd = pot.Amount % pot.Winners.Count;
            foreach (var seat in pot.Winners)
            {
                awards[seat] += baseAward;
            }

            foreach (var seat in ClockwiseFromButton(state, pot.Winners).Take((int)odd))
            {
                awards[seat]++;
            }
        }

        return awards;
    }

    public static IEnumerable<int> ClockwiseFromButton(TableState state, IReadOnlyList<int> seats)
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

    /// <summary>
    /// Recomputes the final stacks from the per-seat starting stacks, the public total commitments
    /// and an independent showdown calculation. It restates the award rule on purpose so the
    /// side-pot tests do not trust the engine's own <see cref="TableState.Pots"/> projection.
    /// </summary>
    public static long[] ExpectedFinalStacks(TableState state)
    {
        var stacks = new long[state.SeatCount];
        for (var seat = 0; seat < state.SeatCount; seat++)
        {
            stacks[seat] = state.StartingStacks[seat] - state.TotalCommitted[seat];
        }

        foreach (var (amount, winners) in BruteForcePots(state))
        {
            var baseAward = amount / winners.Length;
            var odd = (int)(amount % winners.Length);
            foreach (var seat in winners)
            {
                stacks[seat] += baseAward;
            }

            foreach (var seat in ClockwiseFromButton(state, winners).Take(odd))
            {
                stacks[seat]++;
            }
        }

        return stacks;
    }

    /// <summary>
    /// The independent pot calculator used by the property test. It is written against the public
    /// <see cref="TableState"/> projection but does not call any pot code from the product: the
    /// layer construction, eligibility filter, tie split and odd-chip order are restated here.
    /// </summary>
    public static List<(long Amount, int[] Winners)> BruteForcePots(TableState state)
    {
        if (!state.ShowdownReached)
        {
            var survivor = Enumerable.Range(0, state.SeatCount).Single(seat => !state.Folded[seat]);
            return new List<(long Amount, int[] Winners)>
            {
                (state.TotalCommitted.Sum(), new[] { survivor }),
            };
        }

        var levels = state.TotalCommitted
            .Where(static committed => committed > 0)
            .Distinct()
            .OrderBy(static level => level)
            .ToArray();

        var layers = new List<(long Amount, int[] Winners)>();
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
                throw new InvalidOperationException($"No eligible seat at contribution level {level}.");
            }

            int[] winners;
            if (eligible.Length == 1)
            {
                winners = eligible;
            }
            else
            {
                var ranks = eligible
                    .Select(seat => HandEvaluator.Evaluate(Combine(state.HoleCards[seat], state.Board)))
                    .ToArray();
                var best = ranks.Max();
                winners = eligible
                    .Where((_, index) => ranks[index].CompareTo(best) == 0)
                    .ToArray();
            }

            layers.Add((amount, winners));
            previous = level;
        }

        return layers;
    }
}
