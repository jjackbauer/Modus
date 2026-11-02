using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// A stored record of a strategy that was rejected from the pool,
/// kept so the enhancement pipeline can avoid re-trying known dead ends.
/// </summary>
public sealed record GraveyardEntry
{
    public GraveyardEntry(
        StrategyEntry entry,
        string rejectionReason,
        DateTimeOffset timestampUtc)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        RejectionReason = rejectionReason ?? throw new ArgumentNullException(nameof(rejectionReason));
        TimestampUtc = timestampUtc;
    }

    /// <summary>The full strategy entry that was pruned.</summary>
    public StrategyEntry Entry { get; }

    /// <summary>Human-readable reason the strategy was rejected.</summary>
    public string RejectionReason { get; }

    /// <summary>UTC timestamp when the strategy was placed in the graveyard.</summary>
    public DateTimeOffset TimestampUtc { get; }
}

/// <summary>
/// A graveyard store of rejected/replaced strategies with their full match history
/// and rejection reason. When the enhancement pipeline needs to create a replacement,
/// it consults the graveyard to avoid re-trying known dead ends via token-overlap checks.
/// </summary>
public sealed class BonesStrategyGraveyard
{
    private readonly List<GraveyardEntry> _entries = new();

    /// <summary>
    /// Returns the number of entries currently in the graveyard.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Records a rejected strategy in the graveyard.
    /// </summary>
    /// <param name="entry">The strategy entry that was pruned.</param>
    /// <param name="rejectionReason">
    /// Human-readable reason for rejection (e.g. "low composite score", "failed OR-gate").
    /// </param>
    public void Add(StrategyEntry entry, string rejectionReason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rejectionReason);

        var graveyardEntry = new GraveyardEntry(
            entry,
            rejectionReason,
            DateTimeOffset.UtcNow);

        _entries.Add(graveyardEntry);
    }

    /// <summary>
    /// Checks whether the graveyard already contains a strategy whose source code
    /// has more than <paramref name="threshold"/> token overlap with
    /// <paramref name="sourceCode"/>. Uses Jaccard similarity on whitespace/punctuation-
    /// split tokens.
    /// </summary>
    /// <param name="sourceCode">The candidate source code to check.</param>
    /// <param name="threshold">
    /// Minimum Jaccard similarity (0–1) to consider as a duplicate. Default 0.90.
    /// </param>
    /// <returns>True if any graveyard entry exceeds the similarity threshold.</returns>
    public bool ContainsSimilarSource(string sourceCode, double threshold = 0.90)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        if (_entries.Count == 0)
            return false;

        var candidateTokens = Tokenize(sourceCode);

        if (candidateTokens.Count == 0)
            return false;

        foreach (var graveyardEntry in _entries)
        {
            var graveyardTokens = Tokenize(graveyardEntry.Entry.Source);

            var similarity = JaccardSimilarity(candidateTokens, graveyardTokens);

            if (similarity > threshold)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns all graveyard entries in insertion order.
    /// </summary>
    public IReadOnlyList<GraveyardEntry> GetAllGraveyardEntries()
        => _entries.ToArray();

    /// <summary>
    /// Clears all entries from the graveyard.
    /// </summary>
    public void Clear()
        => _entries.Clear();

    /// <summary>
    /// Splits source code into a set of normalized tokens (words/identifiers).
    /// Splits on whitespace, punctuation, and operators; filters empty tokens.
    /// </summary>
    internal static HashSet<string> Tokenize(string source)
    {
        var tokens = new HashSet<string>();

        if (string.IsNullOrWhiteSpace(source))
            return tokens;

        // Split on common C# delimiters: whitespace, punctuation, operators, brackets
        var parts = source.Split(
            new[]
            {
                ' ', '\t', '\r', '\n',
                '{', '}', '(', ')', '[', ']',
                ';', ',', '.', ':', '?',
                '+', '-', '*', '/', '%',
                '=', '<', '>', '!', '&', '|', '^', '~',
                '"', '\'', '`'
            },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                tokens.Add(trimmed);
        }

        return tokens;
    }

    /// <summary>
    /// Computes the Jaccard similarity between two token sets:
    /// |A ∩ B| / |A ∪ B|. Returns 0 when both sets are empty.
    /// </summary>
    internal static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 1.0;

        var intersection = 0;
        var union = new HashSet<string>(a);

        foreach (var token in b)
        {
            if (union.Contains(token))
                intersection++;
            else
                union.Add(token);
        }

        return union.Count == 0 ? 0.0 : (double)intersection / union.Count;
    }
}
