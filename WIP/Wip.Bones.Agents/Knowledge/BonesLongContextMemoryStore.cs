using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public sealed record BonesDiscoveredPattern(
    string PatternDescription,
    double Confidence,
    DateTimeOffset DiscoveredAtUtc);

public sealed record BonesFailedApproach(
    string ApproachDescription,
    int RejectionCount,
    bool IsMarkedDeadEnd);

public sealed class BonesLongContextMemoryStore
{
    private const string ProducerType = "Wip.Bones.Agents.BonesLongContextMemoryStore";
    private const string ProducerVersion = "1.0.0";
    private const string ContextDirectorySegment = "long-context-memory";
    private const int DeadEndRejectionThreshold = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IArtifactStore _artifactStore;
    private readonly string _memoryRootSegment;

    public BonesLongContextMemoryStore(IArtifactStore artifactStore, string dataDirectory)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(dataDirectory));

        _memoryRootSegment = Path.Combine(dataDirectory, ".bones", ContextDirectorySegment);
        Directory.CreateDirectory(_memoryRootSegment);
    }

    public async Task StorePatternAsync(
        BonesPlayerId playerId,
        BonesDiscoveredPattern pattern,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var existing = await LoadPatternsAsync(playerId, cancellationToken);
        var updated = existing.Append(pattern).ToArray();

        await PersistPatternsAsync(playerId, updated, cancellationToken);
    }

    public async Task<IReadOnlyList<BonesDiscoveredPattern>> LoadPatternsAsync(
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        var path = GetPatternsPath(playerId);
        if (!File.Exists(path))
            return Array.Empty<BonesDiscoveredPattern>();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var persistenceList = JsonSerializer.Deserialize<List<BonesDiscoveredPatternPersistence>>(json, JsonOptions);
        return persistenceList?.Select(p => p.ToPattern()).ToArray()
            ?? Array.Empty<BonesDiscoveredPattern>();
    }

    public async Task RecordFailedApproachAsync(
        BonesPlayerId playerId,
        BonesFailedApproach approach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approach);

        var existing = await LoadFailedApproachesAsync(playerId, cancellationToken);
        var existingList = existing.ToList();

        var existingMatch = existingList.FirstOrDefault(a =>
            string.Equals(a.ApproachDescription, approach.ApproachDescription, StringComparison.Ordinal));

        if (existingMatch is not null)
        {
            var updatedCount = existingMatch.RejectionCount + 1;
            var isDeadEnd = updatedCount >= DeadEndRejectionThreshold;
            var updated = existingMatch with { RejectionCount = updatedCount, IsMarkedDeadEnd = isDeadEnd };
            existingList[existingList.IndexOf(existingMatch)] = updated;
        }
        else
        {
            existingList.Add(approach with
            {
                RejectionCount = approach.RejectionCount + 1,
                IsMarkedDeadEnd = (approach.RejectionCount + 1) >= DeadEndRejectionThreshold,
            });
        }

        await PersistFailedApproachesAsync(playerId, existingList, cancellationToken);
    }

    public async Task<IReadOnlyList<BonesFailedApproach>> LoadFailedApproachesAsync(
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        var path = GetFailedApproachesPath(playerId);
        if (!File.Exists(path))
            return Array.Empty<BonesFailedApproach>();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var persistenceList = JsonSerializer.Deserialize<List<BonesFailedApproachPersistence>>(json, JsonOptions);
        return persistenceList?.Select(p => p.ToFailedApproach()).ToArray()
            ?? Array.Empty<BonesFailedApproach>();
    }

    public string BuildEnhancementContext(
        BonesPlayerId playerId,
        IReadOnlyList<BonesDiscoveredPattern> patterns,
        IReadOnlyList<BonesFailedApproach> failedApproaches)
    {
        var lines = new List<string>();
        lines.Add("## Long-context memory");
        lines.Add(string.Empty);

        if (patterns.Count > 0)
        {
            lines.Add("### Discovered patterns");
            foreach (var pattern in patterns.OrderByDescending(p => p.Confidence))
            {
                lines.Add($"- **{pattern.PatternDescription}** (confidence: {pattern.Confidence:P0}, discovered: {pattern.DiscoveredAtUtc:yyyy-MM-dd})");
            }

            lines.Add(string.Empty);
        }

        if (failedApproaches.Count > 0)
        {
            lines.Add("### Failed approaches");
            foreach (var approach in failedApproaches)
            {
                var deadEndLabel = approach.IsMarkedDeadEnd ? " [DEAD END]" : string.Empty;
                lines.Add($"- {approach.ApproachDescription} (rejections: {approach.RejectionCount}){deadEndLabel}");
            }

            lines.Add(string.Empty);

            var deadEnds = failedApproaches.Where(a => a.IsMarkedDeadEnd).ToArray();
            if (deadEnds.Length > 0)
            {
                lines.Add($"Avoid re-exploring the {deadEnds.Length} approach(es) marked [DEAD END] above.");
            }
        }

        if (patterns.Count == 0 && failedApproaches.Count == 0)
        {
            lines.Add("No historical patterns or failed approaches have been recorded for this player.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task PersistPatternsAsync(
        BonesPlayerId playerId,
        IReadOnlyList<BonesDiscoveredPattern> patterns,
        CancellationToken cancellationToken)
    {
        var persistenceList = patterns.Select(BonesDiscoveredPatternPersistence.FromPattern).ToList();
        var json = JsonSerializer.Serialize(persistenceList, JsonOptions);
        await AtomicWriteAllTextAsync(GetPatternsPath(playerId), json, cancellationToken);
    }

    private async Task PersistFailedApproachesAsync(
        BonesPlayerId playerId,
        IReadOnlyList<BonesFailedApproach> approaches,
        CancellationToken cancellationToken)
    {
        var persistenceList = approaches.Select(BonesFailedApproachPersistence.FromFailedApproach).ToList();
        var json = JsonSerializer.Serialize(persistenceList, JsonOptions);
        await AtomicWriteAllTextAsync(GetFailedApproachesPath(playerId), json, cancellationToken);
    }

    private static async Task AtomicWriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetPatternsPath(BonesPlayerId playerId)
        => Path.Combine(_memoryRootSegment, $"patterns-seat-{playerId.Seat}.json");

    private string GetFailedApproachesPath(BonesPlayerId playerId)
        => Path.Combine(_memoryRootSegment, $"failed-approaches-seat-{playerId.Seat}.json");

    internal sealed record BonesDiscoveredPatternPersistence(
        string PatternDescription,
        double Confidence,
        DateTimeOffset DiscoveredAtUtc)
    {
        public static BonesDiscoveredPatternPersistence FromPattern(BonesDiscoveredPattern pattern) => new(
            pattern.PatternDescription,
            pattern.Confidence,
            pattern.DiscoveredAtUtc);

        public BonesDiscoveredPattern ToPattern() => new(PatternDescription, Confidence, DiscoveredAtUtc);
    }

    internal sealed record BonesFailedApproachPersistence(
        string ApproachDescription,
        int RejectionCount,
        bool IsMarkedDeadEnd)
    {
        public static BonesFailedApproachPersistence FromFailedApproach(BonesFailedApproach approach) => new(
            approach.ApproachDescription,
            approach.RejectionCount,
            approach.IsMarkedDeadEnd);

        public BonesFailedApproach ToFailedApproach() => new(ApproachDescription, RejectionCount, IsMarkedDeadEnd);
    }
}
