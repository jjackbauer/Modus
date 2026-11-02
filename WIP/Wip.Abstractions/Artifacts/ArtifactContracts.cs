using Wip.Abstractions.Identifiers;

namespace Wip.Abstractions.Artifacts;

public enum ArtifactKind
{
    Json = 1,
    Markdown = 2,
    Patch = 3
}

public sealed record ArtifactContent
{
    public ArtifactContent(
        ArtifactId artifactId,
        ArtifactKind kind,
        string fileName,
        string content,
        string producerType,
        string producerVersion,
        DateTimeOffset producedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(fileName));

        if (content is null)
            throw new ArgumentNullException(nameof(content));

        if (string.IsNullOrWhiteSpace(producerType))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(producerType));

        if (string.IsNullOrWhiteSpace(producerVersion))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(producerVersion));

        ArtifactId = artifactId;
        Kind = kind;
        FileName = fileName;
        Content = content;
        ProducerType = producerType;
        ProducerVersion = producerVersion;
        ProducedAtUtc = producedAtUtc;
    }

    public ArtifactId ArtifactId { get; }

    public ArtifactKind Kind { get; }

    public string FileName { get; }

    public string Content { get; }

    public string ProducerType { get; }

    public string ProducerVersion { get; }

    public DateTimeOffset ProducedAtUtc { get; }
}

public sealed record ArtifactDescriptor
{
    public ArtifactDescriptor(
        ArtifactId artifactId,
        SessionId sessionId,
        ArtifactKind kind,
        string relativePath,
        string producerType,
        string producerVersion,
        DateTimeOffset producedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(relativePath));

        if (string.IsNullOrWhiteSpace(producerType))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(producerType));

        if (string.IsNullOrWhiteSpace(producerVersion))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(producerVersion));

        ArtifactId = artifactId;
        SessionId = sessionId;
        Kind = kind;
        RelativePath = relativePath;
        ProducerType = producerType;
        ProducerVersion = producerVersion;
        ProducedAtUtc = producedAtUtc;
    }

    public ArtifactId ArtifactId { get; }

    public SessionId SessionId { get; }

    public ArtifactKind Kind { get; }

    public string RelativePath { get; }

    public string ProducerType { get; }

    public string ProducerVersion { get; }

    public DateTimeOffset ProducedAtUtc { get; }
}

public interface IArtifactStore
{
    ValueTask<ArtifactDescriptor> SaveAsync(
        SessionId sessionId,
        ArtifactContent artifact,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    ValueTask<ArtifactContent> LoadAsync(
        SessionId sessionId,
        ArtifactId artifactId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("LoadAsync is not supported by this artifact store.");
}
