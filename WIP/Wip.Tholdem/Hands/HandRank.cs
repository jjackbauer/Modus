using System.Text;

namespace Wip.Tholdem.Hands;

/// <summary>
/// The evaluated strength of a five-card poker hand. <see cref="Score"/> is a total order that is
/// consistent with <see cref="CompareTo"/>.
/// </summary>
public readonly record struct HandRank : IComparable<HandRank>
{
    private const string RankLetters = "23456789TJQKA";

    private readonly int[]? _tiebreakers;
    private readonly long _score;

    public HandRank(HandCategory category, IReadOnlyList<int> tiebreakers)
    {
        ArgumentNullException.ThrowIfNull(tiebreakers);

        if (tiebreakers.Count > 5)
        {
            throw new ArgumentException("A hand rank has at most five tiebreakers.", nameof(tiebreakers));
        }

        var values = new int[tiebreakers.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var value = tiebreakers[i];
            if (value is < 2 or > 14)
            {
                throw new ArgumentException(
                    $"Tiebreaker rank {value} is outside the defined range 2..14.",
                    nameof(tiebreakers));
            }

            values[i] = value;
        }

        Category = category;
        _tiebreakers = values;
        _score = ComputeScore(category, values);
    }

    public HandCategory Category { get; }

    public IReadOnlyList<int> Tiebreakers =>
        _tiebreakers is null ? Array.Empty<int>() : Array.AsReadOnly(_tiebreakers);

    public long Score => _score;

    public int CompareTo(HandRank other) => _score.CompareTo(other._score);

    public bool Equals(HandRank other) => _score == other._score;

    public override int GetHashCode() => _score.GetHashCode();

    public override string ToString()
    {
        var ranks = DisplayRanks();
        var builder = new StringBuilder();
        builder.Append(Category.ToString());
        builder.Append('[');
        for (var i = 0; i < ranks.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(RankLetters[ranks[i] - 2]);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private int[] DisplayRanks()
    {
        var ranks = _tiebreakers ?? Array.Empty<int>();
        return Category switch
        {
            HandCategory.OnePair when ranks.Length == 4 =>
                new[] { ranks[0], ranks[0], ranks[1], ranks[2], ranks[3] },
            HandCategory.TwoPair when ranks.Length == 3 =>
                new[] { ranks[0], ranks[0], ranks[1], ranks[1], ranks[2] },
            HandCategory.ThreeOfAKind when ranks.Length == 3 =>
                new[] { ranks[0], ranks[0], ranks[0], ranks[1], ranks[2] },
            HandCategory.FullHouse when ranks.Length == 2 =>
                new[] { ranks[0], ranks[0], ranks[0], ranks[1], ranks[1] },
            HandCategory.FourOfAKind when ranks.Length == 2 =>
                new[] { ranks[0], ranks[0], ranks[0], ranks[0], ranks[1] },
            _ => ranks,
        };
    }

    private static long ComputeScore(HandCategory category, ReadOnlySpan<int> tiebreakers)
    {
        var score = (long)category << 20;
        if (tiebreakers.Length > 0)
        {
            score |= (long)tiebreakers[0] << 16;
        }

        if (tiebreakers.Length > 1)
        {
            score |= (long)tiebreakers[1] << 12;
        }

        if (tiebreakers.Length > 2)
        {
            score |= (long)tiebreakers[2] << 8;
        }

        if (tiebreakers.Length > 3)
        {
            score |= (long)tiebreakers[3] << 4;
        }

        if (tiebreakers.Length > 4)
        {
            score |= (long)tiebreakers[4];
        }

        return score;
    }
}
