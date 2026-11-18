using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Persistence;

namespace Wip.Factory.Host.Factory;

/// <summary>What the CLI reported for a submission.</summary>
public sealed record SubmitResult(
    bool Accepted,
    string? WorkItemId,
    string? State,
    string? RequirementDigest,
    IReadOnlyList<string> Errors)
{
    public static SubmitResult Refused(params string[] errors) => new(false, null, null, null, errors);
}

/// <summary>Current durable view of one work item.</summary>
public sealed record WorkItemStatus(
    string WorkItemId,
    string State,
    int Version,
    string RequirementDigest,
    string? ProposalDigest,
    string? FrozenContractDigest,
    int EventCount);

/// <summary>
/// The operator-facing factory runtime: one composition root over the durable store, the dispatch worker and
/// the stage handler. The CLI is a thin adapter over this type, so tests and operators exercise the same path.
/// </summary>
public sealed class FactoryRuntime
{
    private readonly FactoryControlRoot _root;
    private readonly LocalFactoryStore _store;
    private readonly FactoryDispatchWorker _worker;

    public FactoryRuntime(
        FactoryControlRoot root,
        IImplementationStageRunner? implementations = null,
        IStageDispatchHandler? handler = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _root.EnsureCreated();
        _store = new LocalFactoryStore(_root.Root);
        _worker = new FactoryDispatchWorker(
            _store,
            _store,
            _store,
            handler ?? new FactoryStageDispatchHandler(implementations),
            new FactoryDispatchOptions(new SubscriberId(FactoryStageDispatchHandler.Subscriber), MaxEventsPerRun: 32));
    }

    public FactoryControlRoot Root => _root;

    public LocalFactoryStore Store => _store;

    /// <summary>Submits a requirement revision: durable Proposed item plus the submitted event, atomically.</summary>
    public async ValueTask<SubmitResult> SubmitAsync(
        string requirementText,
        string? titleOverride = null,
        CancellationToken cancellationToken = default)
    {
        var (document, errors) = RequirementDocument.Parse(requirementText, titleOverride);
        if (document is null)
        {
            return SubmitResult.Refused([.. errors]);
        }

        // Idempotent by requirement digest: an identical revision is not duplicated (v0.3 §3.2.1).
        var existing = await FindByRequirementDigestAsync(document.Digest, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new SubmitResult(true, existing.Value.Id, existing.Value.State, document.Digest, []);
        }

        var workItemId = new WorkItemId("WORK-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(document.Digest)))[..8].ToLowerInvariant());

        var snapshot = new WorkItemSnapshot(
            workItemId,
            FactoryWorkState.Proposed,
            WorkItemSnapshot.MinimumVersion,
            _root.TargetRepository,
            [],
            DateTimeOffset.UtcNow);

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["requirement"] = requirementText,
            ["requirementDigest"] = document.Digest,
            ["title"] = document.Title,
            ["supportedClass"] = document.SupportedClassId,
            ["expectations"] = JsonSerializer.Serialize(document.Expectations)
        };

        var commit = await _store.CommitAsync(
            new WorkItemCommit(snapshot, ExpectedVersion: 0, [FactoryStageDispatchHandler.Event(
                FactoryEventTypes.RequirementSubmitted, workItemId, data)]),
            cancellationToken).ConfigureAwait(false);

        if (!commit.Succeeded)
        {
            return SubmitResult.Refused(commit.Reason ?? "submit-commit-refused");
        }

        return new SubmitResult(true, workItemId.Value, snapshot.State.ToString(), document.Digest, []);
    }

    /// <summary>Drains eligible events until idle. Returns per-pass reports.</summary>
    public async ValueTask<IReadOnlyList<FactoryDispatchRunReport>> RunAsync(
        int maxPasses = 8,
        CancellationToken cancellationToken = default)
    {
        var reports = new List<FactoryDispatchRunReport>();

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var report = await _worker.RunOnceAsync(cancellationToken).ConfigureAwait(false);
            reports.Add(report);

            if (report.Claimed == 0)
            {
                break;
            }
        }

        return reports;
    }

    /// <summary>Reads durable state from a fresh process view — never from in-memory demo surfaces.</summary>
    public async ValueTask<IReadOnlyList<WorkItemStatus>> StatusAsync(CancellationToken cancellationToken = default)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var ids = events
            .Select(static e => e.Subject)
            .Where(static s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var statuses = new List<WorkItemStatus>(ids.Length);

        foreach (var id in ids)
        {
            var snapshot = await _store.LoadAsync(new WorkItemId(id), cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                continue;
            }

            var mine = events.Where(e => string.Equals(e.Subject, id, StringComparison.Ordinal)).ToArray();
            statuses.Add(new WorkItemStatus(
                id,
                snapshot.State.ToString(),
                snapshot.Version,
                LastValue(mine, FactoryEventTypes.RequirementSubmitted, "requirementDigest"),
                LastValue(mine, FactoryEventTypes.ContractProposed, "proposalDigest"),
                LastValue(mine, FactoryEventTypes.ContractApproved, "contractDigest"),
                mine.Length));
        }

        return statuses;
    }

    /// <summary>The exact proposal awaiting approval, if any.</summary>
    public async ValueTask<(ContractProposal? Proposal, string? Error)> ContractShowAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var proposed = events.LastOrDefault(e =>
            string.Equals(e.Subject, workItemId, StringComparison.Ordinal)
            && string.Equals(e.Type, FactoryEventTypes.ContractProposed, StringComparison.Ordinal));

        if (proposed is null || !proposed.Data.TryGetValue("proposal", out var json))
        {
            return (null, "no-contract-proposal");
        }

        var proposal = ContractProposal.FromJson(json);
        return proposal is null ? (null, "contract-proposal-unreadable") : (proposal, null);
    }

    /// <summary>Freezes the proposal whose exact digest the operator authorised.</summary>
    public async ValueTask<(bool Ok, string? Error)> ContractApproveAsync(
        string workItemId,
        string expectedDigest,
        CancellationToken cancellationToken = default)
    {
        var (proposal, error) = await ContractShowAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return (false, error);
        }

        if (!string.Equals(proposal.ProposalDigest, expectedDigest, StringComparison.Ordinal))
        {
            return (false, "contract-digest-mismatch");
        }

        var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return (false, "work-item-not-found");
        }

        var submitted = await SubmittedPayloadAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, string>(submitted, StringComparer.Ordinal)
        {
            ["contractDigest"] = proposal.ProposalDigest,
            ["requirementDigest"] = proposal.RequirementDigest,
            ["approvedBy"] = Environment.UserName,
            ["approvedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["approvalKind"] = "operator"
        };

        var frozen = snapshot with { Version = snapshot.Version + 1 };
        var commit = await _store.CommitAsync(
            new WorkItemCommit(frozen, snapshot.Version, [FactoryStageDispatchHandler.Event(
                FactoryEventTypes.ContractApproved, snapshot.WorkItemId, data)]),
            cancellationToken).ConfigureAwait(false);

        return commit.Succeeded ? (true, null) : (false, commit.Reason ?? "approve-commit-refused");
    }

    /// <summary>The submitted event payload for a work item, so later stages keep the requirement text.</summary>
    private async ValueTask<IReadOnlyDictionary<string, string>> SubmittedPayloadAsync(string workItemId, CancellationToken cancellationToken)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var submitted = events.LastOrDefault(e =>
            string.Equals(e.Subject, workItemId, StringComparison.Ordinal)
            && string.Equals(e.Type, FactoryEventTypes.RequirementSubmitted, StringComparison.Ordinal));

        return submitted?.Data ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async ValueTask<(string Id, string State)?> FindByRequirementDigestAsync(
        string digest,
        CancellationToken cancellationToken)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var match = events.LastOrDefault(e =>
            string.Equals(e.Type, FactoryEventTypes.RequirementSubmitted, StringComparison.Ordinal)
            && e.Data.TryGetValue("requirementDigest", out var value)
            && string.Equals(value, digest, StringComparison.Ordinal));

        if (match is null)
        {
            return null;
        }

        var snapshot = await _store.LoadAsync(new WorkItemId(match.Subject), cancellationToken).ConfigureAwait(false);
        return snapshot is null ? null : (match.Subject, snapshot.State.ToString());
    }

    private static string? LastValue(IReadOnlyList<FactoryEventEnvelope> events, string type, string key)
    {
        for (var i = events.Count - 1; i >= 0; i--)
        {
            if (string.Equals(events[i].Type, type, StringComparison.Ordinal)
                && events[i].Data.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
