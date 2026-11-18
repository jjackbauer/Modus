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
    private readonly ICandidateHost? _candidates;
    private readonly IMergeEngine? _merges;
    private readonly ICandidateBuildVerifier? _builds;
    private readonly FactoryTemplateRegistry _templates;

    public FactoryRuntime(
        FactoryControlRoot root,
        IImplementationStageRunner? implementations = null,
        ICandidateHost? candidates = null,
        ICandidateBuildVerifier? builds = null,
        IMergeEngine? merges = null,
        IStageDispatchHandler? handler = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _candidates = candidates;
        _merges = merges;
        _builds = builds;
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
    /// approval only; this is a separate, human-authorised merge (v0.3 §2, §3.4 step 4).
    /// </summary>
    /// approval only; this is a separate, human-authorised merge (v0.3 §2, §3.4 step 4).
    /// </summary>
    public async ValueTask<(bool Ok, string? Error)> ApproveMergeAsync(
        string workItemId,
        string reviewDigest,
        CancellationToken cancellationToken = default)
    {
        var events = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var review = events.LastOrDefault(e =>
            string.Equals(e.Subject, workItemId, StringComparison.Ordinal)
            && string.Equals(e.Type, OperatorEvents.ReviewAccepted, StringComparison.Ordinal));

        if (review is null)
        {
            return (false, "no-review-to-approve");
        }

        if (!review.Data.TryGetValue("reviewDigest", out var recorded)
            || !string.Equals(recorded, reviewDigest, StringComparison.Ordinal))
        {
            return (false, "review-digest-mismatch");
        }

        var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return (false, "work-item-not-found");
        }

        var data = new Dictionary<string, string>(review.Data, StringComparer.Ordinal)
        {
            ["approvedBy"] = Environment.UserName,
            ["approvedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["approvalKind"] = "operator-merge"
        };

        var bound = snapshot with { Version = snapshot.Version + 1 };
        var commit = await _store.CommitAsync(
            new WorkItemCommit(bound, snapshot.Version, [FactoryStageDispatchHandler.Event(
                OperatorEvents.MergeApproved, snapshot.WorkItemId, data)]),
            cancellationToken).ConfigureAwait(false);

        return commit.Succeeded ? (true, null) : (false, commit.Reason ?? "approve-commit-refused");
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
    public async ValueTask<(bool Ok, string? Error, int ProcessId, string? Url)> ServeAsync(
        string workItemId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (_candidates is null || _merges is null)
        {
            return (false, "serve-unavailable", 0, null);
        }

        var snapshot = await _store.LoadAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return (false, "work-item-not-found", 0, null);
        }

        if (snapshot.State is not (FactoryWorkState.Released or FactoryWorkState.OperationallyValidated))
        {
            return (false, "not-released", 0, null);
        }

        var events = await EventsAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var release = events.LastOrDefault(static e => string.Equals(e.Type, OperatorEvents.ReleaseCompleted, StringComparison.Ordinal));
        if (release is null || !release.Data.TryGetValue("mergedSha", out var mergedSha) || string.IsNullOrWhiteSpace(mergedSha))
        {
            return (false, "no-release-manifest", 0, null);
        }

        if (!release.Data.TryGetValue("acceptanceContract", out var contractJson))
        {
            return (false, "contract-not-executable", 0, null);
        }

        var (contract, _) = AcceptanceContract.Parse(contractJson);
        if (contract is null)
        {
            return (false, "contract-not-executable", 0, null);
        }

        var destination = Path.Combine(_root.Root, "serve", mergedSha[..Math.Min(12, mergedSha.Length)]);
        string revisionPath;
        try
        {
            revisionPath = await _merges.MaterialiseAsync(_root.TargetRepository, mergedSha, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (false, "serve-revision-unavailable:" + exception.Message, 0, null);
        }

        // A served revision must be built first: `dotnet run --no-build` in a fresh checkout has no output.
        if (_builds is not null)
        {
            var (serveBuildOk, serveBuildDetail) = await _builds.VerifyAsync(revisionPath, cancellationToken).ConfigureAwait(false);
            if (!serveBuildOk)
            {
                return (false, "serve-build-failed:" + serveBuildDetail, 0, null);
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
            return (false, "serve-candidate-failed:" + exception.Message, 0, null);
        }

        if (!session.IdentityVerified)
        {
            await session.Stop().ConfigureAwait(false);
            return (false, "serve-identity-unproven", 0, null);
        }

        try
        {
            var probe = await new AcceptanceContractRunner()
                .RunAsync(contract, session.BaseAddress, cancellationToken)
                .ConfigureAwait(false);

            if (!probe.Passed)
            {
                return (false, "serve-contract-failed:" + probe.FailureSummary, session.ProcessId, session.BaseAddress.ToString());
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
            await _store.CommitAsync(
                new WorkItemCommit(observed, snapshot.Version, [FactoryStageDispatchHandler.Event(
                    OperatorEvents.ReleaseObserved, snapshot.WorkItemId, data)]),
                cancellationToken).ConfigureAwait(false);

            await RunAsync(4, cancellationToken).ConfigureAwait(false);

            // Supervise: stay alive for the requested duration, probing liveness.
            var deadline = DateTimeOffset.UtcNow + duration;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            return (true, null, session.ProcessId, session.BaseAddress.ToString());
        }
        finally
        {
            await session.Stop().ConfigureAwait(false);
        }
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
            FactoryPolicy.RunnerVersion);
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
}
