using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Abstractions.Identifiers;

namespace Wip.Workspaces.Git;

/// <summary>Stable provisioning refusal categories. They are part of the durable failure record.</summary>
public static class WorkspaceFailureCategories
{
    public const string Corrupt = "worktree-corrupt";
    public const string Mismatch = "worktree-mismatch";
    public const string StateCorrupt = "worktree-state-corrupt";
    public const string LockTimeout = "worktree-lock-timeout";
    public const string BaselineRewritten = "worktree-baseline-rewritten";
    public const string HistoryRewritten = "worktree-history-rewritten";
    public const string Partial = "worktree-partial";
    public const string ProvisionFailed = "worktree-provision-failed";
}

/// <summary>
/// A named provisioning refusal. The category is stable; the message carries the concrete evidence and, when a
/// partial directory was preserved, its path so the caller can keep the work rather than delete it.
/// </summary>
public sealed class WorkspaceProvisioningException : InvalidOperationException
{
    public WorkspaceProvisioningException(string category, string message, string? preservationPath = null)
        : base(category + ": " + message)
    {
        Category = category;
        PreservationPath = preservationPath;
    }

    public string Category { get; }

    public string? PreservationPath { get; }
}

/// <summary>One provisioning attempt. Attempts are append-only so an original failure survives later retries.</summary>
public sealed record WorkspaceProvisioningAttempt(
    int Attempt,
    string Operation,
    string Outcome,
    string? Category,
    string? Detail,
    string? TargetCommit,
    string? WorktreeHead,
    DateTimeOffset AtUtc);

/// <summary>
/// The durable identity and baseline of one session worktree. It lives in the repository's Git directory, not in the
/// working tree, so uncommitted implementation files are never touched by provisioning bookkeeping.
/// </summary>
public sealed record WorkspaceSessionState(
    int Version,
    string SessionId,
    string RepositoryPath,
    string WorktreePath,
    string SessionBranch,
    string TargetBranch,
    string OriginalTargetCommit,
    string OriginalTargetSource,
    string LastKnownSessionHead,
    IReadOnlyList<WorkspaceProvisioningAttempt> Attempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentVersion = 1;
    public const string ResolvedAtCreate = "resolved-at-create";
    public const string InferredMergeBase = "inferred-merge-base";

    public WorkspaceProvisioningAttempt? OriginalFailure
        => Attempts.FirstOrDefault(static attempt => string.Equals(attempt.Outcome, "failed", StringComparison.Ordinal));

    public WorkspaceProvisioningAttempt? LatestFailure
        => Attempts.LastOrDefault(static attempt => string.Equals(attempt.Outcome, "failed", StringComparison.Ordinal));
}

/// <summary>Reads and writes the durable state atomically. The caller holds the provisioning lease.</summary>
internal static class WorkspaceStateStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string StatePath(string gitCommonDirectory, SessionId sessionId)
        => Path.Combine(gitCommonDirectory, "wip", "sessions", Hash(sessionId.Value) + ".json");

    public static string LockPath(string gitCommonDirectory, SessionId sessionId)
        => Path.Combine(gitCommonDirectory, "wip", "locks", Hash(sessionId.Value) + ".lock");

    public static WorkspaceSessionState? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<WorkspaceSessionState>(File.ReadAllText(path), Json);
            if (state is null || state.Version != WorkspaceSessionState.CurrentVersion)
            {
                throw new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.StateCorrupt,
                    $"state at '{path}' has an unsupported or missing schema version");
            }

            if (string.IsNullOrWhiteSpace(state.SessionId)
                || string.IsNullOrWhiteSpace(state.RepositoryPath)
                || string.IsNullOrWhiteSpace(state.WorktreePath)
                || string.IsNullOrWhiteSpace(state.SessionBranch)
                || string.IsNullOrWhiteSpace(state.OriginalTargetCommit))
            {
                throw new WorkspaceProvisioningException(
                    WorkspaceFailureCategories.StateCorrupt,
                    $"state at '{path}' is missing required identity fields");
            }

            return state;
        }
        catch (WorkspaceProvisioningException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.StateCorrupt,
                $"state at '{path}' cannot be read: {exception.Message}");
        }
    }

    public static void Write(string path, WorkspaceSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>
/// Serializes provisioning for one session across tasks and processes. The in-process semaphore prevents a
/// same-process pair from racing into the file lock; the exclusively opened file lock owns the cross-process claim.
/// File handles are released by the OS on process termination, so a dead attempt cannot strand the session.
/// </summary>
internal sealed class WorkspaceProvisioningLease : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate;
    private readonly FileStream _stream;
    private bool _disposed;

    private WorkspaceProvisioningLease(SemaphoreSlim gate, FileStream stream)
    {
        _gate = gate;
        _stream = stream;
    }

    public static async ValueTask<WorkspaceProvisioningLease> AcquireAsync(
        string lockPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(lockPath);
        var gate = Gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));

        if (!await gate.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.LockTimeout,
                $"timed out after {timeout.TotalSeconds:0.#}s waiting for the in-process lease '{key}'");
        }

        try
        {
            var directory = Path.GetDirectoryName(key);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stopwatch = Stopwatch.StartNew();
            Exception? last = null;

            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    var stream = new FileStream(
                        key,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);
                    return new WorkspaceProvisioningLease(gate, stream);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    last = exception;
                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new WorkspaceProvisioningException(
                WorkspaceFailureCategories.LockTimeout,
                $"timed out after {timeout.TotalSeconds:0.#}s waiting for the cross-process lease '{key}'"
                + (last is null ? string.Empty : ": " + last.Message));
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _stream.Dispose();
        _gate.Release();
        return ValueTask.CompletedTask;
    }
}
