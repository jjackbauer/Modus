using System.Text.Json.Serialization;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// A serializable snapshot of a <see cref="StrategyPool"/>'s entire state:
/// all pool entries, lineage records, elite designation, and best-score tracking.
/// Written to and read from disk for cross-restart persistence.
/// </summary>
public sealed record StrategyPoolSnapshot
{
    public StrategyPoolSnapshot(
        IReadOnlyList<PersistedPoolEntry> entries,
        IReadOnlyList<PersistedLineageRecord> lineageRecords,
        string? eliteStrategyId,
        double allTimeBestCompositeScore,
        DateTimeOffset savedAtUtc)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        LineageRecords = lineageRecords ?? throw new ArgumentNullException(nameof(lineageRecords));
        EliteStrategyId = eliteStrategyId;
        AllTimeBestCompositeScore = allTimeBestCompositeScore;
        SavedAtUtc = savedAtUtc;
    }

    /// <summary>
    /// All pool entries at the time of the snapshot.
    /// </summary>
    public IReadOnlyList<PersistedPoolEntry> Entries { get; }

    /// <summary>
    /// All lineage records recorded in the pool.
    /// </summary>
    public IReadOnlyList<PersistedLineageRecord> LineageRecords { get; }

    /// <summary>
    /// The strategy ID currently designated as all-time elite, or null.
    /// </summary>
    public string? EliteStrategyId { get; }

    /// <summary>
    /// The highest composite score ever observed in the pool session.
    /// </summary>
    public double AllTimeBestCompositeScore { get; }

    /// <summary>
    /// UTC timestamp when this snapshot was created.
    /// </summary>
    public DateTimeOffset SavedAtUtc { get; }
}

/// <summary>
/// A flat, serialization-friendly representation of a <see cref="StrategyEntry"/>
/// suitable for JSON persistence. Mirrors every field on StrategyEntry.
/// </summary>
public sealed record PersistedPoolEntry
{
    public PersistedPoolEntry(
        string strategyId,
        int seat,
        BonesStrategyKind kind,
        string source,
        int matchesPlayed,
        int wins,
        int losses,
        int cumulativeScoreDifferential,
        double rating,
        bool isElite)
    {
        StrategyId = strategyId ?? throw new ArgumentNullException(nameof(strategyId));
        Seat = seat;
        Kind = kind;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        MatchesPlayed = matchesPlayed;
        Wins = wins;
        Losses = losses;
        CumulativeScoreDifferential = cumulativeScoreDifferential;
        Rating = rating;
        IsElite = isElite;
    }

    public string StrategyId { get; }
    public int Seat { get; }
    public BonesStrategyKind Kind { get; }
    public string Source { get; }
    public int MatchesPlayed { get; }
    public int Wins { get; }
    public int Losses { get; }
    public int CumulativeScoreDifferential { get; }
    public double Rating { get; }
    public bool IsElite { get; }

    /// <summary>
    /// Creates a <see cref="PersistedPoolEntry"/> from a domain <see cref="StrategyEntry"/>.
    /// </summary>
    public static PersistedPoolEntry FromEntry(StrategyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new PersistedPoolEntry(
            entry.Id.Value,
            entry.Seat,
            entry.Kind,
            entry.Source,
            entry.MatchesPlayed,
            entry.Wins,
            entry.Losses,
            entry.CumulativeScoreDifferential,
            entry.Rating,
            entry.IsElite);
    }

    /// <summary>
    /// Converts back to a domain <see cref="StrategyEntry"/>. Throws on invalid data.
    /// </summary>
    public StrategyEntry ToEntry()
    {
        return new StrategyEntry(
            new BonesStrategyId(StrategyId),
            Seat,
            Kind,
            Source,
            MatchesPlayed,
            Wins,
            Losses,
            CumulativeScoreDifferential,
            Rating)
        {
            IsElite = IsElite
        };
    }
}

/// <summary>
/// Persistence-friendly representation of a <see cref="PoolLineageRecord"/>
/// using string IDs to avoid type-level coupling with BonesStrategyId serialization.
/// </summary>
public sealed record PersistedLineageRecord
{
    public PersistedLineageRecord(
        string successorId,
        string predecessorId,
        string? secondPredecessorId,
        string outcome,
        string diffSummary,
        DateTimeOffset recordedUtc)
    {
        SuccessorId = successorId ?? throw new ArgumentNullException(nameof(successorId));
        PredecessorId = predecessorId ?? throw new ArgumentNullException(nameof(predecessorId));
        SecondPredecessorId = secondPredecessorId;
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        DiffSummary = diffSummary ?? throw new ArgumentNullException(nameof(diffSummary));
        RecordedUtc = recordedUtc;
    }

    public string SuccessorId { get; }
    public string PredecessorId { get; }
    public string? SecondPredecessorId { get; }
    public string Outcome { get; }
    public string DiffSummary { get; }
    public DateTimeOffset RecordedUtc { get; }

    /// <summary>
    /// Creates a <see cref="PersistedLineageRecord"/> from a domain <see cref="PoolLineageRecord"/>.
    /// </summary>
    public static PersistedLineageRecord FromLineage(PoolLineageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new PersistedLineageRecord(
            record.SuccessorId.Value,
            record.PredecessorId.Value,
            record.SecondPredecessorId?.Value,
            record.Outcome,
            record.DiffSummary,
            record.RecordedUtc);
    }

    /// <summary>
    /// Converts back to a domain <see cref="PoolLineageRecord"/>.
    /// </summary>
    public PoolLineageRecord ToLineage()
    {
        return new PoolLineageRecord(
            new BonesStrategyId(SuccessorId),
            new BonesStrategyId(PredecessorId),
            SecondPredecessorId is not null ? new BonesStrategyId(SecondPredecessorId) : null,
            Outcome,
            DiffSummary,
            RecordedUtc);
    }
}
