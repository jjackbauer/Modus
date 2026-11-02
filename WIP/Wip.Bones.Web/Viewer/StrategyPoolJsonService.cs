using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

public sealed class StrategyPoolJsonService
{
    private readonly BonesStrategyLibrary? _library;

    public StrategyPoolJsonService(BonesStrategyLibrary? library = null)
    {
        _library = library;
    }

    public PoolSnapshot BuildPoolSnapshot()
    {
        var pool = StrategyPool.Instance;
        if (pool is null)
            return new PoolSnapshot
            {
                Entries = [],
                Stats = BuildEmptyStats(),
                IsRunning = false,
            };

        var entries = pool.Pool.Values.ToList();
        var selector = new TournamentSelector();
        var maxMatches = entries.Count > 0 ? entries.Max(static e => e.MatchesPlayed) : 1;
        var maxRating = entries.Count > 0 ? entries.Max(static e => e.Rating) : 1500.0;
        if (maxMatches <= 0) maxMatches = 1;
        if (maxRating <= 0) maxRating = 1500.0;

        var viewModels = entries
            .Select(entry =>
            {
                var score = selector.ComputeCompositeScore(entry, maxMatches, maxRating);
                return CreateEntryViewModel(entry, pool);
            })
            .OrderByDescending(static vm => vm.Rating)
            .ThenByDescending(static vm => vm.MatchesPlayed)
            .ToList();

        return new PoolSnapshot
        {
            Entries = viewModels,
            Stats = BuildStats(entries),
            IsRunning = true,
        };
    }

    public StrategyDetailViewModel? BuildStrategyDetail(BonesStrategyId strategyId)
    {
        var pool = StrategyPool.Instance;
        if (pool is null)
            return null;

        if (!pool.Pool.TryGetValue(strategyId, out var entry))
            return null;

        var lineageRecord = pool.GetLineage(strategyId);
        StrategyLineageViewModel? lineage = null;
        if (lineageRecord is not null)
        {
            var successorIds = pool.Pool.Values
                .Select(e => pool.GetLineage(e.Id))
                .Where(lr => lr is not null)
                .Where(lr => lr!.PredecessorId == strategyId || lr!.SecondPredecessorId == strategyId)
                .Select(lr => lr!.SuccessorId.Value)
                .ToList();

            lineage = new StrategyLineageViewModel
            {
                PredecessorId = lineageRecord.PredecessorId.Value,
                SecondPredecessorId = lineageRecord.SecondPredecessorId?.Value,
                Outcome = lineageRecord.Outcome,
                DiffSummary = lineageRecord.DiffSummary,
                SuccessorIds = successorIds,
            };
        }

        var winRate = entry.MatchesPlayed > 0
            ? (double)entry.Wins / entry.MatchesPlayed
            : 0.0;

        var iterationCount = ComputeIterationCount(entry.Id, pool);

        return new StrategyDetailViewModel
        {
            StrategyId = entry.Id.Value,
            Seat = entry.Seat,
            Kind = entry.Kind.ToString(),
            Source = entry.Source,
            MatchesPlayed = entry.MatchesPlayed,
            Wins = entry.Wins,
            Losses = entry.Losses,
            CumulativeScoreDifferential = entry.CumulativeScoreDifferential,
            Rating = entry.Rating,
            IsElite = entry.IsElite,
            WinRate = Math.Round(winRate, 4),
            Lineage = lineage,
            OpponentSummaries = BuildOpponentSummaries(entry, pool),
            RecentMatches = BuildRecentMatches(entry, pool),
        };
    }

    public StrategyLineageViewModel? BuildLineageViewModel(BonesStrategyId strategyId)
    {
        var pool = StrategyPool.Instance;
        if (pool is null)
            return null;

        if (!pool.Pool.ContainsKey(strategyId))
            return null;

        var lineageRecord = pool.GetLineage(strategyId);
        if (lineageRecord is null)
            return new StrategyLineageViewModel
            {
                PredecessorId = null,
                SecondPredecessorId = null,
                Outcome = "root",
                DiffSummary = "Root strategy - no lineage",
                SuccessorIds = pool.Pool.Values
                    .Select(e => pool.GetLineage(e.Id))
                    .Where(lr => lr is not null)
                    .Where(lr => lr!.PredecessorId == strategyId || lr!.SecondPredecessorId == strategyId)
                    .Select(lr => lr!.SuccessorId.Value)
                    .ToList(),
            };

        var successorIds = pool.Pool.Values
            .Select(e => pool.GetLineage(e.Id))
            .Where(lr => lr is not null)
            .Where(lr => lr!.PredecessorId == strategyId || lr!.SecondPredecessorId == strategyId)
            .Select(lr => lr!.SuccessorId.Value)
            .ToList();

        return new StrategyLineageViewModel
        {
            PredecessorId = lineageRecord.PredecessorId.Value,
            SecondPredecessorId = lineageRecord.SecondPredecessorId?.Value,
            Outcome = lineageRecord.Outcome,
            DiffSummary = lineageRecord.DiffSummary,
            SuccessorIds = successorIds,
        };
    }

    public PoolStatsViewModel BuildPoolStats()
    {
        var pool = StrategyPool.Instance;
        if (pool is null)
            return BuildEmptyStats();

        var entries = pool.Pool.Values.ToList();
        return BuildStats(entries);
    }

    private static PoolStatsViewModel BuildStats(List<StrategyEntry> entries)
    {
        if (entries.Count == 0)
            return BuildEmptyStats();

        var totalMatchesPlayed = entries.Sum(e => e.MatchesPlayed);
        var totalWins = entries.Sum(e => e.Wins);
        var averageElo = entries.Average(e => e.Rating);
        var eliteCount = entries.Count(e => e.IsElite);
        var byKind = entries
            .GroupBy(e => e.Kind.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        var bySeat = entries
            .GroupBy(e => e.Seat)
            .ToDictionary(g => g.Key, g => g.Count());
        var eloDistribution = BuildEloDistribution(entries);

        return new PoolStatsViewModel
        {
            TotalStrategies = entries.Count,
            TotalMatchesPlayed = totalMatchesPlayed,
            TotalWins = totalWins,
            AverageElo = Math.Round(averageElo, 1),
            EloDistribution = eloDistribution,
            EliteCount = eliteCount,
            StrategiesByKind = byKind,
            StrategiesBySeat = bySeat,
        };
    }

    private static PoolStatsViewModel BuildEmptyStats()
    {
        return new PoolStatsViewModel
        {
            TotalStrategies = 0,
            TotalMatchesPlayed = 0,
            TotalWins = 0,
            AverageElo = 0,
            EloDistribution = [],
            EliteCount = 0,
            StrategiesByKind = new Dictionary<string, int>(),
            StrategiesBySeat = new Dictionary<int, int>(),
        };
    }

    private static List<EloBucketViewModel> BuildEloDistribution(List<StrategyEntry> entries)
    {
        if (entries.Count == 0)
            return [];

        var minRating = entries.Min(e => e.Rating);
        var maxRating = entries.Max(e => e.Rating);

        const double bucketSize = 100.0;
        var bucketMin = Math.Floor(minRating / bucketSize) * bucketSize;
        var bucketMax = Math.Ceiling(maxRating / bucketSize) * bucketSize;

        var buckets = new List<EloBucketViewModel>();
        for (var low = bucketMin; low < bucketMax; low += bucketSize)
        {
            var high = low + bucketSize;
            var count = entries.Count(e => e.Rating >= low && e.Rating < high);
            buckets.Add(new EloBucketViewModel
            {
                Label = $"{low:F0}-{high:F0}",
                Min = low,
                Max = high,
                Count = count,
            });
        }

        return buckets;
    }

    private static PoolEntryViewModel CreateEntryViewModel(
        StrategyEntry entry,
        StrategyPool pool)
    {
        var winRate = entry.MatchesPlayed > 0
            ? (double)entry.Wins / entry.MatchesPlayed
            : 0.0;

        var iterationCount = ComputeIterationCount(entry.Id, pool);

        return new PoolEntryViewModel
        {
            Id = entry.Id.Value,
            Seat = entry.Seat,
            Kind = entry.Kind.ToString(),
            MatchesPlayed = entry.MatchesPlayed,
            Wins = entry.Wins,
            Losses = entry.Losses,
            CumulativeScoreDifferential = entry.CumulativeScoreDifferential,
            Rating = entry.Rating,
            IsElite = entry.IsElite,
            WinRate = Math.Round(winRate, 4),
            IterationCount = iterationCount,
        };
    }

    private static int ComputeIterationCount(BonesStrategyId strategyId, StrategyPool pool)
    {
        var depth = 0;
        var current = strategyId;
        var visited = new HashSet<BonesStrategyId>();

        while (true)
        {
            var lineage = pool.GetLineage(current);
            if (lineage is null)
                break;

            if (!visited.Add(current))
                break;

            if (lineage.PredecessorId == current)
                break;

            current = lineage.PredecessorId;
            depth++;
        }

        return depth;
    }

    private static List<OpponentSummaryViewModel> BuildOpponentSummaries(
        StrategyEntry entry,
        StrategyPool pool)
    {
        var opponentIds = new HashSet<BonesStrategyId>();

        foreach (var kvp in pool.Pool)
        {
            var lineage = pool.GetLineage(kvp.Key);
            if (lineage is null)
                continue;

            if (lineage.PredecessorId == entry.Id || lineage.SecondPredecessorId == entry.Id)
                opponentIds.Add(kvp.Key);
        }

        return opponentIds
            .Select(id =>
            {
                pool.Pool.TryGetValue(id, out var opponent);
                return new OpponentSummaryViewModel
                {
                    OpponentStrategyId = id.Value,
                    OpponentKind = opponent?.Kind.ToString() ?? "Unknown",
                    OpponentElo = opponent?.Rating ?? 0,
                    GamesAgainst = opponent?.MatchesPlayed ?? 0,
                    WinsAgainst = 0,
                    LossesAgainst = 0,
                    CumulativeScoreDifferentialAgainst = 0,
                };
            })
            .ToList();
    }

    private static List<MatchLinkViewModel> BuildRecentMatches(
        StrategyEntry entry,
        StrategyPool pool)
    {
        var matchLinks = new List<MatchLinkViewModel>();

        foreach (var kvp in pool.Pool)
        {
            var lineage = pool.GetLineage(kvp.Key);
            if (lineage is null)
                continue;

            if (lineage.PredecessorId == entry.Id || lineage.SecondPredecessorId == entry.Id)
            {
                matchLinks.Add(new MatchLinkViewModel
                {
                    SessionId = string.Empty,
                    MatchId = kvp.Key.Value,
                });
            }
        }

        return matchLinks;
    }
}
