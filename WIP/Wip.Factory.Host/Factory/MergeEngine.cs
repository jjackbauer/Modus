using System.Diagnostics;

namespace Wip.Factory.Host.Factory;

/// <summary>Result of an authorised merge attempt.</summary>
public sealed record MergeResult(bool Ok, string? MergedSha, string? Reason)
{
    public static MergeResult Ok_(string sha) => new(true, sha, null);

    public static MergeResult Refused(string reason) => new(false, null, reason);
}

/// <summary>
/// Performs the authorised merge and materialises the merged revision for verification (v0.3 §3.4 steps 6–7).
/// Injected so tests can exercise the lifecycle without a real repository.
/// </summary>
public interface IMergeEngine
{
    /// <summary>Merges the approved head into the target branch. Must be idempotent by reconciliation.</summary>
    ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default);

    /// <summary>A checkout of the merged revision, never the operator's working tree.</summary>
    ValueTask<string> MaterialiseAsync(string repositoryPath, string mergedSha, string destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the repository already contains the revision. Used to reconcile a crash after the merge was
    /// recorded as applied: the effect is not repeated, but it is also not assumed to exist (FAC-P0-01.4).
    /// </summary>
    ValueTask<bool> ContainsAsync(string repositoryPath, string mergedSha, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    /// <summary>The tree identity of a revision, when the engine can resolve one.</summary>
    ValueTask<string?> ResolveTreeAsync(string repositoryPath, string mergedSha, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(null);
}

/// <summary>The real merge, through git, with reconciliation against what the repository actually contains.</summary>
public sealed class GitMergeEngine : IMergeEngine
{
    public const string MergeConflict = "merge-conflict";

    public async ValueTask<MergeResult> MergeAsync(string repositoryPath, string headSha, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);

        // Reconcile first: if the approved change is already contained, do not merge twice.
        var contains = await TryGitAsync(repositoryPath, cancellationToken, "merge-base", "--is-ancestor", headSha, "HEAD").ConfigureAwait(false);
        if (contains is { } contained && contained.ExitCode == 0)
        {
            var existing = await GitAsync(repositoryPath, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false);
            return MergeResult.Ok_(existing.StandardOutput.Trim());
        }

        var merge = await TryGitAsync(
            repositoryPath, cancellationToken,
            "-c", "user.email=factory@example.test", "-c", "user.name=Factory",
            "merge", "--no-ff", "-m", "merge " + headSha, headSha).ConfigureAwait(false);

        if (merge is not { } merged)
        {
            return MergeResult.Refused(MergeConflict + ":git unavailable");
        }

        if (merged.ExitCode != 0)
        {
            return MergeResult.Refused(MergeConflict + ":" + merged.StandardError.Trim());
        }

        var sha = await GitAsync(repositoryPath, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false);
        return MergeResult.Ok_(sha.StandardOutput.Trim());
    }

    public async ValueTask<string> MaterialiseAsync(
        string repositoryPath,
        string mergedSha,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergedSha);

        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? destination);

        // Reconcile: a previous verification may have left this checkout behind.
        if (Directory.Exists(destination))
        {
            await TryGitAsync(repositoryPath, cancellationToken, "worktree", "remove", "--force", destination).ConfigureAwait(false);
            await TryGitAsync(repositoryPath, cancellationToken, "worktree", "prune").ConfigureAwait(false);

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
        }

        var add = await TryGitAsync(repositoryPath, cancellationToken, "worktree", "add", "--detach", destination, mergedSha)
            .ConfigureAwait(false);

        if (add is not { } added)
        {
            throw new InvalidOperationException("merged-revision-unavailable:git unavailable");
        }

        if (added.ExitCode != 0)
        {
            throw new InvalidOperationException("merged-revision-unavailable:" + added.StandardError.Trim());
        }

        return destination;
    }

    public async ValueTask<bool> ContainsAsync(
        string repositoryPath,
        string mergedSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergedSha);

        var contains = await TryGitAsync(
            repositoryPath, cancellationToken, "merge-base", "--is-ancestor", mergedSha, "HEAD").ConfigureAwait(false);

        return contains is { ExitCode: 0 };
    }

    public async ValueTask<string?> ResolveTreeAsync(
        string repositoryPath,
        string mergedSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergedSha);

        var tree = await TryGitAsync(repositoryPath, cancellationToken, "rev-parse", mergedSha + "^{tree}")
            .ConfigureAwait(false);

        return tree is { ExitCode: 0 } value ? value.StandardOutput.Trim() : null;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> GitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)?> TryGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            return await GitAsync(workingDirectory, cancellationToken, arguments).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
