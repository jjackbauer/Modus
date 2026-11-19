using Wip.Factory.Core.Io;
using Wip.Factory.Wip.Governance;

namespace Wip.Factory.Host.Factory;

/// <summary>One durable evidence row: which work item and which executed check.</summary>
public sealed record ReviewEvidenceRecord(string WorkItemId, ReviewCheckExecution Execution);

/// <summary>
/// Durable, append-only review evidence (v0.4 FAC-P0-02.1). Evidence is written before the review decision is
/// published, and a later reader can recompute the review digest from it. A corrupted interior row fails closed.
/// </summary>
public sealed class ReviewEvidenceStore
{
    private readonly DurableJournal<ReviewEvidenceRecord> _journal;
    private readonly string _lockPath;

    public ReviewEvidenceStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _journal = new DurableJournal<ReviewEvidenceRecord>(path);
        _lockPath = path + ".lock";
    }

    public string Path => _journal.Path;

    /// <summary>Appends the evidence rows, flushing each before returning.</summary>
    public void Append(string workItemId, IEnumerable<ReviewCheckExecution> executions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        foreach (var execution in executions)
        {
            _journal.Append(new ReviewEvidenceRecord(workItemId, execution));
        }
    }

    /// <summary>The latest execution per check for one work item, read after the lock is held.</summary>
    public IReadOnlyList<ReviewCheckExecution> ReadFor(string workItemId)
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        var latest = new Dictionary<ReviewCheckKind, ReviewCheckExecution>();
        foreach (var record in _journal.ReadAll())
        {
            if (string.Equals(record.WorkItemId, workItemId, StringComparison.Ordinal))
            {
                latest[record.Execution.Kind] = record.Execution;
            }
        }

        return latest.Values.OrderBy(static execution => execution.Kind).ToArray();
    }

    /// <summary>A digest over the four evidence artifacts, independent of their human-readable detail.</summary>
    public static string AggregateDigest(IEnumerable<ReviewCheckExecution> executions)
    {
        var canonical = string.Join(
            '|',
            executions
                .OrderBy(static execution => execution.Kind)
                .Select(static execution => execution.Kind + ":" + execution.ArtifactDigest));

        return ReviewEvidence.Digest("review-evidence|" + canonical);
    }
}
