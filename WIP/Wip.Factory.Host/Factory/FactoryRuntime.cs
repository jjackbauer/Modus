using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Persistence;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Persistence;
using Wip.Factory.Wip.Governance;

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
    int EventCount,
    string? LastFailure = null);

/// <summary>
/// The operator-facing factory runtime: one composition root over the durable store, the dispatch worker and
/// the stage handler. The CLI is a thin adapter over this type, so tests and operators exercise the same path.
/// </summary>
public sealed class FactoryRuntime
{
    private readonly FactoryControlRoot _root;
    private readonly LocalFactoryStore _store;
    private readonly FactoryDispatchWorker _worker;
    private readonly ICandidateHost? _candidates;
    private readonly IMergeEngine? _merges;
    private readonly ICandidateBuildVerifier? _builds;
    private readonly FactoryTemplateRegistry _templates;
    private readonly Func<CancellationToken, Task<string?>>? _targetHeadProbe;

    public FactoryRuntime(
        FactoryControlRoot root,
        IImplementationStageRunner? implementations = null,
        ICandidateHost? candidates = null,
        ICandidateBuildVerifier? builds = null,
        IMergeEngine? merges = null,
        IStageDispatchHandler? handler = null,
        Func<CancellationToken, Task<string?>>? targetHeadProbe = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _candidates = candidates;
        _merges = merges;
        _builds = builds;
        _targetHeadProbe = targetHeadProbe;
        _templates = new FactoryTemplateRegistry(root.Root);
        _root.EnsureCreated();
        _store = new LocalFactoryStore(_root.Root);
        _worker = new FactoryDispatchWorker(
            _store,
            _store,
            _store,
            handler ?? new FactoryStageDispatchHandler(
                implementations, candidates, builds,
                verificationRoot: Path.Combine(root.Root, "verify"),
                merges: merges,
                targetHead: merges is null ? null : async ct => await TargetHeadAsync(root.TargetRepository, ct).ConfigureAwait(false),
                currentEvidence: async (workItemId, ct) => await CurrentEvidenceAsync(workItemId, ct).ConfigureAwait(false),
                operations: new FactoryOperationLedger(root.Root)),
            new FactoryDispatchOptions(new SubscriberId(FactoryStageDispatchHandler.Subscriber), MaxEventsPerRun: 32));
    }

    public FactoryControlRoot Root => _root;

    /// <summary>Bounded restarts for one supervised serve window (v0.4 FAC-P1-02.2).</summary>
    public const int MaxServeRestarts = 2;

    public LocalFactoryStore Store => _store;

    /// <summary>Submits a requirement revision: durable Proposed item plus the submitted event, atomically.</summary>
    public async ValueTask<SubmitResult> SubmitAsync(
        string requirementText,
        string? titleOverride = null,
        string? acceptanceContractJson = null,
        CancellationToken cancellationToken = default)
    {
        var (document, errors) = RequirementDocument.Parse(requirementText, titleOverride);
        if (document is null)
        {
            return SubmitResult.Refused([.. errors]);
        }

        // A requirement may name an approved template; intake instantiates it so the oracle is the template exactly.
        if (document.TemplateReference is { Length: > 0 } templateRef)
        {
            var parts = templateRef.Split('@');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var templateVersion))
            {
                return SubmitResult.Refused("template-reference-invalid:" + templateRef);
            }

            var (manifest, templateError) = _templates.Resolve(parts[0], templateVersion);
            if (manifest is null)
            {
                return SubmitResult.Refused(templateError ?? "template-unavailable");
            }

            var parameters = string.IsNullOrWhiteSpace(document.TemplateParametersJson)
                ? new JsonObject()
                : JsonNode.Parse(document.TemplateParametersJson) as JsonObject ?? new JsonObject();

            if (acceptanceContractJson is null)
            {
                // The template carries scenarios; the contract also needs its schema and class.
                acceptanceContractJson = new JsonObject
                {
                    ["schemaVersion"] = manifest.SchemaVersion,
                    ["supportedClass"] = manifest.SupportedClass,
                    ["scenarios"] = FactoryTemplatePolicy.Instantiate(manifest, parameters).DeepClone()
                }.ToJsonString();
            }
        }

        AcceptanceContract? contract = null;
        if (!string.IsNullOrWhiteSpace(acceptanceContractJson))
        {
            var (parsed, contractErrors) = AcceptanceContract.Parse(acceptanceContractJson);
            if (parsed is null)
            {
                return SubmitResult.Refused([.. contractErrors]);
            }

            contract = parsed;
        }

        // Idempotent by requirement digest: an identical revision is not duplicated (v0.3 §3.2.1).
        // The shared mutation lease spans the duplicate check and the commit, so two processes cannot both
        // observe "no existing item" and create it twice (v0.4 FAC-P1-03).
        FactoryMutationLock mutation;
        try
        {
            mutation = await FactoryMutationLock.AcquireAsync(_root.Root, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MutationLockUnavailableException exception)
        {
            return SubmitResult.Refused(exception.Category);
        }

        try
        {
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

        if (document.TemplateReference is { Length: > 0 } namedTemplate)
        {
            // Provenance the policy needs at approval time: which template, instantiated with what.
            data["templateReference"] = namedTemplate;
            data["templateParameters"] = document.TemplateParametersJson ?? "{}";
        }

        if (contract is not null)
        {
            // Store the operator bytes; the digest pins the canonical form derived from them, so
            // whitespace and key order cannot change what was approved.
            data["acceptanceContract"] = acceptanceContractJson!;
            data["acceptanceContractDigest"] = contract.Digest;
            data["contractDigest"] = contract.Digest;
        }

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
        finally
        {
            mutation.Dispose();
        }
    }

    /// <summary>
    /// One exclusive writer per control root (v0.3 §5.3). The lock is an OS file handle held for the whole run, so a
    /// second process gets <c>host-lock-held</c> instead of silently interleaving writes with the first.
    /// </summary>
    public async ValueTask<(IReadOnlyList<FactoryDispatchRunReport> Reports, string? Error)> RunExclusiveAsync(
        int maxPasses = 8,
        CancellationToken cancellationToken = default)
    {
        var lockPath = Path.Combine(_root.LocksPath, "run.lock");
        FileStream handle;

        try
        {
            handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return ([], "host-lock-held");
        }
        catch (UnauthorizedAccessException)
        {
            return ([], "host-lock-denied");
        }

        try
        {
            await handle.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), cancellationToken).ConfigureAwait(false);
            await handle.FlushAsync(cancellationToken).ConfigureAwait(false);

            var reports = await RunAsync(maxPasses, cancellationToken).ConfigureAwait(false);
            return (reports, null);
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
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
                LastValue(mine, FactoryEventTypes.RequirementSubmitted, "requirementDigest") ?? string.Empty,
                LastValue(mine, FactoryEventTypes.ContractProposed, "proposalDigest"),
                LastValue(mine, FactoryEventTypes.ContractApproved, "contractDigest"),
                mine.Length,
                LastFailure(mine)));
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

        var history = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        if (RejectedAfterProposal(history, workItemId, expectedDigest))
        {
            return (false, "contract-rejected");
        }

        if (!proposal.Executable && proposal.HasUnresolvedQuestions)
        {
            // Ambiguous/unobservable prose cannot be frozen as if it were a testable oracle.
            return (false, "contract-ambiguous:" + string.Join(";", proposal.UnresolvedQuestions!));
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

        // A concurrent dispatch may advance the aggregate between our read and our commit. CAS refuses that
        // commit; re-read and retry so a legitimate approval is not silently lost (FAC-P1-03).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return (false, "work-item-not-found");
            }

            var frozen = snapshot with { Version = snapshot.Version + 1 };
            var commit = await _store.CommitAsync(
                new WorkItemCommit(frozen, snapshot.Version, [FactoryStageDispatchHandler.Event(
                    FactoryEventTypes.ContractApproved, snapshot.WorkItemId, data)]),
                cancellationToken).ConfigureAwait(false);

            if (commit.Succeeded)
            {
                return (true, null);
            }

            if (commit.Status != CommitStatus.VersionConflict)
            {
                return (false, commit.Reason ?? "approve-commit-refused");
            }
        }

        return (false, "approve-version-conflict");
    }

    /// <summary>
    /// Records an explicit human rejection of the current contract proposal without changing the work state and
    /// without any implementation side effect. A rejected digest cannot then be approved; a revised requirement
    /// creates a new proposal to consider (v0.3 #78).
    /// </summary>
    public async ValueTask<(bool Ok, string? Error)> ContractRejectAsync(
        string workItemId,
        string proposalDigest,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var (proposal, error) = await ContractShowAsync(workItemId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return (false, error);
        }

        if (!string.Equals(proposal.ProposalDigest, proposalDigest, StringComparison.Ordinal))
        {
            return (false, "contract-digest-mismatch");
        }

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FactoryDispatchOptions.WorkItemDataKey] = workItemId,
            ["proposalDigest"] = proposalDigest,
            ["requirementDigest"] = proposal.RequirementDigest,
            ["reason"] = string.IsNullOrWhiteSpace(reason) ? "operator-rejected" : reason,
            ["rejectedBy"] = Environment.UserName,
            ["rejectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return (false, "work-item-not-found");
            }

            if (snapshot.State != FactoryWorkState.Proposed)
            {
                return (false, "not-awaiting-contract-approval");
            }

            var rejected = snapshot with { Version = snapshot.Version + 1 };
            var commit = await _store.CommitAsync(
                new WorkItemCommit(rejected, snapshot.Version, [FactoryStageDispatchHandler.Event(
                    OperatorEvents.ContractRejected, snapshot.WorkItemId, data)]),
                cancellationToken).ConfigureAwait(false);

            if (commit.Succeeded)
            {
                return (true, null);
            }

            if (commit.Status != CommitStatus.VersionConflict)
            {
                return (false, commit.Reason ?? "reject-commit-refused");
            }
        }

        return (false, "reject-version-conflict");
    }

    private static bool RejectedAfterProposal(
        IReadOnlyList<FactoryEventEnvelope> events,
        string workItemId,
        string proposalDigest)
    {
        var lastProposal = -1;
        var lastRejection = -1;

        for (var index = 0; index < events.Count; index++)
        {
            var envelope = events[index];
            if (!string.Equals(envelope.Subject, workItemId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(envelope.Type, FactoryEventTypes.ContractProposed, StringComparison.Ordinal))
            {
                lastProposal = index;
            }
            else if (string.Equals(envelope.Type, OperatorEvents.ContractRejected, StringComparison.Ordinal)
                     && envelope.Data.TryGetValue("proposalDigest", out var rejectedDigest)
                     && string.Equals(rejectedDigest, proposalDigest, StringComparison.Ordinal))
            {
                lastRejection = index;
            }
        }

        return lastRejection >= 0 && lastRejection > lastProposal;
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

    /// <summary>
    /// Attempts D-09 policy approval: a contract is auto-approved only when it is exactly the instantiation of an
    /// operator-approved template with permitted parameters (v0.3 §4.3).
    /// </summary>
    public async ValueTask<(bool Ok, string? Error, TemplateDecision? Decision)> ContractPolicyApproveAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        var events = await EventsAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var submitted = events.LastOrDefault(static e => string.Equals(e.Type, FactoryEventTypes.RequirementSubmitted, StringComparison.Ordinal));
        if (submitted is null)
        {
            return (false, "no-submitted-requirement", null);
        }

        if (!submitted.Data.TryGetValue("templateReference", out var templateRef)
            || !submitted.Data.TryGetValue("acceptanceContract", out var contractJson))
        {
            return (false, "template-policy:proposal-names-no-template", null);
        }

        var parts = templateRef.Split('@');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var version))
        {
            return (false, "template-reference-invalid:" + templateRef, null);
        }

        var parameters = submitted.Data.TryGetValue("templateParameters", out var parametersJson)
            ? JsonNode.Parse(parametersJson) as JsonObject ?? new JsonObject()
            : new JsonObject();

        var proposal = new JsonObject
        {
            ["templateId"] = parts[0],
            ["templateVersion"] = version,
            ["parameters"] = parameters.DeepClone(),
            // The stored contract is the whole object; the policy compares only its scenarios.
            ["scenarios"] = JsonNode.Parse(contractJson)?["scenarios"]?.DeepClone()
        };

        var decision = FactoryTemplatePolicy.Evaluate(_templates, proposal);
        if (!decision.Approved)
        {
            return (false, decision.Reason, decision);
        }

        var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return (false, "work-item-not-found", decision);
        }

        var data = new Dictionary<string, string>(submitted.Data, StringComparer.Ordinal)
        {
            ["contractDigest"] = decision.ContractDigest!,
            ["approvalKind"] = FactoryTemplatePolicy.ApprovalKind,
            ["templateId"] = decision.TemplateId!,
            ["templateVersion"] = decision.TemplateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["manifestDigest"] = decision.ManifestDigest!,
            ["parametersDigest"] = decision.ParametersDigest!,
            ["approvedBy"] = "policy:" + FactoryPolicy.PolicyVersion,
            ["approvedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var approved = snapshot with { Version = snapshot.Version + 1 };
        var commit = await _store.CommitAsync(
            new WorkItemCommit(approved, snapshot.Version, [FactoryStageDispatchHandler.Event(
                FactoryEventTypes.ContractApproved, snapshot.WorkItemId, data)]),
            cancellationToken).ConfigureAwait(false);

        return commit.Succeeded ? (true, null, decision) : (false, commit.Reason ?? "approve-commit-refused", decision);
    }

    /// <summary>
    /// Records the operator's merge authorisation, bound to the exact review evidence. D-09 authorises contract
    /// approval only; this is a separate, human-authorised merge (v0.3 §2, §3.4 step 4; v0.4 FAC-P1-05).
    /// The approval binds head/base, target branch, requirement revision, frozen contract, all four check evidence
    /// digests and the policy/profile/runner versions, and it refuses missing, stale or already-applied evidence.
    /// </summary>
    public async ValueTask<(bool Ok, string? Error)> ApproveMergeAsync(
        string workItemId,
        string reviewDigest,
        CancellationToken cancellationToken = default)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var mine = events
            .Where(e => string.Equals(e.Subject, workItemId, StringComparison.Ordinal))
            .ToArray();

        var review = mine.LastOrDefault(static e => string.Equals(e.Type, OperatorEvents.ReviewAccepted, StringComparison.Ordinal));
        if (review is null)
        {
            return (false, "no-review-to-approve");
        }

        if (!review.Data.TryGetValue("reviewDigest", out var recorded)
            || !string.Equals(recorded, reviewDigest, StringComparison.Ordinal))
        {
            return (false, "review-digest-mismatch");
        }

        // One approval authorizes one merge. The approval id is durable in the event log, so a restarted process
        // cannot apply the same approval a second time.
        if (mine.Any(e => string.Equals(e.Type, OperatorEvents.MergeApproved, StringComparison.Ordinal)
                          && e.Data.TryGetValue("approvalDigest", out var applied)
                          && string.Equals(applied, reviewDigest, StringComparison.Ordinal)))
        {
            return (false, "approval-already-applied");
        }

        var evidence = new ReviewEvidenceStore(ReviewEvidencePath).ReadFor(workItemId);
        if (evidence.Count != 4)
        {
            return (false, "approval-missing-check-evidence");
        }

        var aggregate = ReviewEvidenceStore.AggregateDigest(evidence);
        if (!review.Data.TryGetValue("evidenceDigest", out var recordedEvidence)
            || !string.Equals(recordedEvidence, aggregate, StringComparison.Ordinal))
        {
            return (false, "approval-evidence-mismatch");
        }

        if (!review.Data.TryGetValue("headSha", out var headSha) || string.IsNullOrWhiteSpace(headSha))
        {
            return (false, "approval-incomplete");
        }

        var baseSha = review.Data.TryGetValue("baseSha", out var boundBase) ? boundBase : string.Empty;

        if (evidence.Any(execution => !execution.IsComplete
                                      || !execution.Passed
                                      || !string.Equals(execution.ReviewedSha, headSha, StringComparison.Ordinal)))
        {
            return (false, "approval-evidence-stale");
        }

        if (!review.Data.TryGetValue("contractDigest", out var reviewContract) || string.IsNullOrWhiteSpace(reviewContract))
        {
            return (false, "approval-incomplete");
        }

        var submitted = mine.LastOrDefault(static e => string.Equals(e.Type, FactoryEventTypes.RequirementSubmitted, StringComparison.Ordinal));
        if (submitted is null)
        {
            return (false, "no-submitted-requirement");
        }

        if (!submitted.Data.TryGetValue("acceptanceContract", out var submittedContractJson))
        {
            return (false, "approval-contract-missing");
        }

        var (submittedContract, _) = AcceptanceContract.Parse(submittedContractJson);
        if (submittedContract is null
            || !string.Equals(submittedContract.Digest, reviewContract, StringComparison.Ordinal))
        {
            return (false, "approval-contract-changed");
        }

        var requirementDigest = submitted.Data.TryGetValue("requirementDigest", out var submittedRequirement)
            ? submittedRequirement
            : string.Empty;

        if (review.Data.TryGetValue("requirementDigest", out var reviewedRequirement)
            && !string.Equals(reviewedRequirement, requirementDigest, StringComparison.Ordinal))
        {
            return (false, "approval-requirement-changed");
        }

        // The target branch must not have moved since the reviewed baseline.
        var targetHead = _targetHeadProbe is null
            ? await TargetHeadAsync(_root.TargetRepository, cancellationToken).ConfigureAwait(false)
            : await _targetHeadProbe(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(targetHead)
            && !string.IsNullOrWhiteSpace(baseSha)
            && !string.Equals(targetHead.Trim(), baseSha.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (false, "approval-target-moved");
        }

        var data = new Dictionary<string, string>(review.Data, StringComparer.Ordinal)
        {
            ["approvedBy"] = Environment.UserName,
            ["approvedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["approvalKind"] = "operator-merge",
            ["approvalId"] = "approval-" + Guid.NewGuid().ToString("N"),
            ["approvalDigest"] = reviewDigest,
            ["targetBranch"] = "main",
            ["requirementDigest"] = requirementDigest,
            ["evidenceDigest"] = aggregate,
            ["validationEvidenceDigest"] = CheckDigest(evidence, ReviewCheckKind.Validation),
            ["coverageEvidenceDigest"] = CheckDigest(evidence, ReviewCheckKind.RequirementCoverage),
            ["architectureEvidenceDigest"] = CheckDigest(evidence, ReviewCheckKind.ArchitecturalConformance),
            ["securityEvidenceDigest"] = CheckDigest(evidence, ReviewCheckKind.Security),
            ["templateProvenanceDigest"] = TemplateProvenance(submitted.Data, mine)
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return (false, "work-item-not-found");
            }

            var bound = snapshot with { Version = snapshot.Version + 1 };
            var commit = await _store.CommitAsync(
                new WorkItemCommit(bound, snapshot.Version, [FactoryStageDispatchHandler.Event(
                    OperatorEvents.MergeApproved, snapshot.WorkItemId, data)]),
                cancellationToken).ConfigureAwait(false);

            if (commit.Succeeded)
            {
                return (true, null);
            }

            if (commit.Status != CommitStatus.VersionConflict)
            {
                return (false, commit.Reason ?? "approve-commit-refused");
            }
        }

        return (false, "approve-version-conflict");
    }

    private string ReviewEvidencePath => Path.Combine(_root.Root, "verify", "review-evidence.log");

    private static string CheckDigest(IReadOnlyList<ReviewCheckExecution> evidence, ReviewCheckKind kind)
        => evidence.FirstOrDefault(execution => execution.Kind == kind)?.ArtifactDigest ?? string.Empty;

    /// <summary>Binds an approval to the validated template provenance when the requirement named a template.</summary>
    private static string TemplateProvenance(
        IReadOnlyDictionary<string, string> submitted,
        IReadOnlyList<FactoryEventEnvelope> events)
    {
        if (!submitted.TryGetValue("templateReference", out var reference) || string.IsNullOrWhiteSpace(reference))
        {
            return string.Empty;
        }

        var approval = events.LastOrDefault(static e =>
            string.Equals(e.Type, FactoryEventTypes.ContractApproved, StringComparison.Ordinal));

        var manifest = approval?.Data.TryGetValue("manifestDigest", out var recorded) == true
            ? recorded
            : string.Empty;

        var parameters = submitted.TryGetValue("templateParameters", out var value) ? value : "{}";
        return FactoryStageDispatchHandler.Digest(reference + "|" + manifest + "|" + parameters);
    }

    /// <summary>Durable event history for one work item, in commit order.</summary>
    public async ValueTask<IReadOnlyList<FactoryEventEnvelope>> EventsAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return events
            .Where(e => string.Equals(e.Subject, workItemId, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Supervises the released revision (v0.3 §7.2, PA-07). Unlike `run`, this owns the process for the whole
    /// duration: it materialises the exact released sha, starts it, proves identity, executes the frozen contract
    /// against the served process, records the observation, and only then allows OperationallyValidated.
    /// </summary>
    public async ValueTask<ServeOutcome> ServeAsync(
        string workItemId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (_candidates is null || _merges is null)
        {
            return ServeOutcome.Refused("serve-unavailable");
        }

        var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return ServeOutcome.Refused("work-item-not-found");
        }

        if (snapshot.State is not (FactoryWorkState.Released or FactoryWorkState.OperationallyValidated))
        {
            return ServeOutcome.Refused("not-released");
        }

        var events = await EventsAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var release = events.LastOrDefault(static e => string.Equals(e.Type, OperatorEvents.ReleaseCompleted, StringComparison.Ordinal));
        if (release is null || !release.Data.TryGetValue("mergedSha", out var mergedSha) || string.IsNullOrWhiteSpace(mergedSha))
        {
            return ServeOutcome.Refused("no-release-manifest");
        }

        if (!release.Data.TryGetValue("acceptanceContract", out var contractJson))
        {
            return ServeOutcome.Refused("contract-not-executable", mergedSha);
        }

        var (contract, _) = AcceptanceContract.Parse(contractJson);
        if (contract is null)
        {
            return ServeOutcome.Refused("contract-not-executable", mergedSha);
        }

        // One supervisor per (control root, work item). The OS-exclusive handle is the exclusivity mechanism and the
        // durable journal makes the owner inspectable after recovery.
        SupervisorLease lease;
        try
        {
            lease = await SupervisorLease.AcquireAsync(_root.Root, workItemId, mergedSha, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MutationLockUnavailableException exception)
        {
            return ServeOutcome.Refused(exception.Category, mergedSha);
        }

        using (lease)
        {
            var journal = new ServeStatusJournal(Path.Combine(
                _root.Root, "serve", SafeName(workItemId) + ".status.log"));

            var destination = Path.Combine(_root.Root, "serve", mergedSha[..Math.Min(12, mergedSha.Length)]);
            string revisionPath;
            try
            {
                revisionPath = await _merges
                    .MaterialiseAsync(_root.TargetRepository, mergedSha, destination, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                journal.Append(workItemId, mergedSha, "failed", 0, 0, "serve-revision-unavailable:" + exception.Message);
                return ServeOutcome.Refused("serve-revision-unavailable:" + exception.Message, mergedSha);
            }

            // A served revision must be built first: `dotnet run --no-build` in a fresh checkout has no output.
            if (_builds is not null)
            {
                var (serveBuildOk, serveBuildDetail) = await _builds
                    .VerifyAsync(revisionPath, cancellationToken)
                    .ConfigureAwait(false);

                if (!serveBuildOk)
                {
                    journal.Append(workItemId, mergedSha, "failed", 0, 0, "serve-build-failed:" + serveBuildDetail);
                    return ServeOutcome.Refused("serve-build-failed:" + serveBuildDetail, mergedSha);
                }
            }

            CandidateSession session;
            try
            {
                session = await _candidates.StartAsync(revisionPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A named refusal, never an unhandled exception (v0.3 §8).
                journal.Append(workItemId, mergedSha, "failed", 0, 0, "serve-candidate-failed:" + exception.Message);
                return ServeOutcome.Refused("serve-candidate-failed:" + exception.Message, mergedSha);
            }

            try
            {
                if (!session.IdentityVerified)
                {
                    journal.Append(workItemId, mergedSha, "failed", session.ProcessId, 0, "serve-identity-unproven");
                    return ServeOutcome.Refused("serve-identity-unproven", mergedSha);
                }

                journal.Append(workItemId, mergedSha, "serving", session.ProcessId, 0, "pinned release " + mergedSha);

                var probe = await new AcceptanceContractRunner()
                    .RunAsync(contract, session.BaseAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (!probe.Passed)
                {
                    journal.Append(workItemId, mergedSha, "failed", session.ProcessId, 0,
                        "serve-contract-failed:" + probe.FailureSummary);
                    return new ServeOutcome(
                        false, "serve-contract-failed:" + probe.FailureSummary, session.ProcessId,
                        session.BaseAddress.ToString(), "failed", 0, mergedSha);
                }

                // The served process is the materialised release, so the observed identity is that release's sha.
                var data = new Dictionary<string, string>(release.Data, StringComparer.Ordinal)
                {
                    ["expectedSha"] = mergedSha,
                    ["observedSha"] = mergedSha,
                    ["processId"] = session.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["servedUrl"] = session.BaseAddress.ToString(),
                    ["scenarios"] = string.Join(",", probe.Scenarios.Select(static s => s.Id + "=" + (s.Passed ? "pass" : "fail")))
                };

                var observed = snapshot with { Version = snapshot.Version + 1 };
                var observation = await _store.CommitAsync(
                    new WorkItemCommit(observed, snapshot.Version, [FactoryStageDispatchHandler.Event(
                        OperatorEvents.ReleaseObserved, snapshot.WorkItemId, data)]),
                    cancellationToken).ConfigureAwait(false);

                if (!observation.Succeeded)
                {
                    journal.Append(workItemId, mergedSha, "failed", session.ProcessId, 0,
                        "serve-observation-refused:" + observation.Reason);
                    return new ServeOutcome(
                        false, "serve-observation-refused:" + observation.Reason, session.ProcessId,
                        session.BaseAddress.ToString(), "failed", 0, mergedSha);
                }

                await RunAsync(4, cancellationToken).ConfigureAwait(false);

                var deadline = DateTimeOffset.UtcNow + duration;
                var restarts = 0;

                while (DateTimeOffset.UtcNow < deadline)
                {
                    if (!session.IsAliveNow())
                    {
                        journal.Append(workItemId, mergedSha, "crashed", session.ProcessId, restarts,
                            "supervised process exited");
                        await session.Stop().ConfigureAwait(false);

                        if (restarts >= MaxServeRestarts)
                        {
                            journal.Append(workItemId, mergedSha, "failed", session.ProcessId, restarts,
                                "supervisor-restart-budget-exhausted");
                            return new ServeOutcome(
                                false, "supervisor-restart-budget-exhausted", session.ProcessId,
                                session.BaseAddress.ToString(), "failed", restarts, mergedSha);
                        }

                        restarts++;
                        try
                        {
                            session = await _candidates.StartAsync(revisionPath, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            journal.Append(workItemId, mergedSha, "failed", 0, restarts, "restart-failed:" + exception.Message);
                            return ServeOutcome.Refused("supervisor-restart-failed:" + exception.Message, mergedSha);
                        }

                        if (!session.IdentityVerified)
                        {
                            journal.Append(workItemId, mergedSha, "failed", session.ProcessId, restarts,
                                "restart-identity-unproven");
                            return new ServeOutcome(
                                false, "serve-identity-unproven", session.ProcessId,
                                session.BaseAddress.ToString(), "failed", restarts, mergedSha);
                        }

                        // The restarted process must pass the frozen contract before it is reported healthy again.
                        var restartProbe = await new AcceptanceContractRunner()
                            .RunAsync(contract, session.BaseAddress, cancellationToken)
                            .ConfigureAwait(false);

                        if (!restartProbe.Passed)
                        {
                            journal.Append(workItemId, mergedSha, "failed", session.ProcessId, restarts,
                                "restart-contract-failed:" + restartProbe.FailureSummary);
                            return new ServeOutcome(
                                false, "serve-contract-failed:" + restartProbe.FailureSummary, session.ProcessId,
                                session.BaseAddress.ToString(), "failed", restarts, mergedSha);
                        }

                        journal.Append(workItemId, mergedSha, "restarted", session.ProcessId, restarts,
                            "bounded restart " + restarts + " of " + MaxServeRestarts);
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                journal.Append(workItemId, mergedSha, "stopped", session.ProcessId, restarts,
                    "timed serve stopped; not an always-on daemon");

                return new ServeOutcome(
                    true, null, session.ProcessId, session.BaseAddress.ToString(), "stopped", restarts, mergedSha);
            }
            finally
            {
                await session.Stop().ConfigureAwait(false);
            }
        }
    }

    private static string SafeName(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_');
        }

        return builder.ToString();
    }
    /// <summary>The latest review evidence for a work item, read from durable storage rather than a cached copy.</summary>
    private async ValueTask<MergeAuthority?> CurrentEvidenceAsync(string workItemId, CancellationToken cancellationToken)
    {
        var events = await EventsAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var review = events.LastOrDefault(static e => string.Equals(e.Type, OperatorEvents.ReviewAccepted, StringComparison.Ordinal));
        if (review is null)
        {
            return null;
        }

        var evidence = new ReviewEvidenceStore(ReviewEvidencePath).ReadFor(workItemId);
        var submitted = events.LastOrDefault(static e => string.Equals(e.Type, FactoryEventTypes.RequirementSubmitted, StringComparison.Ordinal));

        return new MergeAuthority(
            review.Data.TryGetValue("reviewDigest", out var reviewDigest) ? reviewDigest : string.Empty,
            review.Data.TryGetValue("headSha", out var headSha) ? headSha : string.Empty,
            review.Data.TryGetValue("baseSha", out var baseSha) ? baseSha : string.Empty,
            review.Data.TryGetValue("contractDigest", out var contractDigest) ? contractDigest : string.Empty,
            // The versions below are the ones configured *now*, not the recorded ones: a policy or profile
            // bump must invalidate approvals that were bound to the old value.
            FactoryPolicy.PolicyVersion,
            FactoryPolicy.GuidelinesVersion,
            FactoryPolicy.ProfileVersion,
            FactoryPolicy.RunnerVersion,
            TargetBranch: "main",
            RequirementDigest: review.Data.TryGetValue("requirementDigest", out var requirementDigest) ? requirementDigest : string.Empty,
            EvidenceDigest: review.Data.TryGetValue("evidenceDigest", out var evidenceDigest) ? evidenceDigest : string.Empty,
            ValidationEvidenceDigest: CheckDigest(evidence, ReviewCheckKind.Validation),
            CoverageEvidenceDigest: CheckDigest(evidence, ReviewCheckKind.RequirementCoverage),
            ArchitectureEvidenceDigest: CheckDigest(evidence, ReviewCheckKind.ArchitecturalConformance),
            SecurityEvidenceDigest: CheckDigest(evidence, ReviewCheckKind.Security),
            TemplateProvenanceDigest: submitted is null
                ? string.Empty
                : TemplateProvenance(submitted.Data, events));
    }

    private static async Task<string?> TargetHeadAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            info.ArgumentList.Add("rev-parse");
            info.ArgumentList.Add("HEAD");

            using var process = System.Diagnostics.Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
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

    /// <summary>The most recent operator-visible failure category, from durable evidence.</summary>
    private static string? LastFailure(IReadOnlyList<FactoryEventEnvelope> events)
    {
        for (var index = events.Count - 1; index >= 0; index--)
        {
            var envelope = events[index];
            if (!string.Equals(envelope.Type, OperatorEvents.StageFailed, StringComparison.Ordinal)
                && !string.Equals(envelope.Type, "factory.stage.failed.v1", StringComparison.Ordinal))
            {
                continue;
            }

            if (envelope.Data.TryGetValue("failureCategory", out var category) && !string.IsNullOrWhiteSpace(category))
            {
                return category;
            }

            if (envelope.Data.TryGetValue("rejectReason", out var rejection) && !string.IsNullOrWhiteSpace(rejection))
            {
                return rejection;
            }

            if (envelope.Data.TryGetValue("reason", out var reason) && !string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }

            return envelope.Type;
        }

        return null;
    }
}
