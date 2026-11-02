using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Web.Viewer;

/// <summary>
/// Full snapshot of the strategy pool returned by GET /bones/pool.
/// </summary>
public sealed record PoolSnapshot
{
    public required List<PoolEntryViewModel> Entries { get; init; }
    public required PoolStatsViewModel Stats { get; init; }
    public bool IsRunning { get; init; }
}

/// <summary>
/// Simplified view model for a single pool entry displayed in card view.
/// </summary>
public sealed record PoolEntryViewModel
{
    public required string Id { get; init; }
    public required int Seat { get; init; }
    public required string Kind { get; init; }
    public required int MatchesPlayed { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }
    public required int CumulativeScoreDifferential { get; init; }
    public required double Rating { get; init; }
    public required bool IsElite { get; init; }
    public required double WinRate { get; init; }
    public required int IterationCount { get; init; }
}

/// <summary>
/// Detailed view model for a single strategy returned by GET /bones/pool/{id}.
/// </summary>
public sealed record StrategyDetailViewModel
{
    public required string StrategyId { get; init; }
    public required int Seat { get; init; }
    public required string Kind { get; init; }
    public required string Source { get; init; }
    public required int MatchesPlayed { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }
    public required int CumulativeScoreDifferential { get; init; }
    public required double Rating { get; init; }
    public required bool IsElite { get; init; }
    public required double WinRate { get; init; }
    public StrategyLineageViewModel? Lineage { get; init; }
    public required List<OpponentSummaryViewModel> OpponentSummaries { get; init; }
    public required List<MatchLinkViewModel> RecentMatches { get; init; }
}

/// <summary>
/// Lineage view model for a strategy ancestry tree.
/// </summary>
public sealed record StrategyLineageViewModel
{
    public string? PredecessorId { get; init; }
    public string? SecondPredecessorId { get; init; }
    public required string Outcome { get; init; }
    public required string DiffSummary { get; init; }
    public required List<string> SuccessorIds { get; init; }
}

/// <summary>
/// Opponent performance summary for the detail view.
/// </summary>
public sealed record OpponentSummaryViewModel
{
    public required string OpponentStrategyId { get; init; }
    public required string OpponentKind { get; init; }
    public required double OpponentElo { get; init; }
    public required int GamesAgainst { get; init; }
    public required int WinsAgainst { get; init; }
    public required int LossesAgainst { get; init; }
    public required int CumulativeScoreDifferentialAgainst { get; init; }
}

/// <summary>
/// A link to a match replay.
/// </summary>
public sealed record MatchLinkViewModel
{
    public required string SessionId { get; init; }
    public required string MatchId { get; init; }
}

/// <summary>
/// Aggregate pool statistics returned by GET /bones/pool/stats.
/// </summary>
public sealed record PoolStatsViewModel
{
    public required int TotalStrategies { get; init; }
    public required int TotalMatchesPlayed { get; init; }
    public required int TotalWins { get; init; }
    public required double AverageElo { get; init; }
    public required List<EloBucketViewModel> EloDistribution { get; init; }
    public required int EliteCount { get; init; }
    public required Dictionary<string, int> StrategiesByKind { get; init; }
    public required Dictionary<int, int> StrategiesBySeat { get; init; }
}

/// <summary>
/// A single ELO distribution histogram bucket.
/// </summary>
public sealed record EloBucketViewModel
{
    public required string Label { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required int Count { get; init; }
}
