using System.Security.Cryptography;
using System.Text;
using Wip.Factory.Core.Io;

namespace Wip.Factory.Host.Factory;

/// <summary>State of one externally visible effect. A corrective record is a new fact, never an overwrite.</summary>
public enum OperationState
{
    Pending = 1,
    Applied = 2,
    Failed = 3,
    Corrected = 4
}

/// <summary>
/// Verification of an applied effect, kept separate from the effect's own state: an applied merge proves only that
/// Git changed, never that the merged revision passed acceptance (v0.4 FAC-P0-01 / FAC-P1-04).
/// </summary>
public enum VerificationState
{
    Unverified = 1,
    Verified = 2,
    Failed = 3
}

/// <summary>A durable record of an external effect (v0.3 §5.2, v0.4 FAC-P1-04).</summary>
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
    DateTimeOffset RecordedAtUtc,
    VerificationState VerificationState = VerificationState.Unverified,
    string? VerificationDetail = null,
    int Attempt = 1,
    string? CorrectsReason = null);

/// <summary>
/// The operation ledger (v0.3 §5.2, v0.4 FAC-P1-04). An operation id is derived from what the effect <i>is</i>, not
/// from when it runs, so a retry of the same effect reuses the same id and can be reconciled instead of repeated.
/// </summary>
/// <remarks>
/// Records are append-only, digest-framed, and written through to disk. A torn trailing record is discarded; a
/// malformed or digest-mismatched interior record fails closed with <see cref="JournalCorruptionException"/>. Every
/// mutation holds the ledger's own cross-process lock, so two hosts cannot interleave a read-modify-append.
/// </remarks>
public sealed class FactoryOperationLedger
{
    private readonly string _lockPath;
    private readonly DurableJournal<OperationRecord> _journal;

    public FactoryOperationLedger(string controlRoot, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        Directory.CreateDirectory(controlRoot);
        _journal = new DurableJournal<OperationRecord>(Path.Combine(controlRoot, "operations.log"));
        _lockPath = Path.Combine(controlRoot, "locks", "operations.lock");
        Clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public Func<DateTimeOffset> Clock { get; }

    /// <summary>Trailing torn records discarded during the most recent read. Interior damage throws instead.</summary>
    public int TornRecordCount => _journal.TornRecordCount;

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
        => WithRecords(static (records, id) => Last(records, id), operationId);

    public bool IsApplied(string operationId) => Latest(operationId)?.State == OperationState.Applied;

    /// <summary>True only when an applied effect also has a verified higher-level outcome.</summary>
    public bool IsVerified(string operationId)
    {
        var latest = Latest(operationId);
        return latest is { State: OperationState.Applied, VerificationState: VerificationState.Verified };
    }

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
        return WithRecords((records, _) =>
        {
            var existing = Last(records, operationId);
            if (existing is not null)
            {
                return existing;
            }

            var record = new OperationRecord(
                operationId, workItemId, eventId, stage, stageAttemptId, effectOrdinal,
                effectKind, canonicalTarget, desiredDigest, OperationState.Pending,
                null, null, null, Clock());

            _journal.Append(record);
            return record;
        }, string.Empty);
    }

    /// <summary>Marks the effect applied. Repeating it is idempotent: the effect is recorded once.</summary>
    public void MarkApplied(string operationId, string externalIdentity, string verificationDigest)
    {
        WithRecords((records, _) =>
        {
            var existing = Require(records, operationId);

            if (existing.State == OperationState.Applied)
            {
                return existing;
            }

            _journal.Append(existing with
            {
                State = OperationState.Applied,
                ExternalIdentity = externalIdentity,
                VerificationDigest = verificationDigest,
                RecordedAtUtc = Clock()
            });

            return existing;
        }, string.Empty);
    }

    /// <summary>
    /// Marks the effect failed. An applied effect cannot be turned into a failure by a later record: that would erase
    /// an external side effect from the audit. The caller must issue an explicit corrective record instead.
    /// </summary>
    public void MarkFailed(string operationId, string reason, int attempt = 1)
    {
        WithRecords((records, _) =>
        {
            var existing = Require(records, operationId);
            if (existing.State == OperationState.Applied)
            {
                throw new InvalidOperationException(
                    "operation-already-applied:" + operationId + ": use MarkCorrected with the reconciliation evidence");
            }

            _journal.Append(existing with
            {
                State = OperationState.Failed,
                Reason = reason,
                Attempt = Math.Max(attempt, existing.Attempt),
                RecordedAtUtc = Clock()
            });

            return existing;
        }, string.Empty);
    }

    /// <summary>Records an explicit reconciliation fact after an applied effect turned out to be wrong or stale.</summary>
    public void MarkCorrected(string operationId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        WithRecords((records, _) =>
        {
            var existing = Require(records, operationId);
            _journal.Append(existing with
            {
                State = OperationState.Corrected,
                CorrectsReason = reason,
                Reason = reason,
                RecordedAtUtc = Clock()
            });

            return existing;
        }, string.Empty);
    }

    /// <summary>Records that the applied effect's higher-level outcome was independently verified.</summary>
    public void MarkVerified(string operationId, string verificationDigest, string detail)
    {
        WithRecords((records, _) =>
        {
            var existing = Require(records, operationId);
            if (existing.State != OperationState.Applied)
            {
                throw new InvalidOperationException($"operation-not-applied:{operationId}");
            }

            _journal.Append(existing with
            {
                VerificationState = VerificationState.Verified,
                VerificationDigest = verificationDigest,
                VerificationDetail = detail,
                RecordedAtUtc = Clock()
            });

            return existing;
        }, string.Empty);
    }

    /// <summary>Records that verification of an applied effect failed. The effect stays applied; it is not a release.</summary>
    public void MarkVerificationFailed(string operationId, string detail)
    {
        WithRecords((records, _) =>
        {
            var existing = Require(records, operationId);
            _journal.Append(existing with
            {
                VerificationState = VerificationState.Failed,
                VerificationDetail = detail,
                RecordedAtUtc = Clock()
            });

            return existing;
        }, string.Empty);
    }

    /// <summary>Every record in append order — the audit view.</summary>
    public IReadOnlyList<OperationRecord> All()
        => WithRecords(static (records, _) => (IReadOnlyList<OperationRecord>)records.ToArray(), string.Empty);

    private T WithRecords<T>(Func<IReadOnlyList<OperationRecord>, string, T> action, string argument)
    {
        using var lease = CrossProcessFileLock
            .AcquireAsync(_lockPath, TimeSpan.FromSeconds(30))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        var records = _journal.ReadAll();
        return action(records, argument);
    }

    private static OperationRecord? Last(IReadOnlyList<OperationRecord> records, string operationId)
    {
        for (var index = records.Count - 1; index >= 0; index--)
        {
            if (string.Equals(records[index].OperationId, operationId, StringComparison.Ordinal))
            {
                return records[index];
            }
        }

        return null;
    }

    private static OperationRecord Require(IReadOnlyList<OperationRecord> records, string operationId)
        => Last(records, operationId) ?? throw new InvalidOperationException($"operation-unknown:{operationId}");
}
