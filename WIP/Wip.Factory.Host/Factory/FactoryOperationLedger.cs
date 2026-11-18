using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wip.Factory.Host.Factory;

/// <summary>State of one externally visible effect.</summary>
public enum OperationState
{
    Pending = 1,
    Applied = 2,
    Failed = 3
}

/// <summary>A durable record of an external effect (v0.3 §5.2).</summary>
public sealed record OperationRecord(
    string OperationId,
    string WorkItemId,
    string EventId,
    string Stage,
    string StageAttemptId,
    int EffectOrdinal,
    string EffectKind,
    string CanonicalTarget,
    string DesiredDigest,
    OperationState State,
    string? ExternalIdentity,
    string? VerificationDigest,
    string? Reason,
    DateTimeOffset RecordedAtUtc);

/// <summary>
/// The operation ledger (v0.3 §5.2). An operation id is derived from what the effect <i>is</i>, not from when it runs,
/// so a retry of the same effect reuses the same id and can be reconciled instead of repeated.
/// </summary>
public sealed class FactoryOperationLedger
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<OperationRecord> _records = [];
    private bool _loaded;

    public FactoryOperationLedger(string controlRoot, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        Directory.CreateDirectory(controlRoot);
        _path = Path.Combine(controlRoot, "operations.log");
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>Stable identity for one effect. Same inputs ⇒ same id; a different ordinal ⇒ a different id.</summary>
    public static string DeriveOperationId(
        string workItemId,
        string eventId,
        string stage,
        string stageAttemptId,
        int effectOrdinal,
        string effectKind,
        string canonicalTarget,
        string desiredDigest)
    {
        var canonical = string.Join(
            '\u001f',
            workItemId, eventId, stage, stageAttemptId,
            effectOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            effectKind, canonicalTarget, desiredDigest);

        return "op-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24].ToLowerInvariant();
    }

    /// <summary>The latest record for an id, or null when the effect has never been attempted.</summary>
    public OperationRecord? Latest(string operationId)
    {
        EnsureLoaded();
        for (var i = _records.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_records[i].OperationId, operationId, StringComparison.Ordinal))
            {
                return _records[i];
            }
        }

        return null;
    }

    public bool IsApplied(string operationId) => Latest(operationId)?.State == OperationState.Applied;

    /// <summary>Records the intent before the effect runs. Re-running with the same id is a no-op.</summary>
    public OperationRecord Begin(
        string operationId,
        string workItemId,
        string eventId,
        string stage,
        string stageAttemptId,
        int effectOrdinal,
        string effectKind,
        string canonicalTarget,
        string desiredDigest)
    {
        EnsureLoaded();

        var existing = Latest(operationId);
        if (existing is not null)
        {
            return existing;
        }

        var record = new OperationRecord(
            operationId, workItemId, eventId, stage, stageAttemptId, effectOrdinal,
            effectKind, canonicalTarget, desiredDigest, OperationState.Pending,
            null, null, null, _clock());

        Append(record);
        return record;
    }

    public void MarkApplied(string operationId, string externalIdentity, string verificationDigest)
    {
        EnsureLoaded();
        var existing = Latest(operationId)
            ?? throw new InvalidOperationException($"operation-unknown:{operationId}");

        Append(existing with
        {
            State = OperationState.Applied,
            ExternalIdentity = externalIdentity,
            VerificationDigest = verificationDigest,
            RecordedAtUtc = _clock()
        });
    }

    public void MarkFailed(string operationId, string reason)
    {
        EnsureLoaded();
        var existing = Latest(operationId)
            ?? throw new InvalidOperationException($"operation-unknown:{operationId}");

        Append(existing with
        {
            State = OperationState.Failed,
            Reason = reason,
            RecordedAtUtc = _clock()
        });
    }

    /// <summary>Every record in append order — the audit view.</summary>
    public IReadOnlyList<OperationRecord> All()
    {
        EnsureLoaded();
        return _records.ToArray();
    }

    private void Append(OperationRecord record)
    {
        _records.Add(record);
        File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_path))
        {
            return;
        }

        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<OperationRecord>(line);
                if (record is not null)
                {
                    _records.Add(record);
                }
            }
            catch (JsonException)
            {
                // A torn trailing line is ignored; interior corruption would surface as a missing record, not a crash.
            }
        }
    }
}
