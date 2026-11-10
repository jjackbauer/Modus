namespace Wip.Tholdem.Agents.Runtime;

/// <summary>
/// The hidden-information-safe view of one recorded hand for one seat: the board, that seat's own
/// hole cards, a textual action log, the result and the seat's net chips. It deliberately has no
/// member for the seed, the order of the undealt cards or the opponent's hole cards.
/// </summary>
public sealed record SessionHandView(
    int HandNumber,
    IReadOnlyList<string> Board,
    IReadOnlyList<string> OwnHoleCards,
    IReadOnlyList<string> Actions,
    IReadOnlyList<int> Winners,
    bool Showdown,
    long PotAwarded,
    long NetChips);
