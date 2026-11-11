using System.Collections.ObjectModel;
using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Cards;

namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// A weighted preflop opponent range over the 169 canonical two-card classes with per-combo
/// weights. <see cref="Uniform"/> assigns weight 1.0 to every class, which together with the combo
/// counts (6 pair, 4 suited, 12 offsuit) is combo-uniform and remains the labelled control.
/// </summary>
public sealed class OpponentRange
{
    public const string Schema = "tholdem.opponent-range.v1";

    private const string RankLetters = "23456789TJQKA";

    /// <summary>
    /// Canonical class order: pairs AA..22, then suited AKs..32s, then offsuit AKo..32o, each in
    /// descending high-card then descending low-card order.
    /// </summary>
    private static readonly string[] ClassNames = BuildClassNames();

    private static readonly Dictionary<string, int> ClassIndices = BuildClassIndices();

    private static readonly HashSet<string> KnownClasses = new(ClassNames, StringComparer.Ordinal);

    public static OpponentRange Uniform { get; } = Parse(
        "uniform",
        ClassNames.ToDictionary(static name => name, static _ => 1.0, StringComparer.Ordinal));

    private readonly (string Class, double Weight)[] _classes;
    private readonly Dictionary<string, double> _weightsByClass;
    private readonly (Card First, Card Second, double Weight)[] _combos;

    private OpponentRange(string rangeId, (string Class, double Weight)[] classes)
    {
        RangeId = rangeId;
        _classes = classes;
        _weightsByClass = new Dictionary<string, double>(classes.Length, StringComparer.Ordinal);
        foreach (var entry in classes)
        {
            _weightsByClass.Add(entry.Class, entry.Weight);
        }

        var combos = new List<(Card First, Card Second, double Weight)>();
        foreach (var entry in classes)
        {
            AppendCombos(combos, entry.Class, entry.Weight);
        }

        _combos = combos.ToArray();
        Weights = new ReadOnlyDictionary<string, double>(_weightsByClass);
        RangeHash = StrategyArtifact.ComputeHash(ToCanonicalJson());
    }

    public string RangeId { get; }

    /// <summary>The positive class weights, in canonical class order.</summary>
    public IReadOnlyDictionary<string, double> Weights { get; }

    /// <summary>Content address of the canonical JSON form.</summary>
    public string RangeHash { get; }

    /// <summary>
    /// The whitespace-free canonical form with a fixed key order, classes in canonical order and
    /// only positive weights:
    /// <c>{"schema":"tholdem.opponent-range.v1","rangeId":"...","classes":[...]}</c>.
    /// </summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(32 + (32 * _classes.Length));
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"rangeId\":");
        CanonicalJsonWriter.Text(builder, RangeId);
        builder.Append(",\"classes\":[");
        for (var index = 0; index < _classes.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"class\":");
            CanonicalJsonWriter.Text(builder, _classes[index].Class);
            builder.Append(",\"weight\":");
            CanonicalJsonWriter.Number(builder, _classes[index].Weight);
            builder.Append('}');
        }

        builder.Append("]}");
        return builder.ToString();
    }

    /// <summary>The per-combo weight of the class the two cards fall into, or zero when unweighted.</summary>
    public double WeightFor(Card first, Card second)
    {
        var className = Classify(first, second);
        return _weightsByClass.TryGetValue(className, out var weight) ? weight : 0.0;
    }

    /// <summary>
    /// Validates a weighted range: a non-empty identifier, known class names, finite weights at
    /// least zero and at least one positive weight. The weights dictionary may omit classes; an
    /// omitted class has weight zero.
    /// </summary>
    public static OpponentRange Parse(
        string rangeId,
        IReadOnlyDictionary<string, double> weights)
    {
        if (string.IsNullOrWhiteSpace(rangeId))
        {
            throw new ArgumentException("A non-empty range identifier is required.", nameof(rangeId));
        }

        ArgumentNullException.ThrowIfNull(weights);

        var positive = new List<(string Class, double Weight)>(weights.Count);
        foreach (var entry in weights)
        {
            if (!KnownClasses.Contains(entry.Key))
            {
                throw new ArgumentException(
                    $"Unknown preflop class '{entry.Key}'.",
                    nameof(weights));
            }

            if (!double.IsFinite(entry.Value) || entry.Value < 0.0)
            {
                throw new ArgumentException(
                    $"The weight for class '{entry.Key}' must be finite and non-negative; received {entry.Value}.",
                    nameof(weights));
            }

            if (entry.Value > 0.0)
            {
                positive.Add((entry.Key, entry.Value));
            }
        }

        if (positive.Count == 0)
        {
            throw new ArgumentException(
                "At least one class weight must be positive.",
                nameof(weights));
        }

        positive.Sort(static (left, right) =>
            ClassIndices[left.Class].CompareTo(ClassIndices[right.Class]));
        return new OpponentRange(rangeId, positive.ToArray());
    }

    /// <summary>Every legal two-card combination of a positively weighted class, with its weight.</summary>
    internal IReadOnlyList<(Card First, Card Second, double Weight)> Combos => _combos;

    /// <summary>Zero-based index of a card in the deterministic suit-major deck order.</summary>
    internal static int CardIndex(Card card) => ((int)card.Suit * 13) + ((int)card.Rank - 2);

    private static string Classify(Card first, Card second)
    {
        var firstRank = (int)first.Rank;
        var secondRank = (int)second.Rank;
        if (firstRank == secondRank)
        {
            return RankLetter(firstRank) + RankLetter(firstRank);
        }

        var high = Math.Max(firstRank, secondRank);
        var low = Math.Min(firstRank, secondRank);
        var suited = first.Suit == second.Suit;
        return RankLetter(high) + RankLetter(low) + (suited ? 's' : 'o');
    }

    private static string RankLetter(int rank) => RankLetters[rank - 2].ToString();

    private static string[] BuildClassNames()
    {
        var names = new List<string>(169);
        for (var high = 14; high >= 2; high--)
        {
            names.Add(RankLetter(high) + RankLetter(high));
        }

        for (var high = 14; high >= 3; high--)
        {
            for (var low = high - 1; low >= 2; low--)
            {
                names.Add(RankLetter(high) + RankLetter(low) + 's');
            }
        }

        for (var high = 14; high >= 3; high--)
        {
            for (var low = high - 1; low >= 2; low--)
            {
                names.Add(RankLetter(high) + RankLetter(low) + 'o');
            }
        }

        return names.ToArray();
    }

    private static Dictionary<string, int> BuildClassIndices()
    {
        var indices = new Dictionary<string, int>(ClassNames.Length, StringComparer.Ordinal);
        for (var index = 0; index < ClassNames.Length; index++)
        {
            indices.Add(ClassNames[index], index);
        }

        return indices;
    }

    private static void AppendCombos(
        List<(Card First, Card Second, double Weight)> combos,
        string className,
        double weight)
    {
        var high = RankLetterIndex(className[0]);
        if (className.Length == 2)
        {
            for (var firstSuit = 0; firstSuit < 4; firstSuit++)
            {
                for (var secondSuit = firstSuit + 1; secondSuit < 4; secondSuit++)
                {
                    combos.Add((
                        new Card((Rank)high, (Suit)firstSuit),
                        new Card((Rank)high, (Suit)secondSuit),
                        weight));
                }
            }

            return;
        }

        var low = RankLetterIndex(className[1]);
        if (className[2] == 's')
        {
            for (var suit = 0; suit < 4; suit++)
            {
                combos.Add((
                    new Card((Rank)high, (Suit)suit),
                    new Card((Rank)low, (Suit)suit),
                    weight));
            }

            return;
        }

        for (var highSuit = 0; highSuit < 4; highSuit++)
        {
            for (var lowSuit = 0; lowSuit < 4; lowSuit++)
            {
                if (highSuit == lowSuit)
                {
                    continue;
                }

                combos.Add((
                    new Card((Rank)high, (Suit)highSuit),
                    new Card((Rank)low, (Suit)lowSuit),
                    weight));
            }
        }
    }

    private static int RankLetterIndex(char letter)
    {
        var index = RankLetters.IndexOf(letter);
        if (index < 0)
        {
            throw new ArgumentException($"'{letter}' is not a rank letter.", nameof(letter));
        }

        return index + 2;
    }
}
