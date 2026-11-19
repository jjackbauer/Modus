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

/// <summary>Lifecycle phases of one supervised serve window (v0.4 IMP-007).</summary>
public enum ServePhase
{
    Preparing = 1,
    Building = 2,
    Starting = 3,

    /// <summary>The process is identified and its socket is accepting connections; health is not yet proven.</summary>
    Listening = 4,

    /// <summary>Identity, listening socket and the frozen contract have all been proven.</summary>
    Ready = 5,
    Restarted = 6,
    Stopped = 7,
    Failed = 8
}

/// <summary>
/// One progress observation published while serving. <see cref="ServePhase.Listening"/> carries the URL/PID/served
/// SHA as soon as they exist; <see cref="ServePhase.Ready"/> is only published after the frozen contract passes.
/// </summary>
public sealed record ServeProgress(
    ServePhase Phase,
    string? Url,
    int ProcessId,
    string MergedSha,
    string Detail,
    DateTimeOffset AtUtc);

/// <summary>One durable supervision observation (v0.4 FAC-P1-02.5).</summary>
public sealed record ServeStatusRecord(
    string WorkItemId,
    string MergedSha,
    string Status,
    int ProcessId,
    int RestartCount,
    string Detail,
    DateTimeOffset RecordedAtUtc,
    string? ServedUrl = null);

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

    public void Append(string workItemId, string mergedSha, string status, int processId, int restartCount, string detail, string? servedUrl = null)
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(15))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _journal.Append(new ServeStatusRecord(
            workItemId, mergedSha, status, processId, restartCount, detail, DateTimeOffset.UtcNow, servedUrl));
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
