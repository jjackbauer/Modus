using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public sealed class BonesStrategyLineageStore
{
    private const string ProducerType = "Wip.Bones.Agents.BonesStrategyLineageStore";
    private const string ProducerVersion = "1.0.0";
    private const string LineageCategory = "lineage";
    private const string PlayerPathSegmentPrefix = "bones-player-";

    private readonly IArtifactStore _artifactStore;
    private readonly SessionId _sessionId;
    private readonly BonesPlayerId? _accessScope;

    public BonesStrategyLineageStore(
        IArtifactStore artifactStore,
        SessionId sessionId,
        BonesPlayerId? accessScope = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _sessionId = sessionId;
        _accessScope = accessScope;
    }

    public BonesStrategyLineageStore ForPlayer(BonesPlayerId playerId)
        => new(_artifactStore, _sessionId, playerId);

    public async ValueTask RecordVersion(BonesStrategyLineageEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureWriteAccess(entry.PlayerId);

        var persistence = BonesStrategyLineagePersistence.FromEntry(entry);
        var fileName = BuildLineageFileName(entry.PlayerId, entry.SuccessorStrategyId);
        var artifactId = fileName;

        await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId(artifactId),
                kind: ArtifactKind.Json,
                fileName: fileName,
                content: JsonSerializer.Serialize(persistence),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<BonesStrategyLineageEntry>> LoadLineage(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);
        var scopedPrefix = BuildLineageFileNamePrefix(playerId);

        var lineageDescriptors = descriptors
            .Where(descriptor =>
            {
                var fileName = Path.GetFileName(descriptor.RelativePath.Replace('\\', '/'));
                return fileName.StartsWith(scopedPrefix, StringComparison.Ordinal);
            })
            .ToList();

        var entries = new List<BonesStrategyLineageEntry>(lineageDescriptors.Count);
        foreach (var descriptor in lineageDescriptors.OrderBy(d => d.ProducedAtUtc))
        {
            try
            {
                var content = await ReadArtifactContentAsync(descriptor, cancellationToken);
                var persistence = JsonSerializer.Deserialize<BonesStrategyLineagePersistence>(content);
                if (persistence is not null)
                {
                    entries.Add(persistence.ToEntry());
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Skip corrupt lineage entries; they do not block lineage loading
            }
        }

        return entries;
    }

    private void EnsureReadAccess(BonesPlayerId playerId)
    {
        if (_accessScope is { } scope && scope.Seat != playerId.Seat)
        {
            throw new InvalidOperationException(
                $"Isolation gate: player seat {scope.Seat} cannot read lineage for seat {playerId.Seat}.");
        }
    }

    private void EnsureWriteAccess(BonesPlayerId playerId)
    {
        if (_accessScope is { } scope && scope.Seat != playerId.Seat)
        {
            throw new InvalidOperationException(
                $"Player seat {scope.Seat} cannot write lineage for seat {playerId.Seat}.");
        }
    }

    private static string BuildLineageFileNamePrefix(BonesPlayerId playerId)
        => $"{PlayerPathSegmentPrefix}{playerId.Seat}-{LineageCategory}-";

    private static string BuildLineageFileName(BonesPlayerId playerId, BonesStrategyId strategyId)
        => $"{BuildLineageFileNamePrefix(playerId)}{strategyId.Value}";

    private async ValueTask<string> ReadArtifactContentAsync(
        ArtifactDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var result = await _artifactStore.LoadAsync(_sessionId, descriptor.ArtifactId, cancellationToken);
        return result.Content;
    }
}
