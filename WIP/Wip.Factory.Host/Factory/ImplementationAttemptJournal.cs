using System.Security.Cryptography;
using System.Text;
using Wip.Factory.Core.Io;

namespace Wip.Factory.Host.Factory;

/// <summary>
/// One implementation attempt for a work item. Attempts are append-only: the first failure keeps its original
/// category and a later retry failure is recorded separately with a link back to it.
/// </summary>
public sealed record ImplementationAttemptRecord(
    string AttemptId,
    string WorkItemId,
    int AttemptNumber,
    string SessionId,
    string Outcome,
    string? FailureCategory,
    string? FailureRole,
    string? Detail,
    string? RepositoryPath,
    string? WorktreePath,
    string? TargetCommit,
    string? BaseSha,
    string? HeadSha,
    string? PreviousAttemptId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc)
{
    public const string SucceededOutcome = "succeeded";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";
    public const string OriginalFailureRole = "original";
    public const string RetryFailureRole = "retry";
}

/// <summary>
/// Durable per-work-item attempt history. It closes the PA-01 gap: a retry that fails during provisioning no longer
/// replaces the original agent failure, and both causes remain readable after a process restart.
/// </summary>
public sealed class ImplementationAttemptJournal
{
    public const string JournalVersion = "factory/implementation-attempts@1";

    private readonly DurableJournal<ImplementationAttemptRecord> _journal;
    private readonly string _lockPath;

    public ImplementationAttemptJournal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _journal = new DurableJournal<ImplementationAttemptRecord>(path);
        _lockPath = path + ".lock";
    }

    public string Path => _journal.Path;

    public static string PathFor(string controlRoot, string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        var safe = new string(workItemId
            .Select(static character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')
            .ToArray());
        if (safe.Length == 0)
        {
            safe = "work-item";
        }

        if (safe.Length > 80)
        {
            safe = safe[..80] + "-" + ShortHash(workItemId);
        }

        return System.IO.Path.Combine(controlRoot, "attempts", safe + ".ndjson");
    }

    /// <summary>Appends one attempt, assigning its ordinal and linking it to the previous attempt atomically.</summary>
    public async ValueTask<ImplementationAttemptRecord> AppendAsync(
        ImplementationAttemptRecord candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        using var lease = await CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        var history = _journal.ReadAll();
        var attemptNumber = history.Count + 1;
        var previous = history.LastOrDefault();
        var failureRole = string.Equals(candidate.Outcome, ImplementationAttemptRecord.FailedOutcome, StringComparison.Ordinal)
            ? history.Any(static attempt => string.Equals(attempt.Outcome, ImplementationAttemptRecord.FailedOutcome, StringComparison.Ordinal))
                ? ImplementationAttemptRecord.RetryFailureRole
                : ImplementationAttemptRecord.OriginalFailureRole
            : null;

        var record = candidate with
        {
            AttemptNumber = attemptNumber,
            FailureRole = failureRole,
            PreviousAttemptId = previous?.AttemptId
        };

        _journal.Append(record);
        return record;
    }

    /// <summary>Reads every committed attempt. Interior corruption fails closed; a torn trailing write is discarded.</summary>
    public IReadOnlyList<ImplementationAttemptRecord> ReadAll()
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return _journal.ReadAll();
    }

    private static string ShortHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}
