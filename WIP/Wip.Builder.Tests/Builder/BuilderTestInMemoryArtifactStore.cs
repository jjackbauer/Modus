using System.Collections.Concurrent;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;

namespace Wip.Builder.Tests.Builder;

internal sealed class BuilderTestInMemoryArtifactStore : IArtifactStore
{
    private readonly ConcurrentDictionary<SessionId, List<ArtifactDescriptor>> _artifacts = new();
    private readonly ConcurrentDictionary<ArtifactId, string> _contents = new();

    public ValueTask<ArtifactDescriptor> SaveAsync(SessionId sessionId, ArtifactContent artifact, CancellationToken cancellationToken)
    {
        _contents[artifact.ArtifactId] = artifact.Content;

        var descriptor = new ArtifactDescriptor(
            artifact.ArtifactId,
            sessionId,
            artifact.Kind,
            $"artifacts/{sessionId.Value}/{artifact.FileName}-{artifact.ArtifactId.Value}",
            artifact.ProducerType,
            artifact.ProducerVersion,
            artifact.ProducedAtUtc);

        var artifacts = _artifacts.GetOrAdd(sessionId, static _ => []);
        lock (artifacts)
        {
            artifacts.Add(descriptor);
        }

        return ValueTask.FromResult(descriptor);
    }

    public ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!_artifacts.TryGetValue(sessionId, out var artifacts))
            return ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>([]);

        lock (artifacts)
        {
            return ValueTask.FromResult<IReadOnlyList<ArtifactDescriptor>>(artifacts.ToArray());
        }
    }
}