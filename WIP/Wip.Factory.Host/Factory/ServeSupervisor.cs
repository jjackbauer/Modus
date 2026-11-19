using Wip.Factory.Core.Io;

namespace Wip.Factory.Host.Factory;

/// <summary>The result of one supervised serve window. It states what actually happened, never "always-on".</summary>
public sealed record ServeOutcome(
    bool Ok,
    string? Error,
    int ProcessId,
    string? Url,
    string Status,
    int RestartCount,
    string MergedSha)
{
    public static ServeOutcome Refused(string error, string mergedSha = "")
        => new(false, error, 0, null, "failed", 0, mergedSha);
}

/// <summary>One durable supervision observation (v0.4 FAC-P1-02.5).</summary>
public sealed record ServeStatusRecord(
    string WorkItemId,
    string MergedSha,
    string Status,
    int ProcessId,
    int RestartCount,
    string Detail,
    DateTimeOffset RecordedAtUtc);

/// <summary>Append-only liveness/observation evidence for one supervised release.</summary>
public sealed class ServeStatusJournal
{
    private readonly DurableJournal<ServeStatusRecord> _journal;
    private readonly string _lockPath;

    public ServeStatusJournal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _journal = new DurableJournal<ServeStatusRecord>(path);
        _lockPath = path + ".lock";
    }

    public string Path => _journal.Path;

    public void Append(string workItemId, string mergedSha, string status, int processId, int restartCount, string detail)
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(15))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _journal.Append(new ServeStatusRecord(
            workItemId, mergedSha, status, processId, restartCount, detail, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<ServeStatusRecord> ReadAll()
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(15))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return _journal.ReadAll();
    }

    public ServeStatusRecord? Latest()
    {
        var records = ReadAll();
        return records.Count == 0 ? null : records[^1];
    }
}
