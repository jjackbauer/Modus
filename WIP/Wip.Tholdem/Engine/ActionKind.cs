namespace Wip.Tholdem.Engine;

/// <summary>
/// The six actions a heads-up player can take. <c>Bet</c> and <c>Raise</c> carry a "to" amount;
/// the other kinds must carry an amount of zero.
/// </summary>
public enum ActionKind
{
    Fold,
    Check,
    Call,
    Bet,
    Raise,
    AllIn,
}
