using System.Text.Json;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public static class BonesBootstrapSource
{
    public const string Library = "Library";
}

public sealed record BonesStrategyBootstrapResult(
    IReadOnlyList<BonesPlayerId> BootstrappedFromLibrarySeats,
    IReadOnlyList<BonesPlayerId> DefaultSeededSeats);

internal sealed record BonesStrategyBootstrapStatePersistence(
    int[] BootstrappedFromLibrarySeats,
    int[] DefaultSeededSeats)
{
    public static BonesStrategyBootstrapStatePersistence FromResult(BonesStrategyBootstrapResult result)
        => new(
            result.BootstrappedFromLibrarySeats.Select(playerId => playerId.Seat).ToArray(),
            result.DefaultSeededSeats.Select(playerId => playerId.Seat).ToArray());

    public BonesStrategyBootstrapResult ToResult()
        => new(
            BootstrappedFromLibrarySeats.Select(seat => new BonesPlayerId(seat)).ToArray(),
            DefaultSeededSeats.Select(seat => new BonesPlayerId(seat)).ToArray());
}

public static class BonesStrategyBootstrapState
{
    private const string ProducerType = "Wip.Bones.Agents.BonesStrategyBootstrapper";
    private const string ProducerVersion = "1.0.0";
    private const string ArtifactFileName = "bones-strategy-bootstrap";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async ValueTask SaveAsync(
        IArtifactStore artifactStore,
        SessionId sessionId,
        BonesStrategyBootstrapResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactStore);
        ArgumentNullException.ThrowIfNull(result);

        var payload = BonesStrategyBootstrapStatePersistence.FromResult(result);
        await artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-strategy-bootstrap-{sessionId.Value}"),
                kind: ArtifactKind.Json,
                fileName: ArtifactFileName,
                content: JsonSerializer.Serialize(payload, JsonOptions),
                producerType: ProducerType,
                producerVersion: ProducerVersion,
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public static async ValueTask<BonesStrategyBootstrapResult?> TryLoadAsync(
        IArtifactStore artifactStore,
        SessionId sessionId,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var descriptors = await artifactStore.ListAsync(sessionId, cancellationToken);
        var bootstrapDescriptor = descriptors
            .FirstOrDefault(descriptor =>
                string.Equals(descriptor.ProducerType, ProducerType, StringComparison.Ordinal));

        if (bootstrapDescriptor is null)
            return null;

        var artifactPath = Path.Combine(
            repositoryPath,
            bootstrapDescriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(artifactPath))
            return null;

        var content = await File.ReadAllTextAsync(artifactPath, cancellationToken);
        var persistence = JsonSerializer.Deserialize<BonesStrategyBootstrapStatePersistence>(content, JsonOptions);
        return persistence?.ToResult();
    }
}

public sealed class BonesStrategyBootstrapper
{
    private readonly BonesStrategyLibrary _strategyLibrary;

    public BonesStrategyBootstrapper(BonesStrategyLibrary strategyLibrary)
    {
        _strategyLibrary = strategyLibrary ?? throw new ArgumentNullException(nameof(strategyLibrary));
    }

    public async ValueTask<BonesStrategyBootstrapResult> BootstrapAsync(
        IArtifactStore artifactStore,
        SessionId sessionId,
        string repositoryPath,
        bool resumeFromBest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);
        var bootstrappedFromLibrary = new List<BonesPlayerId>();
        var defaultSeeded = new List<BonesPlayerId>();

        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            if (resumeFromBest)
            {
                var libraryBest = _strategyLibrary.LoadBestStrategy(playerId);
                if (libraryBest is not null)
                {
                    await knowledgeStore.SaveStrategyArtifact(playerId, libraryBest, cancellationToken);
                    bootstrappedFromLibrary.Add(playerId);
                    continue;
                }
            }

            await SeedDefaultOpponentStrategyAsync(knowledgeStore, playerId, cancellationToken);
            defaultSeeded.Add(playerId);
        }

        var result = new BonesStrategyBootstrapResult(bootstrappedFromLibrary, defaultSeeded);
        await BonesStrategyBootstrapState.SaveAsync(artifactStore, sessionId, result, cancellationToken);
        return result;
    }

    private static async ValueTask SeedDefaultOpponentStrategyAsync(
        BonesPlayerKnowledgeStore knowledgeStore,
        BonesPlayerId playerId,
        CancellationToken cancellationToken)
    {
        await knowledgeStore.SaveStrategy(
            playerId,
            new BonesStrategyDocument(
                new BonesStrategyId($"initial-seat-{playerId.Seat}-v1"),
                playerId,
                markdown: """
                    # Bones Strategy

                    ## Rules
                    - Prefer first legal move from GetLegalMoves.
                    """),
            cancellationToken);
    }
}
