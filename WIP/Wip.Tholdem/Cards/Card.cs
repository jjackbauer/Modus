namespace Wip.Tholdem.Cards;

/// <summary>
/// A single playing card. The text form is a rank letter from <c>"23456789TJQKA"</c> followed by a
/// suit letter from <c>"cdhs"</c> (for example <c>"As"</c>, <c>"Td"</c>, <c>"2c"</c>).
/// </summary>
public readonly record struct Card
{
    private const string RankLetters = "23456789TJQKA";
    private const string SuitLetters = "cdhs";

    private readonly Rank _rank;
    private readonly Suit _suit;

    public Card(Rank rank, Suit suit)
    {
        ValidateRank(rank);
        ValidateSuit(suit);
        _rank = rank;
        _suit = suit;
    }

    public Rank Rank
    {
        get => _rank;
        init
        {
            ValidateRank(value);
            _rank = value;
        }
    }

    public Suit Suit
    {
        get => _suit;
        init
        {
            ValidateSuit(value);
            _suit = value;
        }
    }

    public void Deconstruct(out Rank rank, out Suit suit)
    {
        rank = _rank;
        suit = _suit;
    }

    public static Card Parse(string text)
    {
        if (text is null || text.Length != 2)
        {
            throw new ArgumentException(
                "Card text must be exactly two characters: a rank letter followed by a suit letter.",
                nameof(text));
        }

        var rankIndex = RankLetters.IndexOf(text[0]);
        if (rankIndex < 0)
        {
            throw new ArgumentException(
                $"'{text}' does not start with a rank letter from \"{RankLetters}\".",
                nameof(text));
        }

        var suitIndex = SuitLetters.IndexOf(text[1]);
        if (suitIndex < 0)
        {
            throw new ArgumentException(
                $"'{text}' does not end with a suit letter from \"{SuitLetters}\".",
                nameof(text));
        }

        return new Card((Rank)(rankIndex + 2), (Suit)suitIndex);
    }

    public override string ToString()
    {
        Span<char> text = stackalloc char[2];
        text[0] = RankLetters[(int)_rank - 2];
        text[1] = SuitLetters[(int)_suit];
        return new string(text);
    }

    private static void ValidateRank(Rank rank)
    {
        if ((int)rank is < 2 or > 14)
        {
            throw new ArgumentException(
                $"Rank value {(int)rank} is outside the defined range 2..14.",
                nameof(rank));
        }
    }

    private static void ValidateSuit(Suit suit)
    {
        if (!Enum.IsDefined(suit))
        {
            throw new ArgumentException($"Suit value {(int)suit} is not a defined suit.", nameof(suit));
        }
    }
}
