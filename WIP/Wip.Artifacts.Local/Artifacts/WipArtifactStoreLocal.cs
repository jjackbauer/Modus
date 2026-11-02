using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;

namespace Wip.Artifacts.Local;

public sealed class WipArtifactStoreLocal : IArtifactStore
{
    private const string DescriptorSuffix = ".descriptor.json";
    private static readonly JsonSerializerOptions DescriptorJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _repositoryPath;
    private readonly ILogger<WipArtifactStoreLocal>? _logger;

    // Retry policy for transient enumeration errors (3 attempts with exponential backoff: 50ms, 200ms, 800ms).
    private const int MaxListRetries = 3;
    private static readonly int[] ListRetryDelaysMs = [50, 200, 800];

    public WipArtifactStoreLocal(string repositoryPath, ILogger<WipArtifactStoreLocal>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(repositoryPath));

        _repositoryPath = repositoryPath;
        _logger = logger;
    }

    public async ValueTask<ArtifactDescriptor> SaveAsync(
        SessionId sessionId,
        ArtifactContent artifact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var artifactsDirectory = BuildArtifactsDirectory(_repositoryPath, sessionId);
        Directory.CreateDirectory(artifactsDirectory);

        var artifactPath = BuildArtifactPath(artifactsDirectory, artifact.FileName, artifact.ArtifactId, artifact.Kind);

        await WriteFileAtomicallyAsync(artifactPath, artifact.Content, cancellationToken);

        var descriptor = new ArtifactDescriptor(
            artifactId: artifact.ArtifactId,
            sessionId: sessionId,
            kind: artifact.Kind,
            relativePath: Path.GetRelativePath(_repositoryPath, artifactPath).Replace('\\', '/'),
            producerType: artifact.ProducerType,
            producerVersion: artifact.ProducerVersion,
            producedAtUtc: artifact.ProducedAtUtc);

        var persistedDescriptor = new PersistedArtifactDescriptor(
            artifactId: descriptor.ArtifactId.Value,
            sessionId: descriptor.SessionId.Value,
            kind: descriptor.Kind,
            fileName: artifact.FileName,
            relativePath: descriptor.RelativePath,
            producerType: descriptor.ProducerType,
            producerCapabilityId: descriptor.ProducerType,
            producerVersion: descriptor.ProducerVersion,
            producedAtUtc: descriptor.ProducedAtUtc);

        await WriteDescriptorAtomicallyAsync(GetDescriptorPath(artifactPath), persistedDescriptor, cancellationToken);

        return descriptor;
    }

    private static async Task WriteFileAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            await MoveIntoPlaceAsync(tempPath, path, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static async Task WriteDescriptorAtomicallyAsync(
        string path,
        PersistedArtifactDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, descriptor, DescriptorJsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await MoveIntoPlaceAsync(tempPath, path, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static async Task MoveIntoPlaceAsync(string tempPath, string path, CancellationToken cancellationToken)
    {
        // Windows reports a replace collision with a reader as UnauthorizedAccessException
        // (the destination is open), not only IOException. Retry both transient shapes.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(25 * attempt, cancellationToken);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask<IReadOnlyList<ArtifactDescriptor>> ListAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var artifactsDirectory = BuildArtifactsDirectory(_repositoryPath, sessionId);
        if (!Directory.Exists(artifactsDirectory))
            return [];

        List<Exception>? capturedExceptions = null;
        for (var attempt = 0; attempt < MaxListRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                var delayMs = attempt <= ListRetryDelaysMs.Length
                    ? ListRetryDelaysMs[attempt - 1]
                    : ListRetryDelaysMs[^1];

                _logger?.LogWarning(
                    "Transient error listing artifacts for session {SessionId} (attempt {Attempt}/{MaxRetries}); retrying in {DelayMs}ms.",
                    sessionId.Value, attempt + 1, MaxListRetries, delayMs);

                await Task.Delay(delayMs, cancellationToken);
            }

            try
            {
                var descriptorPaths = Directory
                    .EnumerateFiles(artifactsDirectory, $"*{DescriptorSuffix}", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();

                var descriptors = new List<ArtifactDescriptor>(descriptorPaths.Length);
                foreach (var descriptorPath in descriptorPaths)
                {
                    // Allow concurrent writers to replace descriptor files while a
                    // status/list read is in progress. File.OpenRead's default
                    // FileShare.Read blocks Move/Delete and causes spurious
                    // UnauthorizedAccessException during promotion.
                    await using var stream = new FileStream(
                        descriptorPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 4096,
                        FileOptions.Asynchronous);
                    var persisted = await JsonSerializer.DeserializeAsync<PersistedArtifactDescriptor>(stream, DescriptorJsonOptions, cancellationToken)
                        ?? throw new InvalidOperationException($"Descriptor file '{descriptorPath}' is empty or invalid.");

                    descriptors.Add(new ArtifactDescriptor(
                        artifactId: new ArtifactId(persisted.ArtifactId),
                        sessionId: new SessionId(persisted.SessionId),
                        kind: persisted.Kind,
                        relativePath: persisted.RelativePath,
                        producerType: persisted.ProducerType,
                        producerVersion: persisted.ProducerVersion,
                        producedAtUtc: persisted.ProducedAtUtc));
                }

                return descriptors
                    .OrderBy(descriptor => descriptor.ProducedAtUtc)
                    .ThenBy(descriptor => descriptor.ArtifactId.Value, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (IOException ex) when (IsTransientEnumerationError(ex))
            {
                capturedExceptions ??= new List<Exception>();
                capturedExceptions.Add(ex);
                // Continue to next retry attempt.
            }
            catch (UnauthorizedAccessException ex)
            {
                capturedExceptions ??= new List<Exception>();
                capturedExceptions.Add(ex);
                // Continue to next retry attempt.
            }
        }

        // All retries exhausted — surface as degraded warning and throw aggregate.
        _logger?.LogWarning(
            "Degraded: artifact listing for session {SessionId} failed after {MaxRetries} retry attempts. Returning empty to allow loop continuation.",
            sessionId.Value, MaxListRetries);

        throw new AggregateException(
            $"Artifact listing for session '{sessionId.Value}' failed after {MaxListRetries} retry attempts.",
            capturedExceptions ?? new List<Exception>());
    }

    private static bool IsTransientEnumerationError(IOException ex)
    {
        // Directory enumeration can fail transiently when files are being
        // created/deleted concurrently (e.g., parallel session artifact writes).
        // HResult codes: ERROR_DIR_NOT_EMPTY (145), ERROR_SHARING_VIOLATION (-2147024864 / 0x80070020).
        const int errorSharingViolation = unchecked((int)0x80070020);
        const int errorDirNotEmpty = 145;
        return ex.HResult == errorSharingViolation
            || ex.HResult == errorDirNotEmpty
            || ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<ArtifactContent> LoadAsync(
        SessionId sessionId,
        ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        var artifactsDirectory = BuildArtifactsDirectory(_repositoryPath, sessionId);
        if (!Directory.Exists(artifactsDirectory))
        {
            throw new InvalidOperationException(
                $"Artifact '{artifactId.Value}' not found in session '{sessionId.Value}'.");
        }

        // List descriptors to find this artifact's relative path
        var descriptors = await ListAsync(sessionId, cancellationToken);
        var descriptor = descriptors.FirstOrDefault(d => d.ArtifactId == artifactId)
            ?? throw new InvalidOperationException(
                $"Artifact '{artifactId.Value}' not found in session '{sessionId.Value}'.");

        var absolutePath = Path.Combine(_repositoryPath, descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        string content;
        await using (var stream = new FileStream(
                         absolutePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete,
                         bufferSize: 4096,
                         FileOptions.Asynchronous))
        using (var reader = new StreamReader(stream))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        return new ArtifactContent(
            artifactId: descriptor.ArtifactId,
            kind: descriptor.Kind,
            fileName: Path.GetFileName(absolutePath),
            content: content,
            producerType: descriptor.ProducerType,
            producerVersion: descriptor.ProducerVersion,
            producedAtUtc: descriptor.ProducedAtUtc);
    }

    private static string BuildArtifactsDirectory(string repositoryPath, SessionId sessionId)
        => Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "artifacts");

    private static string GetDescriptorPath(string artifactPath)
        => $"{artifactPath}{DescriptorSuffix}";

    private static string BuildArtifactPath(string artifactsDirectory, string fileName, ArtifactId artifactId, ArtifactKind kind)
    {
        var normalizedFileName = NormalizeFileName(fileName, artifactId, kind);
        var artifactPath = Path.Combine(artifactsDirectory, normalizedFileName);

        var fullArtifactsDirectory = Path.GetFullPath(artifactsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullArtifactPath = Path.GetFullPath(artifactPath);

        if (!fullArtifactPath.StartsWith(fullArtifactsDirectory, StringComparison.Ordinal))
            throw new ArgumentException("Artifact file name must resolve inside the session artifacts directory.", nameof(fileName));

        return fullArtifactPath;
    }

    private static string NormalizeFileName(string fileName, ArtifactId artifactId, ArtifactKind kind)
    {
        if (Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Artifact file name must not include directory segments.", nameof(fileName));

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Artifact file name contains invalid characters.", nameof(fileName));

        var extension = kind switch
        {
            ArtifactKind.Json => ".json",
            ArtifactKind.Markdown => ".md",
            ArtifactKind.Patch => ".patch",
            _ => throw new InvalidOperationException($"Unsupported artifact kind '{kind}'.")
        };

        var normalizedFileName = Path.HasExtension(fileName) ? fileName : $"{fileName}{extension}";
        var baseName = Path.GetFileNameWithoutExtension(normalizedFileName);
        var normalizedExtension = Path.GetExtension(normalizedFileName);

        return $"{baseName}-{artifactId.Value}{normalizedExtension}";
    }

    private sealed class PersistedArtifactDescriptor
    {
        public PersistedArtifactDescriptor(
            string artifactId,
            string sessionId,
            ArtifactKind kind,
            string fileName,
            string relativePath,
            string producerType,
            string producerCapabilityId,
            string producerVersion,
            DateTimeOffset producedAtUtc)
        {
            ArtifactId = artifactId;
            SessionId = sessionId;
            Kind = kind;
            FileName = fileName;
            RelativePath = relativePath;
            ProducerType = producerType;
            ProducerCapabilityId = producerCapabilityId;
            ProducerVersion = producerVersion;
            ProducedAtUtc = producedAtUtc;
        }

        public PersistedArtifactDescriptor()
        {
        }

        public string ArtifactId { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public ArtifactKind Kind { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public string ProducerType { get; set; } = string.Empty;

        public string ProducerCapabilityId { get; set; } = string.Empty;

        public string ProducerVersion { get; set; } = string.Empty;

        public DateTimeOffset ProducedAtUtc { get; set; }
    }
}