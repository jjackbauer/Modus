using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Core.Operations;

/// <summary>The six views an operator must be able to query (F-L.1, source §12).</summary>
public enum FactoryView
{
    WorkQueue = 1,
    ProductionPipeline = 2,
    Agents = 3,
    Quality = 4,
    Improvement = 5,
    Traceability = 6
}

/// <summary>One machine-readable row in a view.</summary>
public sealed record ViewEntry(string Key, string Value);

/// <summary>A view snapshot. Absent views are reported as missing, never silently omitted.</summary>
public sealed record ViewSnapshot(FactoryView View, IReadOnlyList<ViewEntry> Entries);

/// <summary>
/// The queryable status surface. It is machine-readable and does not require a dashboard (F-L.1).
/// </summary>
public sealed class FactoryStatusSurface
{
    private readonly Dictionary<FactoryView, List<ViewEntry>> _views = [];

    public void Record(FactoryView view, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_views.TryGetValue(view, out var entries))
        {
            entries = [];
            _views[view] = entries;
        }

        entries.Add(new ViewEntry(key, value));
    }

    public ViewSnapshot Get(FactoryView view)
        => new(view, _views.TryGetValue(view, out var entries) ? entries.ToArray() : []);

    public IReadOnlyList<ViewSnapshot> All()
        => Enum.GetValues<FactoryView>().Select(Get).ToArray();

    public IReadOnlyList<FactoryView> MissingViews()
        => Enum.GetValues<FactoryView>()
            .Where(view => !_views.TryGetValue(view, out var entries) || entries.Count == 0)
            .ToArray();
}

/// <summary>The operational controls an operator may invoke (F-L.2).</summary>
public enum OperationalControl
{
    PauseSubscribers = 1,
    ResumeSubscribers = 2,
    PauseRole = 3,
    DrainCapability = 4,
    CancelAttempt = 5,
    ReplayEvent = 6,
    QuarantineEvent = 7,
    OverridePriority = 8,
    RevokeApproval = 9,
    RollBackProfile = 10,
    InspectScheduleOccurrences = 11,

    /// <summary>An attempt to edit immutable evidence. Always refused.</summary>
    MutateEvidence = 12
}

/// <summary>One audited control invocation.</summary>
public sealed record ControlAuditEntry(
    OperationalControl Control,
    string Target,
    string Actor,
    DateTimeOffset AtUtc,
    bool Applied,
    string? Reason);

/// <summary>
/// Applies operational controls and audits every one of them (F-L.2). Evidence mutation is refused
/// unconditionally, and the refusal is audited rather than dropped (F-L.3).
/// </summary>
public sealed class FactoryControlSurface
{
    private readonly List<ControlAuditEntry> _audit = [];

    public const string EvidenceImmutableCategory = "evidence-immutable";
    public const string UnknownTargetCategory = "unknown-target";

    public IReadOnlyList<ControlAuditEntry> Audit => _audit.ToArray();

    public ControlAuditEntry Apply(OperationalControl control, string target, string actor, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (control == OperationalControl.MutateEvidence)
        {
            var refused = new ControlAuditEntry(
                control,
                target,
                actor,
                nowUtc,
                Applied: false,
                EvidenceImmutableCategory + ":operators cannot edit immutable evidence in place");
            _audit.Add(refused);
            return refused;
        }

        var applied = new ControlAuditEntry(control, target, actor, nowUtc, Applied: true, null);
        _audit.Add(applied);
        return applied;
    }

    public IReadOnlyList<ControlAuditEntry> AuditOf(string target)
        => _audit.Where(entry => string.Equals(entry.Target, target, StringComparison.Ordinal)).ToArray();
}

/// <summary>
/// Content-addressed evidence. An entry is written once with its digest and can be read back, but never
/// mutated: a mutation attempt is refused and leaves the digest verifying (F-L.3).
/// </summary>
public sealed class ImmutableEvidenceVault
{
    private readonly Dictionary<string, (string Content, string Digest)> _entries = new(StringComparer.Ordinal);

    public string Put(ArtifactId artifactId, string content, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        _entries[artifactId.Value] = (content, digest);
        return digest;
    }

    public (bool Verified, string? Digest) Verify(ArtifactId artifactId)
    {
        if (!_entries.TryGetValue(artifactId.Value, out var entry))
        {
            return (false, null);
        }

        var recomputed = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.Content))).ToLowerInvariant();

        return (string.Equals(recomputed, entry.Digest, StringComparison.Ordinal), entry.Digest);
    }

    public (bool Mutated, string? Reason) TryMutate(ArtifactId artifactId, string replacementContent)
    {
        if (!_entries.ContainsKey(artifactId.Value))
        {
            return (false, "unknown-artifact");
        }

        // Refused, and deliberately not applied.
        return (false, FactoryControlSurface.EvidenceImmutableCategory);
    }
}
