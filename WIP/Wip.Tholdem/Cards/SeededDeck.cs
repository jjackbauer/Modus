namespace Wip.Tholdem.Cards;

/// <summary>
/// Builds the canonical 52-card deck and shuffles it deterministically from a seed.
/// </summary>
/// <remarks>
/// The unshuffled order is suit ascending (clubs, diamonds, hearts, spades) and rank ascending
/// (two through ace), so the index of a card is <c>suitIndex * 13 + (rank - 2)</c>. The shuffle is
/// Fisher-Yates driven by <see cref="Xorshift64Star"/> exactly as specified by the domain contract:
/// no pseudo-random generator, clock or culture-sensitive formatting participates.
/// </remarks>
public static class SeededDeck
{
    public const int Size = 52;

    public static IReadOnlyList<Card> Create(ulong seed)
    {
        var cards = new Card[Size];
        for (var suit = 0; suit < 4; suit++)
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                cards[(suit * 13) + (rank - 2)] = new Card((Rank)rank, (Suit)suit);
            }
        }

        var random = new Xorshift64Star(seed);
        unchecked
        {
            for (var i = 51; i > 0; i--)
            {
                var j = (int)(random.Next() % (ulong)(i + 1));
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }

        return Array.AsReadOnly(cards);
    }
}
