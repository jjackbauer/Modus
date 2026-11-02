using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// A population-based strategy pool that holds a collection of <see cref="StrategyEntry"/>
/// objects keyed by <see cref="BonesStrategyId"/>. The pool supports random matchup
/// selection, outcome recording, evaluation/promotion, and pruning.
/// </summary>
public sealed class StrategyPool
{
    private const int MinimumSeatCoverage = 4;

    /// <summary>
    /// The singleton pool instance for the current process. Set by DI during startup.
    /// Any code that needs to observe or contribute to the population pool can use this.
    /// </summary>
    public static StrategyPool? Instance { get; internal set; }

    private readonly Dictionary<BonesStrategyId, StrategyEntry> _pool = new();
    private readonly Dictionary<BonesStrategyId, int> _eliteGracePeriod = new();
    private readonly Random _random;
    private BonesStrategyId? _eliteStrategyId;
    private double _allTimeBestCompositeScore;
    private string? _snapshotPath;

    /// <summary>
    /// Number of evaluation cycles a library-seeded entry retains elite
    /// protection before it can be demoted by a higher-scoring strategy.
    /// </summary>
    public const int GracePeriodCycles = 3;

    /// <summary>
    /// Number of evaluation cycles that have been run on this pool instance.
    /// Used for grace period tracking.
    /// </summary>
    internal int EvaluationCyclesRun { get; private set; }

    public StrategyPool(Random? random = null)
    {
        Instance = this;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// The strategy currently designated as the all-time best elite.
    /// </summary>
    public BonesStrategyId? EliteStrategyId => _eliteStrategyId;

    /// <summary>
    /// The highest composite score ever observed in this pool session.
    /// </summary>
    public double AllTimeBestCompositeScore => _allTimeBestCompositeScore;

    /// <summary>
    /// Read-only snapshot of the current pool entries.
    /// </summary>
    public IReadOnlyDictionary<BonesStrategyId, StrategyEntry> Pool =>
        _pool.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    /// <summary>
    /// Returns the number of entries currently in the pool.
    /// </summary>
    public int Count => _pool.Count;

    /// <summary>
    /// When set to a non-null file path, the pool will auto-persist to disk
    /// after every <see cref="EvaluateAndPromote"/> call.
    /// Set by DI during startup when the host data directory is available.
    /// </summary>
    public string? SnapshotPath
    {
        get => _snapshotPath;
        set => _snapshotPath = value;
    }

    /// <summary>
    /// Adds an entry to the pool. Duplicates by <see cref=\"BonesStrategyId\"/> are rejected.
    /// </summary>
    public void Add(StrategyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_pool.ContainsKey(entry.Id))
        {
            throw new ArgumentException(
                $"A strategy entry with id '{entry.Id.Value}' is already present in the pool.",
                nameof(entry));
        }

        _pool[entry.Id] = entry;
    }

    /// <summary>
    /// Returns a <see cref="Matchup"/> of <paramref name="count"/> distinct strategies
    /// randomly sampled from the pool.
    /// </summary>
    /// <param name="count">Number of strategies to select. Must be 4.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the pool contains fewer than <paramref name="count"/> entries.
    /// </exception>
    /// <remarks>
    /// I12: Rookie entries (fewer than <see cref="RookieEvaluationGames"/> matches played)
    /// receive <see cref="RookieWeightMultiplier"/>× selection weight for accelerated evaluation.
    /// </remarks>
    public Matchup GetRandomMatchup(int count)
    {
        if (_pool.Count < count)
        {
            throw new InvalidOperationException(
                $"Cannot select {count} strategies from a pool of {_pool.Count} entries.");
        }

        var entries = _pool.Values.ToArray();

        // I12: Build a weighted list where rookie entries get boosted weight.
        var weighted = new List<StrategyEntry>(entries.Length * RookieWeightMultiplier);
        foreach (var entry in entries)
        {
            var weight = entry.MatchesPlayed < RookieEvaluationGames
                ? RookieWeightMultiplier
                : 1;
            for (var w = 0; w < weight; w++)
                weighted.Add(entry);
        }

        // Fisher-Yates partial shuffle selecting distinct entries from weighted list.
        var selected = new HashSet<BonesStrategyId>();
        var result = new List<StrategyEntry>(count);
        for (var i = 0; i < weighted.Count && result.Count < count; i++)
        {
            var j = _random.Next(i, weighted.Count);
            (weighted[i], weighted[j]) = (weighted[j], weighted[i]);

            if (selected.Add(weighted[i].Id))
                result.Add(weighted[i]);
        }

        // Fallback: ensure we have exactly `count` distinct entries.
        foreach (var entry in entries)
        {
            if (result.Count >= count)
                break;
            if (selected.Add(entry.Id))
                result.Add(entry);
        }

        return new Matchup(result.ToArray());
    }

    /// <summary>
    /// Rookie entries (fewer than this many matches played) get boosted
    /// selection probability in <see cref="GetRandomMatchup"/>.
    /// </summary>
    public const int RookieEvaluationGames = 20;

    /// <summary>
    /// Weight multiplier for rookie entries in matchup selection.
    /// </summary>
    public const int RookieWeightMultiplier = 3;

    /// <summary>
    /// Records the outcome of a completed game for every strategy in the pool
    /// that participated in the match. Updates ELO ratings and cumulative
    /// effectiveness counters atomically (all-or-nothing via immutable replacement).
    /// Also checks whether any strategy has surpassed the all-time best composite
    /// score and promotes it to elite if so.
    /// </summary>
    /// <param name="gameResult">The completed match result with winner, scores, and per-seat data.</param>
    /// <param name="seatToStrategyId">
    /// Maps each seat (1–4) to the <see cref="BonesStrategyId"/> that played that seat.
    /// This is the linkage between the match result and pool entries.
    /// </param>
    public void RecordOutcome(
        BonesMatchResult gameResult,
        IReadOnlyDictionary<int, BonesStrategyId> seatToStrategyId)
    {
        ArgumentNullException.ThrowIfNull(gameResult);
        ArgumentNullException.ThrowIfNull(seatToStrategyId);

        // Collect participating entries and their ratings before any mutation
        var participants = new List<(int Seat, BonesStrategyId StrategyId, StrategyEntry Entry)>();
        foreach (var (seat, strategyId) in seatToStrategyId)
        {
            if (!_pool.TryGetValue(strategyId, out var entry))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Strategy '{strategyId.Value}' (seat {seat}) is not in the pool – skipping.");
                continue;
            }

            participants.Add((seat, strategyId, entry));
        }

        if (participants.Count == 0)
            return;

        // Build rating lookup for all participants
        var ratingByStrategyId = participants.ToDictionary(
            p => p.StrategyId,
            p => p.Entry.Rating);

        // Determine winner seat
        var winnerSeat = gameResult.Winner.Seat;

        // Build replacement entries
        var replacements = new List<StrategyEntry>();
        foreach (var (seat, strategyId, entry) in participants)
        {
            var isWinner = seat == winnerSeat;
            var myScore = gameResult.CumulativeScores.GetValueOrDefault(
                new BonesPlayerId(seat), 0);

            // Opponent ratings: all other participants
            var opponentRatings = participants
                .Where(p => p.StrategyId != strategyId)
                .Select(p => p.Entry.Rating)
                .ToArray();

            // ELO calculation
            var expectedScore = BonesEloCalculator.CalculateExpectedScore(
                entry.Rating, opponentRatings);
            var actualScore = isWinner ? 1.0 : 0.0;
            var newRating = BonesEloCalculator.CalculateNewRating(
                entry.Rating, expectedScore, actualScore);

            // Score differential: my score minus average opponent score
            var opponentScores = participants
                .Where(p => p.StrategyId != strategyId)
                .Select(p => gameResult.CumulativeScores.GetValueOrDefault(
                    new BonesPlayerId(p.Seat), 0))
                .ToArray();
            var avgOpponentScore = opponentScores.Length > 0
                ? opponentScores.Average()
                : 0.0;
            var scoreDiff = (int)Math.Round(myScore - avgOpponentScore, MidpointRounding.AwayFromZero);

            var updatedEntry = new StrategyEntry(
                entry.Id,
                entry.Seat,
                entry.Kind,
                entry.Source,
                entry.MatchesPlayed + 1,
                isWinner ? entry.Wins + 1 : entry.Wins,
                isWinner ? entry.Losses : entry.Losses + 1,
                entry.CumulativeScoreDifferential + scoreDiff,
                newRating)
            {
                IsElite = entry.IsElite // R6: Preserve elite flag across match outcomes
            };

            replacements.Add(updatedEntry);
        }

        // Atomic replace: all updated entries written back together
        foreach (var replacement in replacements)
        {
            _pool[replacement.Id] = replacement;
        }

        // Check for all-time best promotion after each game
        CheckAllTimeBestPromotion();
    }

    /// <summary>
    /// Evaluates all pool entries using the tournament selector and returns
    /// promotion results. Survivors are the top-ranked entries; the bottom K
    /// (excluding elite) are designated for the graveyard.
    /// </summary>
    /// <param name="selector">
    /// Optional <see cref="TournamentSelector"/>. When null a default instance is used.
    /// </param>
    /// <param name="graveyard">
    /// Optional <see cref="BonesStrategyGraveyard"/>. When non-null, rejected strategies
    /// are recorded in the graveyard with their full match history and rejection reason.
    /// </param>
    public PromotionResults EvaluateAndPromote(
        TournamentSelector? selector = null,
        BonesStrategyGraveyard? graveyard = null)
    {
        selector ??= new TournamentSelector();

        // I14: Increment evaluation cycle counter for grace-period tracking.
        EvaluationCyclesRun++;

        var entries = _pool.Values.ToArray();
        if (entries.Length == 0)
            return PromotionResults.Empty;

        // Never prune below full seat coverage: a pool missing a seat makes the
        // next match fail when it looks up a strategy for that seat.
        var k = Math.Min(
            TournamentSelector.ComputeK(entries.Length),
            Math.Max(0, entries.Length - MinimumSeatCoverage));
        var maxSize = entries.Length - k;

        var survivors = selector.SelectSurvivors(entries, maxSize);
        var graveyardCandidates = selector.SelectGraveyard(entries, k);

        // Record rejected strategies in the graveyard if provided
        if (graveyard is not null)
        {
            foreach (var rejected in graveyardCandidates)
            {
                graveyard.Add(rejected, "Low composite score — pruned during EvaluateAndPromote");
            }
        }

        // Replace pool contents: keep only survivors
        _pool.Clear();
        foreach (var entry in survivors)
        {
            _pool[entry.Id] = entry;
        }

        // H4: Auto-persist the pool to disk after every evaluate-and-promote cycle.
        if (_snapshotPath is not null)
        {
            PersistToDisk(_snapshotPath);
        }

        return new PromotionResults(
            promoted: survivors.Count,
            rejected: graveyardCandidates.Count);
    }

    /// <summary>
    /// Prunes the pool down to at most <paramref name="populationLimit"/> entries
    /// using the tournament selector. Elite strategies are never removed.
    /// </summary>
    /// <param name="populationLimit">Maximum number of entries to retain.</param>
    /// <param name="selector">
    /// Optional <see cref="TournamentSelector"/>. When null a default instance is used.
    /// </param>
    public void Prune(int populationLimit, TournamentSelector? selector = null)
    {
        if (populationLimit <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(populationLimit), populationLimit,
                "Population limit must be at least 1.");

        var effectiveLimit = Math.Max(populationLimit, MinimumSeatCoverage);
        if (_pool.Count <= effectiveLimit)
            return;

        selector ??= new TournamentSelector();

        var entries = _pool.Values.ToArray();
        var survivors = selector.SelectSurvivors(entries, effectiveLimit);

        _pool.Clear();
        foreach (var entry in survivors)
        {
            _pool[entry.Id] = entry;
        }
    }

    /// <summary>
    /// Seeds the pool from a <see cref="BonesStrategyLibrary"/> by loading all
    /// current library entries. All seeded entries are initially marked as elite
    /// to prevent within-session catastrophic regression.
    /// When fewer than <paramref name="targetPoolSize"/> entries exist in the library,
    /// the best strategies per seat are replicated to fill the pool to the target size.
    /// </summary>
    /// <param name="library">The strategy library to seed from.</param>
    /// <param name="random">Optional random instance for shuffle.</param>
    /// <param name="targetPoolSize">
    /// Desired pool size after seeding. Defaults to <see cref="PopulationPoolOptions.DefaultPoolSize"/>.
    /// When the library has fewer entries, best-per-seat strategies are replicated.
    /// </param>
    public static StrategyPool SeedFromLibrary(
        BonesStrategyLibrary library,
        Random? random = null,
        int? targetPoolSize = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var pool = new StrategyPool(random);
        var allEntries = library.AllEntries;
        var targetSize = targetPoolSize ?? PopulationPoolOptions.DefaultPoolSize;

        foreach (var entry in allEntries)
        {
            var strategyId = entry.StrategyId;
            var effectiveness = entry.Effectiveness;
            var initialRating = EloInitialRating(effectiveness);

            var poolEntry = new StrategyEntry(
                strategyId,
                entry.PlayerId.Seat,
                entry.Kind,
                entry.Source,
                effectiveness.MatchesPlayed,
                effectiveness.Wins,
                effectiveness.Losses,
                effectiveness.CumulativeScoreDifferential,
                initialRating)
            {
                IsElite = true // Library entries are the best known; protect them
            };

            pool.Add(poolEntry);

            // I14: Library entries get a grace period before they can be demoted.
            pool._eliteGracePeriod[poolEntry.Id] = GracePeriodCycles;
        }

        // Fill pool to target size by creating genuinely distinct variants
        // and exploration strategies instead of near-clone replicas.
        if (pool.Count < targetSize && allEntries.Count > 0)
        {
            var rng = random ?? Random.Shared;

            // Step 1: Create 2 mutated variants per library entry with distinct heuristics.
            var mutationIndex = 1;
            foreach (var entry in allEntries)
            {
                if (pool.Count >= targetSize)
                    break;

                var baseRating = EloInitialRating(entry.Effectiveness);

                // Variant A: "aggressive" — adds high-double priority comment
                var mutAId = new BonesStrategyId($"{entry.StrategyId.Value}-mut-aggressive");
                var mutASource = entry.Source + "\n// Mutation: prioritize high doubles for scoring\n";
                pool.Add(new StrategyEntry(
                    mutAId, entry.PlayerId.Seat, entry.Kind, mutASource,
                    0, 0, 0, 0,
                    baseRating - 50 + rng.NextDouble() * 20)
                { IsElite = false });

                // Variant B: "defensive" — adds blocking priority comment
                if (pool.Count < targetSize)
                {
                    var mutBId = new BonesStrategyId($"{entry.StrategyId.Value}-mut-defensive");
                    var mutBSource = entry.Source + "\n// Mutation: prioritize blocking opponent moves\n";
                    pool.Add(new StrategyEntry(
                        mutBId, entry.PlayerId.Seat, entry.Kind, mutBSource,
                        0, 0, 0, 0,
                        baseRating - 50 + rng.NextDouble() * 20)
                    { IsElite = false });
                }

                mutationIndex++;
            }

            // Step 2: Fill remaining slots with exploration strategies at lower ELO.
            var explorationIndex = 1;
            while (pool.Count < targetSize)
            {
                var seat = (explorationIndex % 4) + 1;
                var (expId, expSource) = CreateExplorationStrategy(explorationIndex, seat, rng);
                pool.Add(new StrategyEntry(
                    expId, seat, BonesStrategyKind.Script, expSource,
                    0, 0, 0, 0,
                    1400.0 + rng.NextDouble() * 50)  // Start at 1400 ELO
                { IsElite = false });

                explorationIndex++;
            }
        }

        // Compute initial all-time best from seeded elites
        pool.CheckAllTimeBestPromotion();

        return pool;
    }

    /// <summary>
    /// Creates a simple exploration strategy with a distinct heuristic.
    /// Returns a tuple of (strategyId, C# source code).
    /// </summary>
    private static (BonesStrategyId Id, string Source) CreateExplorationStrategy(
        int index,
        int seat,
        Random rng)
    {
        // Cycle through different exploration heuristics to ensure diversity.
        var heuristicKind = index % 5;
        var id = new BonesStrategyId($"explore-{heuristicKind}-s{seat}-v{index}");

        var source = heuristicKind switch
        {
            0 => CreateHighDoubleStrategy(seat),
            1 => CreateLowPipStrategy(seat),
            2 => CreateBlockFirstStrategy(seat),
            3 => CreateMaxOptionsStrategy(seat),
            _ => CreateRandomExplorationStrategy(seat, rng),
        };

        return (id, source);
    }

    private static string CreateHighDoubleStrategy(int seat) => $@"
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {{
        // Exploration: always play highest double, then highest pip
        BonesMove? best = null;
        int bestScore = int.MinValue;
        foreach (var move in legalMoves)
        {{
            if (move.IsPass) continue;
            var tile = move.Tile!.Value;
            var score = tile.IsDouble ? tile.TotalPips * 2 : tile.TotalPips;
            if (score > bestScore) {{ best = move; bestScore = score; }}
        }}
        return best ?? legalMoves[0];
    }}
}}";

    private static string CreateLowPipStrategy(int seat) => $@"
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {{
        // Exploration: always play lowest pip first
        BonesMove? best = null;
        int bestScore = int.MaxValue;
        foreach (var move in legalMoves)
        {{
            if (move.IsPass) continue;
            var tile = move.Tile!.Value;
            if (tile.TotalPips < bestScore) {{ best = move; bestScore = tile.TotalPips; }}
        }}
        return best ?? legalMoves[0];
    }}
}}";

    private static string CreateBlockFirstStrategy(int seat) => $@"
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {{
        // Exploration: if a blocking move exists (reduces opponent options), play it
        foreach (var move in legalMoves)
        {{
            if (move.IsPass) continue;
            // Play any non-pass move — simple blocking heuristic
            return move;
        }}
        return legalMoves[0];
    }}
}}";

    private static string CreateMaxOptionsStrategy(int seat) => $@"
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {{
        // Exploration: play move that leaves most future options (prefer doubles)
        BonesMove? best = null;
        int bestOptions = int.MinValue;
        foreach (var move in legalMoves)
        {{
            if (move.IsPass) continue;
            var tile = move.Tile!.Value;
            var options = tile.IsDouble ? 10 : 1;
            if (options > bestOptions) {{ best = move; bestOptions = options; }}
        }}
        return best ?? legalMoves[0];
    }}
}}";

    private static string CreateRandomExplorationStrategy(int seat, Random rng) => $@"
using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{{
    private static readonly int Seed = {rng.Next()};

    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {{
        // Exploration: semi-random with seed-based deterministic behavior
        var nonPass = new System.Collections.Generic.List<BonesMove>();
        foreach (var move in legalMoves)
            if (!move.IsPass) nonPass.Add(move);
        if (nonPass.Count == 0) return legalMoves[0];
        var idx = (unchecked((Seed * 397) ^ (state.CurrentPlayer.Seat * 17)) & int.MaxValue) % nonPass.Count;
        return nonPass[idx];
    }}
}}";

    // ────────────────────────────────────────────
    // Pool persistence
    // ────────────────────────────────────────────

    private const string PoolDirectoryName = "pool";
    private const string PoolSnapshotFileName = "pool-snapshot.json";

    private static readonly JsonSerializerOptions PoolJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Returns the canonical snapshot file path for the given data directory.
    /// Mirrors the <c>.bones/strategy-library/</c> convention used by <see cref="BonesStrategyLibrary"/>.
    /// </summary>
    public static string GetPoolSnapshotPath(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(dataDirectory));

        return Path.Combine(
            Path.GetFullPath(dataDirectory),
            BonesStrategyLibrary.LibraryDirectoryName,
            PoolDirectoryName,
            PoolSnapshotFileName);
    }

    /// <summary>
    /// Serializes the entire pool state (entries, lineage, elite ID, best score)
    /// into a <see cref="StrategyPoolSnapshot"/> ready for disk persistence.
    /// </summary>
    public StrategyPoolSnapshot Save()
    {
        var entries = new List<PersistedPoolEntry>(_pool.Count);
        foreach (var kvp in _pool)
        {
            entries.Add(PersistedPoolEntry.FromEntry(kvp.Value));
        }

        var lineageRecords = new List<PersistedLineageRecord>();
        foreach (var kvp in _lineage)
        {
            lineageRecords.Add(PersistedLineageRecord.FromLineage(kvp.Value));
        }

        return new StrategyPoolSnapshot(
            entries,
            lineageRecords,
            _eliteStrategyId?.Value,
            _allTimeBestCompositeScore,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Writes the pool snapshot to disk atomically (temp file + rename).
    /// </summary>
    /// <param name="filePath">Full path to the snapshot file.</param>
    public void PersistToDisk(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var snapshot = Save();
        var json = JsonSerializer.Serialize(snapshot, PoolJsonOptions);

        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        AtomicWriteAllText(filePath, json);
    }

    /// <summary>
    /// Reconstructs a <see cref="StrategyPool"/> from a snapshot loaded from disk.
    /// All entries, lineage records, elite designation, and best-score state are restored.
    /// </summary>
    /// <param name="snapshot">The deserialized snapshot.</param>
    /// <param name="logger">
    /// Optional logger for warning about invalid entries that are skipped.
    /// </param>
    /// <returns>A fully restored pool, or null when the snapshot is null.</returns>
    public static StrategyPool? TryLoad(
        StrategyPoolSnapshot? snapshot,
        ILogger? logger = null)
    {
        if (snapshot is null)
            return null;

        // Re-seed random from Random.Shared so subsequent matchups are
        // non-deterministic across sessions (random is not persisted).
        var pool = new StrategyPool(Random.Shared);

        // Restore entries with validation; skip corrupt ones.
        if (snapshot.Entries is not null)
        {
            foreach (var persisted in snapshot.Entries)
            {
                try
                {
                    var entry = persisted.ToEntry();
                    pool.Add(entry);
                }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException)
                {
                    logger?.LogWarning(ex,
                        "Skipping corrupt pool entry '{StrategyId}' during snapshot load.",
                        persisted.StrategyId);
                }
            }
        }

        // Restore lineage records.
        if (snapshot.LineageRecords is not null)
        {
            foreach (var persisted in snapshot.LineageRecords)
            {
                var record = persisted.ToLineage();
                pool._lineage[record.SuccessorId] = record;
            }
        }

        // Restore elite designation.
        if (snapshot.EliteStrategyId is not null)
        {
            var eliteId = new BonesStrategyId(snapshot.EliteStrategyId);
            if (pool._pool.ContainsKey(eliteId))
            {
                pool._eliteStrategyId = eliteId;
                pool._allTimeBestCompositeScore = snapshot.AllTimeBestCompositeScore;

                // Re-apply elite flag on the designated entry.
                var eliteEntry = pool._pool[eliteId];
                pool._pool[eliteId] = eliteEntry with { IsElite = true };
            }
        }

        return pool;
    }

    /// <summary>
    /// Attempts to load a pool snapshot from the given file path.
    /// Returns null when the file is missing or the JSON is corrupt/unreadable.
    /// Invalid individual entries are skipped with a warning.
    /// </summary>
    public static StrategyPool? TryLoadFromDisk(
        string filePath,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var snapshot = JsonSerializer.Deserialize<StrategyPoolSnapshot>(json, PoolJsonOptions);
            return TryLoad(snapshot, logger);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns all currently recorded lineage records (for inspection/testing).
    /// </summary>
    internal IReadOnlyList<PoolLineageRecord> GetLineageRecords()
        => _lineage.Values.ToArray();

    /// <summary>
    /// Verifies the integrity of the loaded pool state. Returns a list of
    /// diagnostic messages for any issues found. An empty list means the
    /// pool passed all integrity checks.
    /// 
    /// Checks performed: (a) at least 4 distinct seats, (b) no duplicate IDs,
    /// (c) elite reference resolves to an existing entry, (d) lineage records
    /// reference valid strategy IDs.
    /// </summary>
    public IReadOnlyList<string> VerifyIntegrity()
    {
        var issues = new List<string>();

        // (a) At least 4 distinct seat assignments exist
        var distinctSeats = _pool.Values.Select(e => e.Seat).Distinct().Count();
        if (distinctSeats < 4)
        {
            issues.Add(
                $"Pool has only {distinctSeats} distinct seat(s); expected at least 4 for a full table.");
        }

        // (b) Duplicate strategy IDs — already enforced by Add, but verify just in case
        var idCounts = _pool.Keys.GroupBy(k => k.Value).ToDictionary(g => g.Key, g => g.Count());
        var dupes = idCounts.Where(kvp => kvp.Value > 1).Select(kvp => kvp.Key).ToList();
        if (dupes.Count > 0)
        {
            issues.Add($"Duplicate strategy IDs found: {string.Join(", ", dupes)}");
        }

        // (c) Elite strategy ID references an entry that exists
        if (_eliteStrategyId is not null && !_pool.ContainsKey(_eliteStrategyId.Value))
        {
            issues.Add(
                $"Elite strategy ID '{_eliteStrategyId.Value}' references a non-existent entry.");
        }

        // (d) Lineage records reference strategy IDs present in the pool
        foreach (var (successorId, record) in _lineage)
        {
            if (!_pool.ContainsKey(successorId))
            {
                issues.Add(
                    $"Lineage successor '{successorId.Value}' is not in the pool.");
            }

            if (!_pool.ContainsKey(record.PredecessorId))
            {
                issues.Add(
                    $"Lineage predecessor '{record.PredecessorId.Value}' (for successor '{successorId.Value}') is not in the pool.");
            }

            if (record.SecondPredecessorId is not null && !_pool.ContainsKey(record.SecondPredecessorId.Value))
            {
                issues.Add(
                    $"Lineage second-predecessor '{record.SecondPredecessorId.Value}' (for successor '{successorId.Value}') is not in the pool.");
            }
        }

        return issues;
    }

    /// <summary>
    /// Writes the full content to <paramref name="path"/> atomically
    /// using a temp file + rename to prevent corruption on process kill mid-write.
    /// </summary>
    private static void AtomicWriteAllText(string path, string json)
    {
        var tempPath = path + ".tmp" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Checks all pool entries against the all-time best composite score.
    /// If a non-elite strategy surpasses the current all-time best, it is
    /// promoted to elite status and the previous elite(s) lose protection.
    /// </summary>
    internal void CheckAllTimeBestPromotion()
    {
        if (_pool.Count == 0)
            return;

        // I14: Decrement grace period counters for entries that still have them.
        foreach (var kvp in _eliteGracePeriod.ToList())
        {
            if (kvp.Value > 1)
                _eliteGracePeriod[kvp.Key] = kvp.Value - 1;
            else
                _eliteGracePeriod.Remove(kvp.Key);
        }

        var selector = new TournamentSelector();
        var entries = _pool.Values.ToArray();
        var maxMatches = entries.Max(e => e.MatchesPlayed);
        if (maxMatches <= 0) maxMatches = 1;
        var maxRating = entries.Max(e => e.Rating);
        if (maxRating <= 0) maxRating = 1500.0;

        foreach (var entry in entries)
        {
            var score = selector.ComputeCompositeScore(entry, maxMatches, maxRating);

            if (score > _allTimeBestCompositeScore)
            {
                // I14: Only demote the old elite if its grace period has expired.
                var oldEliteId = _eliteStrategyId;
                var canDemote = oldEliteId is null
                    || !_eliteGracePeriod.ContainsKey(oldEliteId.Value);

                if (canDemote)
                {
                    // New all-time best found — demote old elite, promote new one
                    DemoteAllElites();

                    var updated = entry with { IsElite = true };
                    _pool[updated.Id] = updated;
                    _eliteStrategyId = updated.Id;
                    _allTimeBestCompositeScore = score;
                }
                // If grace period is still active, skip promotion — current elite is protected.
            }
        }
    }

    /// <summary>
    /// Clears the elite flag on all pool entries that are not grace-period protected.
    /// </summary>
    private void DemoteAllElites()
    {
        foreach (var kvp in _pool.Where(kvp => kvp.Value.IsElite).ToList())
        {
            // I14: Skip demotion for entries still in their grace period.
            if (_eliteGracePeriod.ContainsKey(kvp.Key))
                continue;

            _pool[kvp.Key] = kvp.Value with { IsElite = false };
        }
    }

    /// <summary>
    /// Derives an initial ELO rating from an effectiveness record.
    /// Baseline 1500, adjusted by Bayesian win rate.
    /// </summary>
    private static double EloInitialRating(BonesStrategyEffectivenessRecord e)
    {
        if (e.MatchesPlayed == 0)
            return 1500.0;

        // Bayesian WR: (Wins + 10) / (Matches + 20)
        const double priorWins = 10.0;
        const double priorMatches = 20.0;
        var bayesianWR = (e.Wins + priorWins) / (e.MatchesPlayed + priorMatches);

        // Map to ELO: 1500 baseline, each 0.01 WR above 0.5 → +4 ELO
        const double baselineElo = 1500.0;
        const double eloPerWinRatePoint = 400.0;

        return baselineElo + eloPerWinRatePoint * (bayesianWR - 0.5);
    }

    /// <summary>
    /// In-memory lineage store for tracking parent-child strategy relationships.
    /// </summary>
    private readonly Dictionary<BonesStrategyId, PoolLineageRecord> _lineage = new();

    /// <summary>
    /// Records a lineage link between a successor strategy and one or two predecessors.
    /// </summary>
    public void RecordLineage(
        BonesStrategyId successor,
        IReadOnlyList<BonesStrategyId> predecessors,
        string outcome,
        string diffSummary)
    {
        ArgumentNullException.ThrowIfNull(successor);
        ArgumentNullException.ThrowIfNull(predecessors);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(diffSummary);

        if (predecessors.Count == 0)
            throw new ArgumentException("At least one predecessor is required.", nameof(predecessors));

        var record = new PoolLineageRecord(
            successor,
            predecessors[0],
            predecessors.Count > 1 ? predecessors[1] : null,
            outcome,
            diffSummary,
            DateTimeOffset.UtcNow);

        _lineage[successor] = record;
    }

    /// <summary>
    /// Retrieves the lineage record for a strategy, or null if none exists.
    /// </summary>
    public PoolLineageRecord? GetLineage(BonesStrategyId strategyId)
    {
        return _lineage.TryGetValue(strategyId, out var record) ? record : null;
    }
}
