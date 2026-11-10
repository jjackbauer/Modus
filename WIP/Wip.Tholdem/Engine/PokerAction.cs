namespace Wip.Tholdem.Engine;

/// <summary>
/// A single action offered to or taken by a player.
/// </summary>
/// <remarks>
/// For <see cref="ActionKind.Bet"/> and <see cref="ActionKind.Raise"/>, <see cref="Amount"/> is the
/// player's total street commitment after the action ("bet/raise to"). For every other kind the
/// amount must be zero; <see cref="HeadsUpHandEngine.Apply"/> rejects a non-zero amount with the
/// <c>amount-not-allowed</c> reason.
/// </remarks>
public readonly record struct PokerAction(ActionKind Kind, long Amount = 0)
{
    public static PokerAction Fold() => new(ActionKind.Fold);

    public static PokerAction Check() => new(ActionKind.Check);

    public static PokerAction Call() => new(ActionKind.Call);

    public static PokerAction BetTo(long amount) => new(ActionKind.Bet, amount);

    public static PokerAction RaiseTo(long amount) => new(ActionKind.Raise, amount);

    public static PokerAction AllIn() => new(ActionKind.AllIn);
}
