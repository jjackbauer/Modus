using Wip.Tholdem.Cards;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests;

internal static class EngineTestSupport
{
    public static TableState LimpAndFlop(ulong seed = 42)
    {
        var state = HeadsUpHandEngine.Start(seed);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Call());
        return HeadsUpHandEngine.Apply(state, PokerAction.Check());
    }

    public static TableState CheckDown(ulong seed = 42)
    {
        var state = LimpAndFlop(seed);
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        state = HeadsUpHandEngine.Apply(state, PokerAction.Check());
        return HeadsUpHandEngine.Apply(state, PokerAction.Check());
    }

    public static TableState PlayAllInHand(ulong seed = 42)
    {
        var state = HeadsUpHandEngine.Start(seed);
        state = HeadsUpHandEngine.Apply(state, PokerAction.AllIn());
        return HeadsUpHandEngine.Apply(state, PokerAction.Call());
    }

    public static TableState PlayRandom(ulong seed)
    {
        var state = HeadsUpHandEngine.Start(seed);
        var random = new Xorshift64Star(seed ^ 0x9E3779B97F4A7C15UL);
        var actions = 0;
        while (!state.IsComplete)
        {
            var legal = HeadsUpHandEngine.GetLegalActions(state);
            if (legal.Actions.Count == 0)
            {
                throw new InvalidOperationException($"No legal actions at street {state.Street}.");
            }

            var index = (int)(random.Next() % (ulong)legal.Actions.Count);
            state = HeadsUpHandEngine.Apply(state, legal.Actions[index]);
            actions++;
            if (actions > 200)
            {
                throw new InvalidOperationException($"Hand seed {seed} did not complete within 200 actions.");
            }
        }

        return state;
    }

    public static HandRecord CheckDownRecord(ulong seed = 42) => HandRecord.From(CheckDown(seed));

    public static HandRecord RandomRecord(ulong seed) => HandRecord.From(PlayRandom(seed));

    public static Card[] SevenCards(TableState state, int seat)
    {
        var hole = seat == 0 ? state.HoleCardsSeat0 : state.HoleCardsSeat1;
        var cards = new Card[hole.Count + state.Board.Count];
        for (var i = 0; i < hole.Count; i++)
        {
            cards[i] = hole[i];
        }

        for (var i = 0; i < state.Board.Count; i++)
        {
            cards[hole.Count + i] = state.Board[i];
        }

        return cards;
    }

    public static void AssertChipConservation(TableState state, long expectedTotal)
    {
        Assert.Equal(expectedTotal, state.Stacks.Sum() + state.Pot);
    }
}
