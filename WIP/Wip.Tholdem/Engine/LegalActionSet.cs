namespace Wip.Tholdem.Engine;

/// <summary>
/// The complete legality projection for the acting seat. <see cref="Actions"/> contains one
/// canonical representative action for each legal kind, in Fold, Check, Call, Bet, Raise, AllIn
/// order.
/// </summary>
public sealed record LegalActionSet(
    bool CanFold,
    bool CanCheck,
    bool CanCall,
    long CallAmount,
    bool CanBet,
    bool CanRaise,
    long MinRaiseTo,
    long MaxRaiseTo,
    bool CanAllIn,
    IReadOnlyList<PokerAction> Actions);
