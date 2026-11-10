using Wip.Tholdem.Cards;
using Wip.Tholdem.Hands;

namespace Wip.Tholdem.Web;

/// <summary>
/// Human-readable poker hand-strength names for the spectator viewer. The names are the ones the
/// viewer contract documents — for example <c>"Two Pair, Kings and Jacks"</c>,
/// <c>"Straight, Nine high"</c>, <c>"Flush, Ace high"</c>, <c>"Full House, Kings full of Sevens"</c>
/// and <c>"Four of a Kind, Nines"</c>. Before the board has at least three cards a made hand cannot
/// exist, so the name is <c>"Preflop"</c>.
/// </summary>
public static class HandNaming
{
    /// <summary>The name shown before a hand contains at least five cards.</summary>
    public const string Preflop = "Preflop";

    private const int MinimumEvaluableCards = 5;

    private static readonly string[] SingularNames =
    [
        string.Empty,
        string.Empty,
        "Two",
        "Three",
        "Four",
        "Five",
        "Six",
        "Seven",
        "Eight",
        "Nine",
        "Ten",
        "Jack",
        "Queen",
        "King",
        "Ace",
    ];

    private static readonly string[] PluralNames =
    [
        string.Empty,
        string.Empty,
        "Twos",
        "Threes",
        "Fours",
        "Fives",
        "Sixes",
        "Sevens",
        "Eights",
        "Nines",
        "Tens",
        "Jacks",
        "Queens",
        "Kings",
        "Aces",
    ];

    /// <summary>The two-seat default name list for a hand that has not reached the flop.</summary>
    public static IReadOnlyList<string> PreflopNames => [Preflop, Preflop];

    /// <summary>
    /// Names every seat's best five-card hand from engine card values. Before the flop the list holds
    /// one <see cref="Preflop"/> entry per seat, so the result always has one name per seat.
    /// </summary>
    public static IReadOnlyList<string> ForSeats(
        IReadOnlyList<Card> board,
        IReadOnlyList<IReadOnlyList<Card>> holeCards)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(holeCards);

        if (board.Count < 3)
        {
            return Enumerable.Repeat(Preflop, holeCards.Count).ToArray();
        }

        var names = new string[holeCards.Count];
        for (var seat = 0; seat < holeCards.Count; seat++)
        {
            names[seat] = NameFor(board, holeCards[seat]);
        }

        return names;
    }

    /// <summary>
    /// Names every seat's best five-card hand from recorded card text. Before the flop the list holds
    /// one <see cref="Preflop"/> entry per seat.
    /// </summary>
    public static IReadOnlyList<string> ForSeats(
        IReadOnlyList<string> board,
        IReadOnlyList<IReadOnlyList<string>> holeCards)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(holeCards);

        if (board.Count < 3)
        {
            return Enumerable.Repeat(Preflop, holeCards.Count).ToArray();
        }

        var parsedBoard = TryParse(board);
        if (parsedBoard is null)
        {
            return Enumerable.Repeat(Preflop, holeCards.Count).ToArray();
        }

        var names = new string[holeCards.Count];
        for (var seat = 0; seat < holeCards.Count; seat++)
        {
            names[seat] = NameFor(parsedBoard, TryParse(holeCards[seat]));
        }

        return names;
    }

    /// <summary>Names both seats' best five-card hands from engine card values.</summary>
    public static IReadOnlyList<string> ForCards(
        IReadOnlyList<Card> board,
        IReadOnlyList<Card> holeCardsSeat0,
        IReadOnlyList<Card> holeCardsSeat1)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(holeCardsSeat0);
        ArgumentNullException.ThrowIfNull(holeCardsSeat1);

        if (board.Count < 3)
        {
            return PreflopNames;
        }

        return
        [
            NameFor(board, holeCardsSeat0),
            NameFor(board, holeCardsSeat1),
        ];
    }

    /// <summary>Names both seats' best five-card hands from recorded card text.</summary>
    public static IReadOnlyList<string> ForCards(
        IReadOnlyList<string> board,
        IReadOnlyList<string> holeCardsSeat0,
        IReadOnlyList<string> holeCardsSeat1)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(holeCardsSeat0);
        ArgumentNullException.ThrowIfNull(holeCardsSeat1);

        if (board.Count < 3)
        {
            return PreflopNames;
        }

        var parsedBoard = TryParse(board);
        if (parsedBoard is null)
        {
            return PreflopNames;
        }

        return
        [
            NameFor(parsedBoard, TryParse(holeCardsSeat0)),
            NameFor(parsedBoard, TryParse(holeCardsSeat1)),
        ];
    }

    /// <summary>Formats one evaluated hand with the contracted friendly wording.</summary>
    public static string Describe(HandRank rank)
    {
        var ranks = rank.Tiebreakers;
        return rank.Category switch
        {
            HandCategory.HighCard => "High Card, " + Singular(ranks, 0),
            HandCategory.OnePair => "One Pair, " + Plural(ranks, 0),
            HandCategory.TwoPair => "Two Pair, " + Plural(ranks, 0) + " and " + Plural(ranks, 1),
            HandCategory.ThreeOfAKind => "Three of a Kind, " + Plural(ranks, 0),
            HandCategory.Straight => "Straight, " + Singular(ranks, 0) + " high",
            HandCategory.Flush => "Flush, " + Singular(ranks, 0) + " high",
            HandCategory.FullHouse => "Full House, " + Plural(ranks, 0) + " full of " + Plural(ranks, 1),
            HandCategory.FourOfAKind => "Four of a Kind, " + Plural(ranks, 0),
            HandCategory.StraightFlush => "Straight Flush, " + Singular(ranks, 0) + " high",
            _ => "Unknown hand",
        };
    }

    private static string NameFor(IReadOnlyList<Card> board, IReadOnlyList<Card>? holeCards)
    {
        if (holeCards is null || holeCards.Count != 2)
        {
            return Preflop;
        }

        var cards = new Card[board.Count + holeCards.Count];
        for (var index = 0; index < board.Count; index++)
        {
            cards[index] = board[index];
        }

        for (var index = 0; index < holeCards.Count; index++)
        {
            cards[board.Count + index] = holeCards[index];
        }

        return cards.Length < MinimumEvaluableCards ? Preflop : Describe(HandEvaluator.Evaluate(cards));
    }

    private static Card[]? TryParse(IReadOnlyList<string> cards)
    {
        var parsed = new Card[cards.Count];
        for (var index = 0; index < cards.Count; index++)
        {
            try
            {
                parsed[index] = Card.Parse(cards[index]);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return parsed;
    }

    private static string Singular(IReadOnlyList<int> ranks, int index) =>
        index < ranks.Count && ranks[index] >= 2 && ranks[index] <= 14
            ? SingularNames[ranks[index]]
            : "Unknown";

    private static string Plural(IReadOnlyList<int> ranks, int index) =>
        index < ranks.Count && ranks[index] >= 2 && ranks[index] <= 14
            ? PluralNames[ranks[index]]
            : "Unknown";
}
