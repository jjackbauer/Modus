using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public sealed class BonesPlayerKnowledgeStore
{
    private const string ProducerType = "Wip.Bones.Agents.BonesPlayerKnowledgeStore";
    private const string ProducerVersion = "1.0.0";
    private const string PlayerPathSegmentPrefix = "bones-player-";
    private const string StrategyCategory = "strategy";
    private const string ObservationCategory = "observation";
    private const string MatchCategory = "match";
    private const string EffectivenessCategory = "effectiveness";
    private const string ActiveStrategyArtifactPrefix = "bones-active-strategy-seat-";

    private readonly IArtifactStore _artifactStore;
    private readonly string _repositoryPath;
    private readonly SessionId _sessionId;
    private readonly BonesPlayerId? _accessScope;
    private readonly ILogger<BonesPlayerKnowledgeStore>? _logger;

    public BonesPlayerKnowledgeStore(
        IArtifactStore artifactStore,
        string repositoryPath,
        SessionId sessionId,
        BonesPlayerId? accessScope = null,
        ILogger<BonesPlayerKnowledgeStore>? logger = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));

        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(repositoryPath));

        _repositoryPath = repositoryPath;
        _sessionId = sessionId;
        _accessScope = accessScope;
        _logger = logger;
    }

    public BonesPlayerKnowledgeStore ForPlayer(BonesPlayerId playerId)
        => new(_artifactStore, _repositoryPath, _sessionId, playerId, logger: _logger);

    public async ValueTask<BonesStrategyDocument> LoadStrategy(
        BonesPlayerId playerId,
        SessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureSessionScope(sessionId);

        var activeStrategy = await LoadActiveStrategy(playerId, cancellationToken);
        return new BonesStrategyDocument(activeStrategy.StrategyId, playerId, activeStrategy.Source);
    }

    public async ValueTask<BonesStrategyArtifact> LoadActiveStrategy(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var strategyId = await LoadActiveStrategyIdAsync(playerId, cancellationToken);
        return await LoadStrategyArtifact(playerId, strategyId, cancellationToken);
    }

    public async ValueTask<BonesStrategyArtifact?> TryLoadActiveStrategy(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var strategyId = await TryLoadActiveStrategyIdAsync(playerId, cancellationToken);
        if (strategyId is not { } activeStrategyId)
            return null;

        return await LoadStrategyArtifact(playerId, activeStrategyId, cancellationToken);
    }

    public async ValueTask<ArtifactDescriptor> SaveCandidateStrategy(
        BonesPlayerId playerId,
        BonesStrategyArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EnsurePlayerOwnership(playerId, artifact.PlayerId);

        var candidate = new BonesStrategyArtifact(
            artifact.StrategyId,
            artifact.PlayerId,
            artifact.Kind,
            artifact.Source,
            BonesPromotionStatus.Candidate);

        return await SaveStrategyArtifact(playerId, candidate, cancellationToken);
    }

    public async ValueTask PromoteStrategy(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var candidate = await LoadStrategyArtifact(playerId, strategyId, cancellationToken);
        EnsurePlayerOwnership(playerId, candidate.PlayerId);

        if (candidate.PromotionStatus is not BonesPromotionStatus.Candidate and not BonesPromotionStatus.Active)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId.Value}' cannot be promoted from status '{candidate.PromotionStatus}'.");
        }

        var activeStrategyId = await TryLoadActiveStrategyIdAsync(playerId, cancellationToken);
        if (activeStrategyId is { } incumbentId
            && incumbentId != strategyId)
        {
            var incumbent = await LoadStrategyArtifact(playerId, incumbentId, cancellationToken);
            var superseded = new BonesStrategyArtifact(
                incumbent.StrategyId,
                incumbent.PlayerId,
                incumbent.Kind,
                incumbent.Source,
                BonesPromotionStatus.Superseded);

            await SaveStrategyArtifact(playerId, superseded, cancellationToken);
        }

        if (candidate.PromotionStatus != BonesPromotionStatus.Active)
        {
            var promoted = new BonesStrategyArtifact(
                candidate.StrategyId,
                candidate.PlayerId,
                candidate.Kind,
                candidate.Source,
                BonesPromotionStatus.Active);

            await SaveStrategyArtifact(playerId, promoted, cancellationToken);
        }

        await SaveActiveStrategyPointerAsync(playerId, strategyId, cancellationToken);
    }

    public async ValueTask<BonesStrategyEffectivenessRecord> GetEffectiveness(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        try
        {
            return await LoadEffectivenessRecord(playerId, strategyId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogWarning(ex,
                "Effectiveness record for strategy {StrategyId} of player seat {Seat} is corrupt or missing; returning empty record.",
                strategyId.Value,
                playerId.Seat);
            return BonesStrategyEffectivenessRecord.Empty(strategyId, playerId);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "Effectiveness record for strategy {StrategyId} of player seat {Seat} contains invalid JSON; returning empty record.",
                strategyId.Value,
                playerId.Seat);
            return BonesStrategyEffectivenessRecord.Empty(strategyId, playerId);
        }
    }

    public async ValueTask RecordMatchOutcome(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        BonesStrategyMatchOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureReadAccess(playerId);

        var scoreDifferential = outcome.GetScoreDifferentialForPlayer(playerId);
        var record = await GetEffectiveness(playerId, strategyId, cancellationToken);
        var updated = playerId == outcome.Winner
            ? record.RecordWin(scoreDifferential)
            : record.RecordLoss(scoreDifferential);

        await SaveEffectivenessRecord(playerId, updated, cancellationToken);
    }

    public async ValueTask<BonesStrategyId?> TryLoadCandidateStrategyId(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var versions = await ListStrategyVersions(playerId, cancellationToken);
        for (var index = versions.Count - 1; index >= 0; index--)
        {
            if (versions[index].PromotionStatus == BonesPromotionStatus.Candidate)
                return versions[index].StrategyId;
        }

        return null;
    }

    public async ValueTask<IReadOnlyList<BonesStrategyArtifact>> ListStrategyVersions(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await ListStrategyArtifacts(playerId, cancellationToken);
        var versions = new List<BonesStrategyArtifact>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            var strategyId = ExtractStrategyId(descriptor, playerId);
            versions.Add(await LoadStrategyArtifact(playerId, strategyId, cancellationToken));
        }

        return versions;
    }

    public async ValueTask<ArtifactDescriptor> SaveStrategy(
        BonesPlayerId playerId,
        BonesStrategyDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsurePlayerOwnership(playerId, document.PlayerId);

        var fileName = BuildScopedFileName(playerId, StrategyCategory, document.StrategyId.Value);

        var descriptor = await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-strategy-{document.StrategyId.Value}"),
                kind: ArtifactKind.Markdown,
                fileName: fileName,
                content: document.Markdown,
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        await SaveActiveStrategyPointerAsync(playerId, document.StrategyId, cancellationToken);
        return descriptor;
    }

    public async ValueTask<ArtifactDescriptor> SaveStrategyArtifact(
        BonesPlayerId playerId,
        BonesStrategyArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EnsurePlayerOwnership(playerId, artifact.PlayerId);

        var fileName = BuildScopedFileName(playerId, StrategyCategory, artifact.StrategyId.Value);
        var payload = BonesStrategyArtifactPersistence.FromArtifact(artifact);

        var descriptor = await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-strategy-{artifact.StrategyId.Value}"),
                kind: ArtifactKind.Json,
                fileName: fileName,
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        if (artifact.PromotionStatus == BonesPromotionStatus.Active)
            await SaveActiveStrategyPointerAsync(playerId, artifact.StrategyId, cancellationToken);

        return descriptor;
    }

    public async ValueTask<BonesStrategyArtifact> LoadStrategyArtifact(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);
        var artifactId = new ArtifactId($"bones-strategy-{strategyId.Value}");
        var strategyDescriptor = descriptors
            .FirstOrDefault(descriptor =>
                descriptor.ArtifactId == artifactId
                && IsPlayerScopedArtifact(descriptor, playerId, StrategyCategory))
            ?? throw new InvalidOperationException(
                $"No strategy artifact exists for strategy '{strategyId.Value}' and player seat {playerId.Seat}.");

        var content = await ReadArtifactContentAsync(strategyDescriptor, cancellationToken);
        return ParseStrategyArtifactContent(playerId, strategyId, strategyDescriptor, content);
    }

    private static BonesStrategyArtifact ParseStrategyArtifactContent(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        ArtifactDescriptor strategyDescriptor,
        string content)
    {
        if (strategyDescriptor.Kind == ArtifactKind.Json
            || content.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            var payload = JsonSerializer.Deserialize<BonesStrategyArtifactPersistence>(content)
                ?? throw new InvalidOperationException(
                    $"Strategy artifact '{strategyId.Value}' contains invalid JSON.");

            return payload.ToArtifact(playerId);
        }

        return new BonesStrategyArtifact(
            strategyId,
            playerId,
            BonesStrategyKind.Markdown,
            content,
            BonesPromotionStatus.Active);
    }

    public async ValueTask<ArtifactDescriptor> SaveEffectivenessRecord(
        BonesPlayerId playerId,
        BonesStrategyEffectivenessRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsurePlayerOwnership(playerId, record.PlayerId);

        var fileName = BuildScopedFileName(playerId, EffectivenessCategory, record.StrategyId.Value);
        var payload = BonesStrategyEffectivenessPersistence.FromRecord(record);

        return await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-effectiveness-{playerId.Seat}-{record.StrategyId.Value}"),
                kind: ArtifactKind.Json,
                fileName: fileName,
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async ValueTask<BonesStrategyEffectivenessRecord> LoadEffectivenessRecord(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);
        var artifactId = new ArtifactId($"bones-effectiveness-{playerId.Seat}-{strategyId.Value}");
        var effectivenessDescriptor = descriptors
            .FirstOrDefault(descriptor =>
                descriptor.ArtifactId == artifactId
                && IsPlayerScopedArtifact(descriptor, playerId, EffectivenessCategory))
            ?? throw new InvalidOperationException(
                $"No effectiveness record exists for strategy '{strategyId.Value}' and player seat {playerId.Seat}.");

        var content = await ReadArtifactContentAsync(effectivenessDescriptor, cancellationToken);
        var payload = JsonSerializer.Deserialize<BonesStrategyEffectivenessPersistence>(content)
            ?? throw new InvalidOperationException(
                $"Effectiveness record '{strategyId.Value}' contains invalid JSON.");

        return payload.ToRecord(playerId);
    }

    public async ValueTask<ArtifactDescriptor> SaveObservation(
        BonesPlayerId playerId,
        BonesObservationTranscript transcript,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        EnsurePlayerOwnership(playerId, transcript.ObserverId);

        var fileName = BuildScopedFileName(playerId, ObservationCategory, transcript.GameId.Value);

        return await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-observation-{playerId.Seat}-{transcript.GameId.Value}"),
                kind: ArtifactKind.Markdown,
                fileName: fileName,
                content: transcript.Markdown,
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async ValueTask<ArtifactDescriptor> SaveMatchHistory(
        BonesPlayerId playerId,
        BonesMatchHistoryTranscript transcript,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        EnsurePlayerOwnership(playerId, transcript.PlayerId);

        var fileName = BuildScopedFileName(playerId, MatchCategory, transcript.GameId.Value);

        return await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-match-{playerId.Seat}-{transcript.GameId.Value}"),
                kind: ArtifactKind.Markdown,
                fileName: fileName,
                content: transcript.Markdown,
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ArtifactDescriptor>> ListHistory(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);

        return descriptors
            .Where(descriptor =>
                IsPlayerScopedArtifact(descriptor, playerId, ObservationCategory)
                || IsPlayerScopedArtifact(descriptor, playerId, MatchCategory))
            .OrderBy(descriptor => descriptor.ProducedAtUtc)
            .ThenBy(descriptor => descriptor.ArtifactId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<ArtifactDescriptor>> ListStrategyArtifacts(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);

        return descriptors
            .Where(descriptor => IsPlayerScopedArtifact(descriptor, playerId, StrategyCategory))
            .OrderBy(descriptor => descriptor.ProducedAtUtc)
            .ThenBy(descriptor => descriptor.ArtifactId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<BonesMatchHistoryTranscript>> LoadMatchHistoryTranscripts(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);
        var matchDescriptors = descriptors
            .Where(descriptor => IsPlayerScopedArtifact(descriptor, playerId, MatchCategory))
            .OrderBy(descriptor => descriptor.ProducedAtUtc)
            .ThenBy(descriptor => descriptor.ArtifactId.Value, StringComparer.Ordinal)
            .ToArray();

        var transcripts = new List<BonesMatchHistoryTranscript>(matchDescriptors.Length);

        foreach (var descriptor in matchDescriptors)
        {
            EnsurePlayerScopedMatchDescriptor(descriptor, playerId);

            var markdown = await ReadArtifactContentAsync(descriptor, cancellationToken);
            var gameId = ExtractMatchGameId(descriptor, playerId);
            transcripts.Add(new BonesMatchHistoryTranscript(gameId, playerId, markdown));
        }

        return transcripts;
    }

    public async ValueTask<IReadOnlyList<BonesObservationTranscript>> LoadObservationTranscripts(
        BonesPlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        EnsureReadAccess(playerId);

        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);
        var observationDescriptors = descriptors
            .Where(descriptor => IsPlayerScopedArtifact(descriptor, playerId, ObservationCategory))
            .OrderBy(descriptor => descriptor.ProducedAtUtc)
            .ThenBy(descriptor => descriptor.ArtifactId.Value, StringComparer.Ordinal)
            .ToArray();

        var transcripts = new List<BonesObservationTranscript>(observationDescriptors.Length);

        foreach (var descriptor in observationDescriptors)
        {
            EnsurePlayerScopedObservationDescriptor(descriptor, playerId);

            var markdown = await ReadArtifactContentAsync(descriptor, cancellationToken);
            var gameId = ExtractObservationGameId(descriptor, playerId);
            transcripts.Add(new BonesObservationTranscript(gameId, playerId, markdown));
        }

        return transcripts;
    }

    private void EnsureSessionScope(SessionId sessionId)
    {
        if (sessionId != _sessionId)
        {
            throw new InvalidOperationException(
                $"Session '{sessionId.Value}' does not match knowledge store session '{_sessionId.Value}'.");
        }
    }

    private void EnsureReadAccess(BonesPlayerId targetPlayerId)
    {
        if (_accessScope is { } accessScope && accessScope != targetPlayerId)
        {
            throw new InvalidOperationException(
                $"Isolation gate: player seat {accessScope.Seat} cannot read private artifacts for seat {targetPlayerId.Seat}.");
        }
    }

    private static void EnsurePlayerOwnership(BonesPlayerId requestedPlayerId, BonesPlayerId owningPlayerId)
    {
        if (requestedPlayerId != owningPlayerId)
        {
            throw new InvalidOperationException(
                $"Player seat {requestedPlayerId.Seat} cannot write artifacts owned by seat {owningPlayerId.Seat}.");
        }
    }

    private async ValueTask<string> ReadArtifactContentAsync(
        ArtifactDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var artifactPath = Path.Combine(
            _repositoryPath,
            descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(artifactPath))
        {
            throw new InvalidOperationException(
                $"Artifact content is missing at '{descriptor.RelativePath}'.");
        }

        return await File.ReadAllTextAsync(artifactPath, cancellationToken);
    }

    private static void EnsurePlayerScopedObservationDescriptor(
        ArtifactDescriptor descriptor,
        BonesPlayerId playerId)
    {
        if (!IsPlayerScopedArtifact(descriptor, playerId, ObservationCategory))
        {
            throw new InvalidOperationException(
                $"Observation artifact path '{descriptor.RelativePath}' is not scoped to player seat {playerId.Seat}.");
        }
    }

    private static void EnsurePlayerScopedMatchDescriptor(
        ArtifactDescriptor descriptor,
        BonesPlayerId playerId)
    {
        if (!IsPlayerScopedArtifact(descriptor, playerId, MatchCategory))
        {
            throw new InvalidOperationException(
                $"Match history artifact path '{descriptor.RelativePath}' is not scoped to player seat {playerId.Seat}.");
        }
    }

    private static BonesGameId ExtractObservationGameId(ArtifactDescriptor descriptor, BonesPlayerId playerId)
    {
        EnsurePlayerScopedObservationDescriptor(descriptor, playerId);

        const string artifactPrefix = "bones-observation-";
        if (!descriptor.ArtifactId.Value.StartsWith(artifactPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Observation artifact id '{descriptor.ArtifactId.Value}' is not a bones observation artifact.");
        }

        var suffix = descriptor.ArtifactId.Value[artifactPrefix.Length..];
        var seatPrefix = $"{playerId.Seat}-";
        if (!suffix.StartsWith(seatPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Observation artifact id '{descriptor.ArtifactId.Value}' is not owned by player seat {playerId.Seat}.");
        }

        return new BonesGameId(suffix[seatPrefix.Length..]);
    }

    private static BonesGameId ExtractMatchGameId(ArtifactDescriptor descriptor, BonesPlayerId playerId)
    {
        EnsurePlayerScopedMatchDescriptor(descriptor, playerId);

        const string artifactPrefix = "bones-match-";
        if (!descriptor.ArtifactId.Value.StartsWith(artifactPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Match history artifact id '{descriptor.ArtifactId.Value}' is not a bones match artifact.");
        }

        var suffix = descriptor.ArtifactId.Value[artifactPrefix.Length..];
        var seatPrefix = $"{playerId.Seat}-";
        if (!suffix.StartsWith(seatPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Match history artifact id '{descriptor.ArtifactId.Value}' is not owned by player seat {playerId.Seat}.");
        }

        return new BonesGameId(suffix[seatPrefix.Length..]);
    }

    private static BonesStrategyId ExtractStrategyId(ArtifactDescriptor descriptor, BonesPlayerId playerId)
    {
        if (!IsPlayerScopedArtifact(descriptor, playerId, StrategyCategory))
        {
            throw new InvalidOperationException(
                $"Strategy artifact path '{descriptor.RelativePath}' is not scoped to player seat {playerId.Seat}.");
        }

        const string artifactPrefix = "bones-strategy-";
        if (!descriptor.ArtifactId.Value.StartsWith(artifactPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Strategy artifact id '{descriptor.ArtifactId.Value}' is not a bones strategy artifact.");
        }

        return new BonesStrategyId(descriptor.ArtifactId.Value[artifactPrefix.Length..]);
    }

    private static string BuildScopedFileName(BonesPlayerId playerId, string category, string suffix)
        => $"{PlayerPathSegmentPrefix}{playerId.Seat}-{category}-{suffix}";

    private static bool IsPlayerScopedArtifact(
        ArtifactDescriptor descriptor,
        BonesPlayerId playerId,
        string category)
    {
        var scopedPrefix = BuildScopedFileName(playerId, category, string.Empty);
        var fileName = Path.GetFileName(descriptor.RelativePath.Replace('\\', '/'));
        return fileName.StartsWith(scopedPrefix, StringComparison.Ordinal);
    }

    private static string BuildActiveStrategyArtifactId(BonesPlayerId playerId)
        => $"{ActiveStrategyArtifactPrefix}{playerId.Seat}";

    private async ValueTask<BonesStrategyId> LoadActiveStrategyIdAsync(
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        var strategyId = await TryLoadActiveStrategyIdAsync(playerId, cancellationToken);
        if (strategyId is not { } activeStrategyId)
        {
            throw new InvalidOperationException(
                $"No active strategy pointer exists for player seat {playerId.Seat} in session '{_sessionId.Value}'.");
        }

        return activeStrategyId;
    }

    private async ValueTask<BonesStrategyId?> TryLoadActiveStrategyIdAsync(
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        var descriptors = await _artifactStore.ListAsync(_sessionId, cancellationToken);
        var artifactId = new ArtifactId(BuildActiveStrategyArtifactId(playerId));
        var pointerDescriptor = descriptors
            .FirstOrDefault(descriptor => descriptor.ArtifactId == artifactId);

        if (pointerDescriptor is null)
            return null;

        var content = await ReadArtifactContentAsync(pointerDescriptor, cancellationToken);
        var payload = JsonSerializer.Deserialize<BonesActiveStrategyPointerPersistence>(content)
            ?? throw new InvalidOperationException(
                $"Active strategy pointer for seat {playerId.Seat} contains invalid JSON.");

        return payload.ToStrategyId(playerId);
    }

    private async ValueTask SaveActiveStrategyPointerAsync(
        BonesPlayerId playerId,
        BonesStrategyId strategyId,
        CancellationToken cancellationToken)
    {
        var payload = BonesActiveStrategyPointerPersistence.FromPointer(strategyId, playerId);
        var artifactId = BuildActiveStrategyArtifactId(playerId);

        await _artifactStore.SaveAsync(
            _sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId(artifactId),
                kind: ArtifactKind.Json,
                fileName: artifactId,
                content: JsonSerializer.Serialize(payload),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
