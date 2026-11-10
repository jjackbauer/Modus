using Wip.Tholdem.History;

namespace Wip.Tholdem.Engine;

/// <summary>
/// The two-seat convenience surface of <see cref="HoldemHandEngine"/>. Every member delegates to
/// the general engine with exactly two seats, so the recorded heads-up behaviour is preserved:
/// <c>sum(Stacks) + Pot == 2 * StartingStack</c> after every transition.
/// </summary>
public static class HeadsUpHandEngine
{
    public const int Seats = 2;

    public static TableState Start(
        ulong seed,
        int handNumber = 1,
        long startingStack = 1000,
        long smallBlind = 5,
        long bigBlind = 10,
        int buttonSeat = 0)
        => HoldemHandEngine.Start(
            seed,
            Seats,
            handNumber,
            startingStack,
            smallBlind,
            bigBlind,
            buttonSeat);

    public static LegalActionSet GetLegalActions(TableState state) => HoldemHandEngine.GetLegalActions(state);

    public static TableState Apply(TableState state, PokerAction action) => HoldemHandEngine.Apply(state, action);

    public static TableState Replay(HandRecord record) => HoldemHandEngine.Replay(record);

    /// <summary>
    /// Splits a two-seat pot in half, giving any odd chip to the non-button seat. This is retained
    /// for the pinned heads-up test; the general engine performs the same split through the
    /// "odd chip goes to the first eligible seat clockwise from the button" rule.
    /// </summary>
    internal static IReadOnlyList<long> SplitPot(long pot, int buttonSeat)
    {
        if (pot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pot), pot, "A pot cannot be negative.");
        }

        if (buttonSeat is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(buttonSeat), buttonSeat, "The button seat must be 0 or 1.");
        }

        var half = pot / 2;
        var extra = pot % 2;
        var awards = new[] { half, half };
        awards[1 - buttonSeat] += extra;
        return awards;
    }
}
