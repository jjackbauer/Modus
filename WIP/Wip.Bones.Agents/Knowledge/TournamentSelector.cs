using Wip.Bones.Domain;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Computes composite scores for pool strategies and selects survivors and graveyard
/// slots based on weighted tournament ranking. Supports configurable weights and
/// elite protection.
/// </summary>
public sealed class TournamentSelector
{
    private readonly CompositeScoreWeights _weights;

    public TournamentSelector(CompositeScoreWeights? weights = null)
    {
        _weights = weights ?? CompositeScoreWeights.Default;
    }

    /// <summary>
    /// The weights currently in use for composite score calculation.
    /// </summary>
    public CompositeScoreWeights Weights => _weights;

    /// <summary>
    /// Computes the composite score for a single strategy entry.
    /// </summary>
    /// <param name="entry">The strategy entry to score.</param>
    /// <param name="maxMatches">
    /// The maximum matches any entry in the pool has played. Used to normalize
    /// score differential to a per-game average for comparability.
    /// </param>
    /// <param name="maxRating">
    /// The maximum rating any entry in the pool has. Used to normalise
    /// the ELO component to a 0–1 range.
    /// </param>
    public double ComputeCompositeScore(StrategyEntry entry, int maxMatches, double maxRating)
    {
        // Win rate: Wins / MatchesPlayed (0 if no matches)
        var winRate = entry.MatchesPlayed > 0
            ? (double)entry.Wins / entry.MatchesPlayed
            : 0.0;

        // Score differential per game (normalised by maxMatches to bound 0–1 range)
        var avgScoreDiff = maxMatches > 0
            ? entry.CumulativeScoreDifferential / (double)maxMatches
            : 0.0;

        // Normalise score diff: map from [-inf, +inf] through tanh-like scaling
        // We use a simple logistic: sigmoid(z/50) maps per-game scores to (0,1)
        var normalisedScoreDiff = Sigmoid(avgScoreDiff / 50.0);

        // ELO rating normalised to 0–1
        var normalisedElo = maxRating > 0
            ? entry.Rating / maxRating
            : 0.5;

        return _weights.WinRate * winRate
             + _weights.ScoreDifferential * normalisedScoreDiff
             + _weights.EloRating * normalisedElo;
    }

    /// <summary>
    /// Selects the top K strategies by composite score as survivors.
    /// Elite strategies are always included regardless of rank.
    /// </summary>
    /// <param name="pool">All pool entries.</param>
    /// <param name="maxSize">
    /// The maximum number of survivors to select. Computed as pool count - K
    /// where K = max(pool.Size / 4, 4).
    /// </param>
    public IReadOnlyList<StrategyEntry> SelectSurvivors(
        IReadOnlyCollection<StrategyEntry> pool,
        int maxSize)
    {
        if (pool.Count == 0)
            return Array.Empty<StrategyEntry>();

        var maxMatches = pool.Max(e => e.MatchesPlayed);
        var maxRating = pool.Max(e => e.Rating);
        if (maxRating <= 0) maxRating = 1500.0;
        if (maxMatches <= 0) maxMatches = 1;

        // Compute scores
        var ranked = pool
            .Select(e => new
            {
                Entry = e,
                Score = ComputeCompositeScore(e, maxMatches, maxRating)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Rating) // Tiebreaker: ELO rating
            .ToList();

        var survivors = new List<StrategyEntry>(maxSize);

        // First pass: ensure all elite strategies survive
        var eliteEntries = ranked.Where(x => x.Entry.IsElite).ToList();
        var nonElite = ranked.Where(x => !x.Entry.IsElite).ToList();

        foreach (var elite in eliteEntries)
        {
            if (survivors.Count >= maxSize)
                break;
            survivors.Add(elite.Entry);
            nonElite.Remove(elite);
        }

        // Second pass: fill remaining slots with highest-ranked non-elite
        var remainingSlots = maxSize - survivors.Count;
        if (remainingSlots > 0)
        {
            survivors.AddRange(nonElite.Take(remainingSlots).Select(x => x.Entry));
        }

        return survivors;
    }

    /// <summary>
    /// Selects the bottom K strategies by composite score as graveyard candidates.
    /// Elite strategies are excluded from graveyard selection.
    /// </summary>
    /// <param name="pool">All pool entries.</param>
    /// <param name="count">
    /// Number of graveyard slots. Computed as max(pool.Size / 4, 4).
    /// </param>
    public IReadOnlyList<StrategyEntry> SelectGraveyard(
        IReadOnlyCollection<StrategyEntry> pool,
        int count)
    {
        if (pool.Count == 0 || count <= 0)
            return Array.Empty<StrategyEntry>();

        var maxMatches = pool.Max(e => e.MatchesPlayed);
        var maxRating = pool.Max(e => e.Rating);
        if (maxRating <= 0) maxRating = 1500.0;
        if (maxMatches <= 0) maxMatches = 1;

        var ranked = pool
            .Where(e => !e.IsElite) // Elite protection: never graveyard
            .Select(e => new
            {
                Entry = e,
                Score = ComputeCompositeScore(e, maxMatches, maxRating)
            })
            .OrderBy(x => x.Score)              // Ascending: worst first
            .ThenBy(x => x.Entry.Rating)         // Tiebreaker: lower ELO = worse
            .ToList();

        return ranked.Take(count).Select(x => x.Entry).ToList();
    }

    /// <summary>
    /// The K value used for computing survivor and graveyard counts.
    /// K = max(pool.Count / 4, 4).
    /// </summary>
    public static int ComputeK(int poolSize)
    {
        return Math.Max(poolSize / 4, 4);
    }

    private static double Sigmoid(double x)
    {
        return 1.0 / (1.0 + Math.Exp(-x));
    }
}

/// <summary>
/// Configurable weights for the tournament composite score calculation.
/// All weights should sum to 1.0.
/// </summary>
public sealed record CompositeScoreWeights
{
    public static readonly CompositeScoreWeights Default = new(0.4, 0.3, 0.3);

    public CompositeScoreWeights(double winRate, double scoreDifferential, double eloRating)
    {
        WinRate = winRate;
        ScoreDifferential = scoreDifferential;
        EloRating = eloRating;
    }

    /// <summary>Weight for win rate component (0–1). Default: 0.4.</summary>
    public double WinRate { get; }

    /// <summary>Weight for score differential component (0–1). Default: 0.3.</summary>
    public double ScoreDifferential { get; }

    /// <summary>Weight for ELO rating component (0–1). Default: 0.3.</summary>
    public double EloRating { get; }
}
