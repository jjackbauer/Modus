using Wip.Abstractions.Privacy;

namespace Wip.Runtime.Runtime;

/// <summary>
/// WP-5.2 retention: inspects and purges files under <c>&lt;repository&gt;/.wip/sessions/*/artifacts</c>.
/// Purge only deletes regular files older than the policy boundary and never follows links outside
/// the sessions root.
/// </summary>
public sealed class SessionArtifactRetention
{
    private const string SessionsDirectoryName = "sessions";
    private const string ArtifactsDirectoryName = "artifacts";

    public ValueTask<RetentionStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var files = EnumerateArtifactFiles(repositoryPath, cancellationToken);
        var sessionDirectories = files
            .Select(static file => file.SessionDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return ValueTask.FromResult(new RetentionStatus(
            SessionDirectories: sessionDirectories,
            ArtifactFiles: files.Count,
            TotalBytes: files.Sum(static file => file.Length),
            OldestArtifactUtc: files.Count == 0 ? null : files.Min(static file => file.LastWriteTimeUtc)));
    }

    public ValueTask<RetentionPurgeResult> PurgeAsync(
        string repositoryPath,
        RetentionPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        var now = DateTimeOffset.UtcNow;
        if (!policy.Enabled)
        {
            var all = EnumerateArtifactFiles(repositoryPath, cancellationToken);
            return ValueTask.FromResult(new RetentionPurgeResult(0, 0, all.Count, now));
        }

        var cutoff = now - policy.MaxArtifactAge!.Value;
        var deleted = 0;
        long deletedBytes = 0;
        var retained = 0;

        foreach (var file in EnumerateArtifactFiles(repositoryPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc >= cutoff)
            {
                retained++;
                continue;
            }

            try
            {
                File.Delete(file.Path);
                deleted++;
                deletedBytes += file.Length;
            }
            catch (IOException)
            {
                // A locked file is retained and reported; the next purge retries it.
                retained++;
            }
            catch (UnauthorizedAccessException)
            {
                retained++;
            }
        }

        return ValueTask.FromResult(new RetentionPurgeResult(deleted, deletedBytes, retained, now));
    }

    private static List<ArtifactFile> EnumerateArtifactFiles(string repositoryPath, CancellationToken cancellationToken)
    {
        var files = new List<ArtifactFile>();
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return files;
        }

        var sessionsRoot = Path.GetFullPath(Path.Combine(repositoryPath, ".wip", SessionsDirectoryName));
        if (!Directory.Exists(sessionsRoot))
        {
            return files;
        }

        foreach (var sessionDirectory in Directory.EnumerateDirectories(sessionsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifactsDirectory = Path.Combine(sessionDirectory, ArtifactsDirectoryName);
            if (!Directory.Exists(artifactsDirectory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(artifactsDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }

                // Confinement: never report or delete a resolved path outside the artifacts root.
                var resolved = Path.GetFullPath(path);
                if (!resolved.StartsWith(Path.GetFullPath(artifactsDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !string.Equals(resolved, Path.GetFullPath(artifactsDirectory), StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(new ArtifactFile(resolved, sessionDirectory, info.Length, info.LastWriteTimeUtc));
            }
        }

        return files;
    }

    private sealed record ArtifactFile(string Path, string SessionDirectory, long Length, DateTimeOffset LastWriteTimeUtc);
}
