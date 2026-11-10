using Wip.Tholdem.Cards;

namespace Wip.Tholdem.Engine;

/// <summary>
/// One pot layer awarded at showdown (or the single pot awarded when a hand ends by fold).
/// </summary>
/// <remarks>
/// Every showdown layer is recorded as its own <see cref="PotAward"/>; consecutive layers won by the
/// same winner set are deliberately <b>not</b> merged, so the layer amounts stay independently
/// checkable against a brute-force side-pot calculator. <see cref="Winners"/> is always in ascending
/// seat order, even when an odd chip was assigned by walking clockwise from the button. A fold
/// produces one award for the whole pot.
/// </remarks>
public sealed record PotAward(long Amount, IReadOnlyList<int> Winners);

/// <summary>
/// Immutable snapshot of a no-limit Texas Hold'em hand for two to six seats.
/// </summary>
/// <remarks>
/// <see cref="HoldemHandEngine.Apply"/> never mutates an existing instance; it returns a new
/// snapshot. Every live state satisfies the chip-conservation invariant
/// <c>sum(Stacks) + Pot == sum(StartingStacks)</c>; uncalled excess is settled as the top pot layer
/// at showdown, so it is never removed from the count before that point. The hole cards, seed, deck
/// order and deck position are deliberately not part of the public surface: only
/// <see cref="Wip.Tholdem.History.PlayerObservation"/> is the agent-facing projection. Tests can
/// inspect them through the internal accessors because the test assembly is a declared friend.
/// </remarks>
public sealed record TableState
{
    internal TableState()
    {
        Board = Array.Empty<Card>();
        Stacks = Array.Empty<long>();
        StartingStacks = Array.Empty<long>();
        StreetCommitted = Array.Empty<long>();
        TotalCommitted = Array.Empty<long>();
        Folded = Array.Empty<bool>();
        AllIn = Array.Empty<bool>();
        Winners = Array.Empty<int>();
        Pots = Array.Empty<PotAward>();
        Deck = Array.Empty<Card>();
        HoleCards = Array.Empty<IReadOnlyList<Card>>();
        ActionLog = Array.Empty<ActionLogEntry>();
    }

    public int SeatCount { get; internal init; }

    public int HandNumber { get; internal init; }

    public Street Street { get; internal init; }

    public int ButtonSeat { get; internal init; }

    public int SmallBlindSeat { get; internal init; }

    public int BigBlindSeat { get; internal init; }

    public int ActingSeat { get; internal init; }

    public IReadOnlyList<Card> Board { get; internal init; }

    /// <summary>The starting stack of each seat, in seat order; one value per seat.</summary>
    public IReadOnlyList<long> StartingStacks { get; internal init; }

    public IReadOnlyList<long> Stacks { get; internal init; }

    public IReadOnlyList<long> StreetCommitted { get; internal init; }

    public IReadOnlyList<long> TotalCommitted { get; internal init; }

    public IReadOnlyList<bool> Folded { get; internal init; }

    public IReadOnlyList<bool> AllIn { get; internal init; }

    public long Pot { get; internal init; }

    public long CurrentBet { get; internal init; }

    public long LastRaiseSize { get; internal init; }

    public bool IsComplete { get; internal init; }

    public bool ShowdownReached { get; internal init; }

    public IReadOnlyList<int> Winners { get; internal init; }

    public IReadOnlyList<PotAward> Pots { get; internal init; }

    /// <summary>
    /// Compatibility projection of <see cref="StartingStacks"/>: the first seat's starting stack.
    /// It equals every seat's stack in every equal-stack (v1) case.
    /// </summary>
    public long StartingStack { get; internal init; }

    public long SmallBlind { get; internal init; }

    public long BigBlind { get; internal init; }

    public long AwardedPot { get; internal init; }

    internal ulong Seed { get; init; }

    internal IReadOnlyList<Card> Deck { get; init; }

    internal IReadOnlyList<IReadOnlyList<Card>> HoleCards { get; init; }

    /// <summary>Compatibility projection for the two-seat history and the existing tests.</summary>
    internal IReadOnlyList<Card> HoleCardsSeat0 =>
        HoleCards.Count > 0 ? HoleCards[0] : Array.Empty<Card>();

    /// <summary>Compatibility projection for the two-seat history and the existing tests.</summary>
    internal IReadOnlyList<Card> HoleCardsSeat1 =>
        HoleCards.Count > 1 ? HoleCards[1] : Array.Empty<Card>();

    internal int DeckPosition { get; init; }

    internal int ActedMask { get; init; }

    internal IReadOnlyList<ActionLogEntry> ActionLog { get; init; }
}

/// <summary>
/// One recorded action. <see cref="PotAfter"/> is the pot after the action and any immediate
/// settlement (pot award or showdown), which makes replay verification exact.
/// </summary>
internal readonly record struct ActionLogEntry(
    int Street,
    int Seat,
    ActionKind Kind,
    long Amount,
    long PotAfter);
