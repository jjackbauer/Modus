using System.Text.Json;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public enum BonesLibrarySeatIntegrityStatus
{
    Present,
    Corrupt,
    Missing,
}

public sealed record BonesLibrarySeatIntegrity(
    int Seat,
    BonesLibrarySeatIntegrityStatus Status);

public sealed record BonesStrategyLibraryEntry(
    BonesStrategyId StrategyId,
    BonesPlayerId PlayerId,
    BonesStrategyKind Kind,
    string Source,
    BonesStrategyEffectivenessRecord Effectiveness,
    DateTimeOffset LastUpdatedUtc);

internal sealed record BonesStrategyLibraryEntryPersistence(
    string StrategyId,
    int PlayerSeat,
    BonesStrategyKind Kind,
    string Source,
    int MatchesPlayed,
    int Wins,
    int Losses,
    int CumulativeScoreDifferential,
    DateTimeOffset LastUpdatedUtc)
{
    public static BonesStrategyLibraryEntryPersistence FromEntry(BonesStrategyLibraryEntry entry)
        => new(
            entry.StrategyId.Value,
            entry.PlayerId.Seat,
            entry.Kind,
            entry.Source,
            entry.Effectiveness.MatchesPlayed,
            entry.Effectiveness.Wins,
            entry.Effectiveness.Losses,
            entry.Effectiveness.CumulativeScoreDifferential,
            entry.LastUpdatedUtc);

    public BonesStrategyLibraryEntry ToEntry(BonesPlayerId playerId)
    {
        if (playerId.Seat != PlayerSeat)
        {
            throw new InvalidOperationException(
                $"Library entry seat {PlayerSeat} does not match player seat {playerId.Seat}.");
        }

        return new BonesStrategyLibraryEntry(
            new BonesStrategyId(StrategyId),
            playerId,
            Kind,
            Source,
            new BonesStrategyEffectivenessRecord(
                new BonesStrategyId(StrategyId),
                playerId,
                MatchesPlayed,
                Wins,
                Losses,
                CumulativeScoreDifferential),
            LastUpdatedUtc);
    }
}

public sealed class BonesStrategyLibrary
{
    private const string LibraryDirectoryName = ".bones";
    private const string StrategyLibrarySegment = "strategy-library";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _libraryRoot;

    public BonesStrategyLibrary(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(dataDirectory));

        _libraryRoot = Path.Combine(
            Path.GetFullPath(dataDirectory),
            LibraryDirectoryName,
            StrategyLibrarySegment);
    }

    public BonesStrategyArtifact? LoadBestStrategy(BonesPlayerId playerId)
    {
        var ranking = GetRanking(playerId);
        if (ranking.Count == 0)
            return null;

        var best = ranking[0];
        return new BonesStrategyArtifact(
            best.StrategyId,
            best.PlayerId,
            best.Kind,
            best.Source,
            BonesPromotionStatus.Active);
    }

    public IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> LoadBestStrategiesForAllSeats()
    {
        var results = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();
        for (var seat = 1; seat <= 4; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            var best = LoadBestStrategy(playerId);
            if (best is not null)
                results[playerId] = best;
        }

        return results;
    }

    public (IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact> Strategies,
            IReadOnlyList<BonesLibrarySeatIntegrity> IntegrityReport) VerifyIntegrityWithReport()
    {
        var strategies = LoadBestStrategiesForAllSeats();
        var integrity = VerifyIntegrity();
        return (strategies, integrity);
    }

    public IReadOnlyList<BonesStrategyLibraryEntry> GetRanking(BonesPlayerId playerId)
    {
        var entry = TryLoadEntry(playerId);
        return entry is null
            ? Array.Empty<BonesStrategyLibraryEntry>()
            : BonesStrategyLibraryRanking.SortDescending([entry]);
    }

    public bool UpsertBestStrategy(
        BonesPlayerId playerId,
        BonesStrategyArtifact artifact,
        BonesStrategyEffectivenessRecord metrics)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(metrics);

        if (artifact.PlayerId != playerId)
        {
            throw new InvalidOperationException(
                $"Artifact player seat {artifact.PlayerId.Seat} does not match seat {playerId.Seat}.");
        }

        if (metrics.PlayerId != playerId || metrics.StrategyId != artifact.StrategyId)
        {
            throw new InvalidOperationException(
                "Effectiveness metrics must match the supplied player and strategy identifiers.");
        }

        var candidateEntry = new BonesStrategyLibraryEntry(
            artifact.StrategyId,
            playerId,
            artifact.Kind,
            artifact.Source,
            metrics,
            DateTimeOffset.UtcNow);

        var existingEntry = TryLoadEntry(playerId);
        if (!BonesStrategyLibraryRanking.RanksHigher(candidateEntry, existingEntry))
            return false;

        Directory.CreateDirectory(_libraryRoot);
        var path = GetSeatEntryPath(playerId);
        var json = JsonSerializer.Serialize(BonesStrategyLibraryEntryPersistence.FromEntry(candidateEntry), JsonOptions);
        AtomicWriteAllText(path, json);
        return true;
    }

    public IReadOnlyList<BonesStrategyLibraryEntry> AllEntries
    {
        get
        {
            if (!Directory.Exists(_libraryRoot))
                return Array.Empty<BonesStrategyLibraryEntry>();

            var entries = new List<BonesStrategyLibraryEntry>();
            for (var seat = 1; seat <= 4; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                var entry = TryLoadEntry(playerId);
                if (entry is not null)
                    entries.Add(entry);
            }

            return entries;
        }
    }

    public IReadOnlyList<BonesLibrarySeatIntegrity> VerifyIntegrity()
    {
        var results = new List<BonesLibrarySeatIntegrity>();
        for (var seat = 1; seat <= 4; seat++)
        {
            var path = GetSeatEntryPath(new BonesPlayerId(seat));
            if (!File.Exists(path))
            {
                results.Add(new BonesLibrarySeatIntegrity(seat, BonesLibrarySeatIntegrityStatus.Missing));
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);
                var persistence = JsonSerializer.Deserialize<BonesStrategyLibraryEntryPersistence>(json, JsonOptions);
                if (persistence is null)
                    throw new JsonException("Deserialized to null.");

                results.Add(new BonesLibrarySeatIntegrity(seat, BonesLibrarySeatIntegrityStatus.Present));
            }
            catch (JsonException)
            {
                results.Add(new BonesLibrarySeatIntegrity(seat, BonesLibrarySeatIntegrityStatus.Corrupt));
            }
        }

        return results;
    }

    public bool UpsertCandidate(
        BonesPlayerId playerId,
        BonesStrategyArtifact artifact,
        BonesPromotionEvaluationMetrics evaluationMetrics)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(evaluationMetrics);

        if (artifact.PlayerId != playerId)
        {
            throw new InvalidOperationException(
                $"Artifact player seat {artifact.PlayerId.Seat} does not match seat {playerId.Seat}.");
        }

        var candidates = LoadCandidatesInternal(playerId);
        var candidate = new BonesStrategyCandidateEntry(
            artifact.StrategyId,
            playerId,
            artifact.Kind,
            artifact.Source,
            evaluationMetrics,
            DateTimeOffset.UtcNow);

        // Avoid duplicates by strategy ID
        candidates = candidates
            .Where(c => c.StrategyId != artifact.StrategyId)
            .Append(candidate)
            .OrderByDescending(c => c.EvaluationMetrics.ScoreDifferentialDelta)
            .Take(MaxCandidateCount)
            .ToArray();

        SaveCandidatesInternal(playerId, candidates);
        return true;
    }

    public IReadOnlyList<BonesStrategyCandidateEntry> LoadCandidates(BonesPlayerId playerId)
        => LoadCandidatesInternal(playerId);

    private const int MaxCandidateCount = 10;
    private const string CandidateFileSuffix = "-candidates";

    private BonesStrategyCandidateEntry[] LoadCandidatesInternal(BonesPlayerId playerId)
    {
        var path = GetCandidateFilePath(playerId);
        if (!File.Exists(path))
            return Array.Empty<BonesStrategyCandidateEntry>();

        try
        {
            var json = File.ReadAllText(path);
            var persisted = JsonSerializer.Deserialize<BonesStrategyCandidatePersistence[]>(json, JsonOptions);
            return persisted?.Select(p => p.ToEntry(playerId)).ToArray()
                ?? Array.Empty<BonesStrategyCandidateEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<BonesStrategyCandidateEntry>();
        }
    }

    private void SaveCandidatesInternal(BonesPlayerId playerId, IEnumerable<BonesStrategyCandidateEntry> candidates)
    {
        Directory.CreateDirectory(_libraryRoot);
        var path = GetCandidateFilePath(playerId);
        var persisted = candidates
            .Select(BonesStrategyCandidatePersistence.FromEntry)
            .ToArray();
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        AtomicWriteAllText(path, json);
    }

    private string GetCandidateFilePath(BonesPlayerId playerId)
        => Path.Combine(_libraryRoot, $"seat-{playerId.Seat}{CandidateFileSuffix}.json");

    internal BonesStrategyLibraryEntry? TryLoadEntryForTests(BonesPlayerId playerId)
        => TryLoadEntry(playerId);

    private static void AtomicWriteAllText(string path, string json)
    {
        var tempPath = path + ".tmp" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private BonesStrategyLibraryEntry? TryLoadEntry(BonesPlayerId playerId)
    {
        var path = GetSeatEntryPath(playerId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var persistence = JsonSerializer.Deserialize<BonesStrategyLibraryEntryPersistence>(json, JsonOptions);
            return persistence?.ToEntry(playerId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetSeatEntryPath(BonesPlayerId playerId)
        => Path.Combine(_libraryRoot, $"seat-{playerId.Seat}.json");
}
