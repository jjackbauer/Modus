using System.Text.Json.Serialization;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Domain;

/// <summary>
/// A pool entry representing a single strategy with cumulative match effectiveness
/// and a continuously-updated ELO rating.
/// </summary>
public sealed record StrategyEntry
{
    public StrategyEntry(
        BonesStrategyId id,
        int seat,
        BonesStrategyKind kind,
        string source,
        int matchesPlayed,
        int wins,
        int losses,
        int cumulativeScoreDifferential,
        double rating)
    {
        Id = id;
        Seat = seat;
        Kind = kind;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        MatchesPlayed = matchesPlayed;
        Wins = wins;
        Losses = losses;
        CumulativeScoreDifferential = cumulativeScoreDifferential;
        Rating = rating;

        if (seat < BonesPlayerId.MinSeat || seat > BonesPlayerId.MaxSeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat), seat,
                $"Seat must be between {BonesPlayerId.MinSeat} and {BonesPlayerId.MaxSeat}.");
        }

        if (matchesPlayed < 0)
            throw new ArgumentOutOfRangeException(nameof(matchesPlayed));

        if (wins < 0)
            throw new ArgumentOutOfRangeException(nameof(wins));

        if (losses < 0)
            throw new ArgumentOutOfRangeException(nameof(losses));

        if (wins + losses > matchesPlayed)
        {
            throw new ArgumentException(
                "Wins and losses cannot exceed matches played.", nameof(matchesPlayed));
        }
    }

    public BonesStrategyId Id { get; }
    public int Seat { get; }
    public BonesStrategyKind Kind { get; }
    public string Source { get; }
    public int MatchesPlayed { get; }
    public int Wins { get; }
    public int Losses { get; }
    public int CumulativeScoreDifferential { get; }
    public double Rating { get; }

    /// <summary>
    /// When true, this strategy is elite-protected and cannot be pruned
    /// regardless of its composite score rank.
    /// </summary>
    public bool IsElite { get; init; }

    /// <summary>
    /// Convenience: the corresponding BonesPlayerId for this entry's seat.
    /// </summary>
    [JsonIgnore]
    public BonesPlayerId PlayerId => new(Seat);
}

/// <summary>
/// A 4-player matchup extracted from the pool for a single game.
/// </summary>
public sealed record Matchup
{
    public Matchup(IReadOnlyList<StrategyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count != 4)
            throw new ArgumentException("A matchup must contain exactly 4 entries.", nameof(entries));

        Entries = entries;
    }

    public IReadOnlyList<StrategyEntry> Entries { get; }
}

/// <summary>
/// Result of an evaluate-and-promote cycle on the pool.
/// </summary>
public sealed record PromotionResults
{
    public static readonly PromotionResults Empty = new(0, 0);

    public PromotionResults(int promoted, int rejected)
    {
        Promoted = promoted;
        Rejected = rejected;
    }

    public int Promoted { get; }
    public int Rejected { get; }
}

/// <summary>
/// Context passed to the enhancement LLM for a pool strategy, including
/// all match histories from the evaluation cycle, opponent summaries,
/// and ELO deltas.
/// </summary>
public sealed record StrategyEnhancementContext
{
    public StrategyEnhancementContext(
        BonesStrategyId strategyId,
        int totalMatchesInCycle,
        double currentElo,
        double eloDeltaInCycle,
        IReadOnlyList<OpponentPerformanceSummary> opponentSummaries,
        double averageOpponentElo,
        int winsInCycle,
        int lossesInCycle,
        int cumulativeScoreDifferential,
        int allTimeWins,
        int allTimeLosses,
        double winRate)
    {
        StrategyId = strategyId;
        TotalMatchesInCycle = totalMatchesInCycle;
        CurrentElo = currentElo;
        EloDeltaInCycle = eloDeltaInCycle;
        OpponentSummaries = opponentSummaries;
        AverageOpponentElo = averageOpponentElo;
        WinsInCycle = winsInCycle;
        LossesInCycle = lossesInCycle;
        CumulativeScoreDifferential = cumulativeScoreDifferential;
        AllTimeWins = allTimeWins;
        AllTimeLosses = allTimeLosses;
        WinRate = winRate;
    }

    public BonesStrategyId StrategyId { get; }
    public int TotalMatchesInCycle { get; }
    public double CurrentElo { get; }
    public double EloDeltaInCycle { get; }
    public IReadOnlyList<OpponentPerformanceSummary> OpponentSummaries { get; }
    public double AverageOpponentElo { get; }
    public int WinsInCycle { get; }
    public int LossesInCycle { get; }
    public int CumulativeScoreDifferential { get; }
    public int AllTimeWins { get; }
    public int AllTimeLosses { get; }
    public double WinRate { get; }
}

/// <summary>
/// Summarises how a strategy performed against a specific opponent.
/// </summary>
public sealed record OpponentPerformanceSummary
{
    public OpponentPerformanceSummary(
        BonesStrategyId opponentStrategyId,
        BonesStrategyKind opponentKind,
        double opponentElo,
        int gamesAgainst,
        int winsAgainst,
        int lossesAgainst,
        int cumulativeScoreDifferentialAgainst)
    {
        OpponentStrategyId = opponentStrategyId;
        OpponentKind = opponentKind;
        OpponentElo = opponentElo;
        GamesAgainst = gamesAgainst;
        WinsAgainst = winsAgainst;
        LossesAgainst = lossesAgainst;
        CumulativeScoreDifferentialAgainst = cumulativeScoreDifferentialAgainst;
    }

    public BonesStrategyId OpponentStrategyId { get; }
    public BonesStrategyKind OpponentKind { get; }
    public double OpponentElo { get; }
    public int GamesAgainst { get; }
    public int WinsAgainst { get; }
    public int LossesAgainst { get; }
    public int CumulativeScoreDifferentialAgainst { get; }
}
