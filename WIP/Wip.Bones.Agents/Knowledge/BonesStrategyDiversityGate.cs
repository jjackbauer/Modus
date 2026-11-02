using System.Security.Cryptography;
using System.Text;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Evaluates behavioral divergence between strategy entries so the library
/// can enforce a minimum number of meaningfully distinct strategies per seat.
///
/// Two strategies with identical source should score 0 diversity.
/// Two strategies with similar source (small edit distance) score lower.
///
/// For pool-level diversity, Jaccard token-set overlap is used against
/// a configurable similarity threshold (default 0.95) to detect near-duplicates.
/// </summary>
public sealed class BonesStrategyDiversityGate
{
    private readonly int _minimumDistinctStrategies;
    private readonly int _minDiversityCount;
    private readonly double _nearDuplicateThreshold;

    public BonesStrategyDiversityGate(
        int minimumDistinctStrategies = 1,
        int minDiversityCount = 3,
        double nearDuplicateThreshold = 0.95)
    {
        if (minimumDistinctStrategies < 1)
            throw new ArgumentOutOfRangeException(
                nameof(minimumDistinctStrategies),
                "MinimumDistinctStrategies must be at least 1.");

        if (minDiversityCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(minDiversityCount),
                "MinDiversityCount must be at least 1.");

        if (nearDuplicateThreshold is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(nearDuplicateThreshold),
                "NearDuplicateThreshold must be between 0.0 and 1.0.");

        _minimumDistinctStrategies = minimumDistinctStrategies;
        _minDiversityCount = minDiversityCount;
        _nearDuplicateThreshold = nearDuplicateThreshold;
    }

    /// <summary>
    /// Minimum number of meaningfully different strategies required per seat.
    /// </summary>
    public int MinimumDistinctStrategies => _minimumDistinctStrategies;

    /// <summary>
    /// Minimum number of architecturally distinct strategies required in the pool
    /// before a Ponder-injected strategy is triggered.
    /// </summary>
    public int MinDiversityCount => _minDiversityCount;

    /// <summary>
    /// Jaccard similarity threshold above which two strategies are considered
    /// near-duplicates (default 0.95 = 95%).
    /// </summary>
    public double NearDuplicateThreshold => _nearDuplicateThreshold;

    /// <summary>
    /// Computes a diversity score (0.0 to 1.0) between two strategy sources.
    /// 1.0 = completely distinct, 0.0 = byte-identical.
    /// </summary>
    public double ComputeDiversity(string sourceA, string sourceB)
    {
        ArgumentNullException.ThrowIfNull(sourceA);
        ArgumentNullException.ThrowIfNull(sourceB);

        if (sourceA.Length == 0 && sourceB.Length == 0)
            return 0.0;

        var normalizedA = Normalize(sourceA);
        var normalizedB = Normalize(sourceB);

        if (normalizedA == normalizedB)
            return 0.0;

        // Use source hash distance as a fast approximate divergence.
        var hashA = ComputeSourceHash(normalizedA);
        var hashB = ComputeSourceHash(normalizedB);

        // Simple ratio: identical characters / max length.
        var maxLen = Math.Max(normalizedA.Length, normalizedB.Length);
        if (maxLen == 0)
            return 0.0;

        // Use Levenshtein ratio as the diversity measure.
        var distance = LevenshteinDistance(normalizedA, normalizedB);
        var diversity = (double)distance / maxLen;

        // Clamp and boost with hash diff signal.
        // If hashes differ but text is very similar, provide a slight floor.
        if (hashA != hashB && diversity < 0.01)
            diversity = 0.01;

        return Math.Clamp(diversity, 0.0, 1.0);
    }

    /// <summary>
    /// Returns true when the library should allow the Ponder stage to run
    /// even if a viable entry exists, because the seat has fewer than
    /// MinimumDistinctStrategies meaningfully different strategies.
    /// </summary>
    public bool ShouldInvokePonder(
        BonesPlayerId playerId,
        IReadOnlyList<BonesStrategyLibraryEntry> existingEntries)
    {
        ArgumentNullException.ThrowIfNull(existingEntries);

        if (_minimumDistinctStrategies <= 1)
            return false;

        if (existingEntries.Count == 0)
            return true;

        // Count how many entries are meaningfully different from each other.
        var distinctSet = new List<BonesStrategyLibraryEntry>();
        foreach (var entry in existingEntries)
        {
            var isDistinct = true;
            foreach (var alreadyDistinct in distinctSet)
            {
                if (ComputeDiversity(entry.Source, alreadyDistinct.Source) < 0.05)
                {
                    isDistinct = false;
                    break;
                }
            }

            if (isDistinct)
                distinctSet.Add(entry);
        }

        return distinctSet.Count < _minimumDistinctStrategies;
    }

    /// <summary>
    /// Returns the count of meaningfully distinct strategies for a seat.
    /// </summary>
    public int CountDistinctStrategies(IReadOnlyList<BonesStrategyLibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return 0;

        var distinctSet = new List<BonesStrategyLibraryEntry>();
        foreach (var entry in entries)
        {
            var isDistinct = true;
            foreach (var alreadyDistinct in distinctSet)
            {
                if (ComputeDiversity(entry.Source, alreadyDistinct.Source) < 0.05)
                {
                    isDistinct = false;
                    break;
                }
            }

            if (isDistinct)
                distinctSet.Add(entry);
        }

        return distinctSet.Count;
    }

    /// <summary>
    /// Checks pool-level diversity using Jaccard token-set overlap on
    /// <see cref="StrategyEntry"/> source code. When the number of
    /// architecturally distinct strategies drops below
    /// <see cref="MinDiversityCount"/>, <see cref="PoolDiversityResult.NeedsPonderInjection"/>
    /// is set to true.
    /// </summary>
    public PoolDiversityResult CheckPoolDiversity(
        IReadOnlyDictionary<BonesStrategyId, StrategyEntry> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (pool.Count == 0)
        {
            return new PoolDiversityResult(
                0, _minDiversityCount, _nearDuplicateThreshold, needsPonderInjection: true);
        }

        var entries = pool.Values.ToArray();
        var distinctArchitectures = CountDistinctArchitectures(entries);

        var needsPonderInjection = distinctArchitectures < _minDiversityCount;

        return new PoolDiversityResult(
            distinctArchitectures, _minDiversityCount, _nearDuplicateThreshold, needsPonderInjection);
    }

    /// <summary>
    /// Returns true when the pool has fewer than <see cref="MinDiversityCount"/>
    /// architecturally distinct strategies, indicating a Ponder-injected
    /// strategy should be triggered to restore diversity.
    /// </summary>
    public bool NeedsPonderInjection(IReadOnlyDictionary<BonesStrategyId, StrategyEntry> pool)
    {
        var result = CheckPoolDiversity(pool);
        return result.NeedsPonderInjection;
    }

    /// <summary>
    /// Counts the number of architecturally distinct strategies in a collection
    /// of pool entries. Two strategies with Jaccard similarity above
    /// <see cref="NearDuplicateThreshold"/> are grouped as near-duplicates
    /// and only count as one architecture.
    /// </summary>
    public int CountDistinctArchitectures(IReadOnlyList<StrategyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return 0;

        // Pre-tokenize all entries
        var tokenSets = entries
            .Select(e => BonesStrategyGraveyard.Tokenize(e.Source))
            .ToArray();

        // Union-Find: group entries whose Jaccard similarity > threshold
        var n = entries.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var similarity = BonesStrategyGraveyard.JaccardSimilarity(tokenSets[i], tokenSets[j]);
                if (similarity > _nearDuplicateThreshold)
                {
                    Union(parent, i, j);
                }
            }
        }

        // Count distinct root sets
        var distinctRoots = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            distinctRoots.Add(Find(parent, i));
        }

        return distinctRoots.Count;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]]; // path compression
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int i, int j)
    {
        var rootI = Find(parent, i);
        var rootJ = Find(parent, j);
        if (rootI != rootJ)
            parent[rootJ] = rootI;
    }

    private static string Normalize(string source)
        => source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string ComputeSourceHash(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Use only first 8KB for performance on large sources.
        const int maxLength = 8192;
        if (a.Length > maxLength) a = a[..maxLength];
        if (b.Length > maxLength) b = b[..maxLength];

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}

/// <summary>
/// Result of a pool-level diversity check via
/// <see cref="BonesStrategyDiversityGate.CheckPoolDiversity"/>.
/// </summary>
public sealed record PoolDiversityResult
{
    public PoolDiversityResult(
        int distinctArchitectureCount,
        int minDiversityCount,
        double nearDuplicateThreshold,
        bool needsPonderInjection)
    {
        DistinctArchitectureCount = distinctArchitectureCount;
        MinDiversityCount = minDiversityCount;
        NearDuplicateThreshold = nearDuplicateThreshold;
        NeedsPonderInjection = needsPonderInjection;
    }

    /// <summary>
    /// Number of architecturally distinct strategies found in the pool.
    /// Near-duplicates (Jaccard similarity > threshold) count as one.
    /// </summary>
    public int DistinctArchitectureCount { get; }

    /// <summary>
    /// The configured minimum number of distinct architectures required.
    /// </summary>
    public int MinDiversityCount { get; }

    /// <summary>
    /// The Jaccard similarity threshold used for this check.
    /// </summary>
    public double NearDuplicateThreshold { get; }

    /// <summary>
    /// True when <see cref="DistinctArchitectureCount"/> is below
    /// <see cref="MinDiversityCount"/>, indicating the pool needs
    /// a fresh Ponder-generated strategy to restore diversity.
    /// </summary>
    public bool NeedsPonderInjection { get; }
}
