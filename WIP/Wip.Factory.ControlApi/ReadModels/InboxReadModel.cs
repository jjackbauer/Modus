using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wip.Factory.Contracts.Work;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.ControlApi.Services;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Governance;

namespace Wip.Factory.ControlApi.ReadModels;

public sealed partial class FactoryReadModel
{
    public async Task<ApprovalInboxDto> GetApprovalsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<ApprovalItemDto>();

        foreach (var status in snapshot.Statuses)
        {
            var events = snapshot.For(status.WorkItemId);
            var submitted = Last(events, "factory.requirement.submitted.v1");
            var title = Value(submitted, "title") ?? status.WorkItemId;
            var proposed = Last(events, "factory.contract.proposed.v1");
            var approved = Last(events, "factory.contract.approved.v1");
            var rejected = Last(events, "factory.contract.rejected.v1");
            var proposalDigest = status.ProposalDigest;

            var rejectionAfterProposal = rejected is not null && proposed is not null
                                         && rejected.Value.Sequence > proposed.Value.Sequence
                                         && proposalDigest is not null
                                         && string.Equals(Value(rejected, "proposalDigest"), proposalDigest, StringComparison.Ordinal);

            if (string.Equals(status.State, "Proposed", StringComparison.Ordinal)
                && proposalDigest is not null
                && status.FrozenContractDigest is null
                && !rejectionAfterProposal)
            {
                var contract = await GetContractAsync(status.WorkItemId, cancellationToken).ConfigureAwait(false);
                items.Add(new ApprovalItemDto(
                    "contract-" + status.WorkItemId + "-" + proposalDigest[^12..],
                    "contract",
                    status.WorkItemId,
                    title,
                    "Contract proposal with " + contract.Scenarios.Count.ToString(CultureInfo.InvariantCulture) + " scenario(s); "
                    + (contract.UnresolvedQuestions.Count > 0 ? contract.UnresolvedQuestions.Count + " unresolved question(s)" : "no unresolved questions"),
                    proposalDigest,
                    Value(proposed, "requirementDigest") ?? status.RequirementDigest,
                    ["Exact proposal digest", "Requirement digest", "Frozen HTTP acceptance contract"],
                    ["Freezes the contract so implementation may be dispatched", "Makes the proposal digest immutable for this requirement revision"],
                    "operator",
                    "approve-contract",
                    "reject-contract",
                    "/api/instances/{instance}/work-items/" + status.WorkItemId + "/contract",
                    false,
                    null,
                    Time(proposed)?.ToString("O")));
            }

            var review = Last(events, "factory.review.accepted.v1");
            var reviewDigest = Value(review, "reviewDigest");
            var mergeApproval = Last(events, "factory.merge.approved.v1");
            var alreadyApproved = mergeApproval is not null && reviewDigest is not null
                                  && string.Equals(Value(mergeApproval, "approvalDigest"), reviewDigest, StringComparison.Ordinal);

            if (string.Equals(status.State, "ReadyForDelivery", StringComparison.Ordinal)
                && reviewDigest is not null
                && !alreadyApproved)
            {
                var executions = _service.ReviewEvidence.ReadFor(status.WorkItemId);
                var evidenceDigest = ReviewEvidenceStore.AggregateDigest(executions);
                var recordedEvidence = Value(review, "evidenceDigest");
                var reviewedSha = Value(review, "reviewedSha");
                var staleReason = executions.Count != 4
                    ? "missing-check-evidence"
                    : !string.Equals(evidenceDigest, recordedEvidence, StringComparison.Ordinal)
                        ? "evidence-digest-mismatch"
                        : executions.Any(execution => !execution.IsComplete || !execution.Passed || !string.Equals(execution.ReviewedSha, reviewedSha, StringComparison.Ordinal))
                            ? "evidence-stale"
                            : null;

                items.Add(new ApprovalItemDto(
                    "merge-" + status.WorkItemId + "-" + reviewDigest[^12..],
                    "merge",
                    status.WorkItemId,
                    title,
                    "Merge authorization for reviewed revision " + ShortSha(reviewedSha),
                    reviewDigest,
                    reviewedSha,
                    ["Reviewed head SHA", "Base SHA", "Target branch main", "Frozen contract digest", "Four mandatory review evidence digests", "Policy/profile/runner versions"],
                    ["Allows the factory to merge the reviewed head", "Triggers post-merge build, regression and contract verification"],
                    "operator",
                    "approve-merge",
                    null,
                    "/api/instances/{instance}/work-items/" + status.WorkItemId + "/merge-approvals",
                    staleReason is not null,
                    staleReason,
                    Time(review)?.ToString("O")));
            }
        }

        foreach (var template in _service.Templates.List())
        {
            if (template.Approved || template.Revoked)
            {
                continue;
            }

            items.Add(new ApprovalItemDto(
                "template-" + template.Manifest.TemplateId + "-" + template.Manifest.TemplateVersion,
                "template",
                string.Empty,
                template.Manifest.TemplateId + "@" + template.Manifest.TemplateVersion,
                "Approved template policy: " + template.Manifest.SupportedClass,
                template.Manifest.ManifestDigest,
                template.Manifest.TemplateId + "@" + template.Manifest.TemplateVersion,
                ["Immutable template manifest digest", "Scenario body", "Parameter schema"],
                ["Allows exact template instantiations to be policy-approved", "Does not approve any other contract or version"],
                "admin",
                "approve-template",
                "revoke-template",
                "/api/instances/{instance}/templates",
                false,
                null,
                null));
        }

        var itemsArray = items
            .OrderBy(static item => item.Kind == "merge" ? 0 : item.Kind == "contract" ? 1 : 2)
            .ThenByDescending(static item => item.CreatedAtUtc, StringComparer.Ordinal)
            .ToArray();

        return new ApprovalInboxDto(
            itemsArray,
            itemsArray.Count(static item => item.Kind == "contract"),
            itemsArray.Count(static item => item.Kind == "merge"),
            itemsArray.Count(static item => item.Kind == "template"),
            0);
    }

    public async Task<IReadOnlyList<WorkReviewDto>> GetReviewsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var reviews = new List<WorkReviewDto>();
        foreach (var status in snapshot.Statuses)
        {
            var events = snapshot.For(status.WorkItemId);
            var accepted = Last(events, "factory.review.accepted.v1");
            var rework = Last(events, "factory.rework.requested.v1");
            var latest = accepted is not null && (rework is null || accepted.Value.Sequence > rework.Value.Sequence)
                ? accepted
                : rework;
            if (latest is null)
            {
                continue;
            }

            var executions = _service.ReviewEvidence.ReadFor(status.WorkItemId);
            var title = Value(Last(events, "factory.requirement.submitted.v1"), "title") ?? status.WorkItemId;
            var verdict = accepted is not null && (rework is null || accepted.Value.Sequence > rework.Value.Sequence)
                ? "accepted"
                : "rework";
            reviews.Add(new WorkReviewDto(
                status.WorkItemId,
                title,
                status.State,
                verdict,
                Value(latest, "reviewDigest") ?? Value(accepted, "reviewDigest"),
                Value(latest, "reviewedSha") ?? Value(accepted, "reviewedSha"),
                Value(latest, "contractDigest") ?? Value(accepted, "contractDigest"),
                latest.Value.Event.TimeUtc.ToString("O"),
                executions.Select(ToExecutionDto).ToArray()));
        }

        return reviews
            .OrderByDescending(static review => review.RecordedAtUtc, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ReleaseListDto> GetReleasesAsync(CancellationToken cancellationToken)    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<ReleaseListItemDto>();

        foreach (var status in snapshot.Statuses)
        {
            var events = snapshot.For(status.WorkItemId);
            var review = Last(events, "factory.review.accepted.v1");
            var release = Last(events, "factory.release.completed.v1");
            if (review is null && release is null)
            {
                continue;
            }

            var title = Value(Last(events, "factory.requirement.submitted.v1"), "title") ?? status.WorkItemId;
            var mergedSha = Value(release, "mergedSha");
            var verification = mergedSha is null
                ? null
                : _service.ReleaseVerifications.ReadAll()
                    .LastOrDefault(record => string.Equals(record.WorkItemId, status.WorkItemId, StringComparison.Ordinal)
                                             && string.Equals(record.MergedSha, mergedSha, StringComparison.Ordinal));
            var serving = _service.Serving.GetServing(status.WorkItemId, mergedSha ?? string.Empty);

            items.Add(new ReleaseListItemDto(
                status.WorkItemId,
                title,
                status.State,
                mergedSha,
                Value(release, "contractDigest") ?? Value(review, "contractDigest"),
                verification?.VerifiedAtUtc.ToString("O"),
                verification?.Verified ?? false,
                serving.Active ? "serving" : serving.Status ?? "not-served",
                serving.Url,
                Time(release)?.ToString("O")));
        }

        var ordered = items
            .OrderByDescending(static item => item.MergedAtUtc, StringComparer.Ordinal)
            .ThenBy(static item => item.WorkItemId, StringComparer.Ordinal)
            .ToArray();
        return new ReleaseListDto(ordered, ordered.Length);
    }

    public async Task<ReleaseDto> GetReleaseAsync(string workItemId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = snapshot.Statuses.FirstOrDefault(candidate =>
            string.Equals(candidate.WorkItemId, workItemId, StringComparison.Ordinal));
        if (status is null)
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        var release = BuildRelease(workItemId, snapshot.For(workItemId));
        return release ?? throw ApiException.NotFound(
            "release-not-available",
            "This work item has no accepted review or recorded release yet.");
    }

    public IReadOnlyList<TemplateDto> GetTemplates()
    {
        return _service.Templates.List()
            .Select(template =>
            {
                var marker = ParseMarker(template.ApprovalMarkerJson);
                return new TemplateDto(
                    template.Manifest.TemplateId,
                    template.Manifest.TemplateVersion,
                    template.Manifest.SchemaVersion,
                    template.Manifest.SupportedClass,
                    template.Manifest.ManifestDigest,
                    template.Manifest.Parameters.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray(),
                    template.Manifest.Scenarios.ToJsonString(),
                    template.Approved,
                    template.Revoked,
                    marker.GetValueOrDefault("approvedAtUtc"),
                    marker.GetValueOrDefault("approver"),
                    template.Error);
            })
            .ToArray();
    }

    private static Dictionary<string, string> ParseMarker(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null)
            {
                return result;
            }

            foreach (var (key, value) in node)
            {
                if (value is not null)
                {
                    result[key] = value.ToJsonString().Trim('"');
                }
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }

    public async Task<TemplateDto> RegisterTemplateAsync(string manifestJson, CancellationToken cancellationToken)
    {
        JsonNode node;
        try
        {
            node = JsonNode.Parse(manifestJson) ?? throw ApiException.BadRequest("template-invalid", "The template manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw ApiException.BadRequest("template-invalid-json", exception.Message);
        }

        try
        {
            var manifest = _service.Templates.Register(node);
            return GetTemplates().Single(template =>
                string.Equals(template.TemplateId, manifest.TemplateId, StringComparison.Ordinal)
                && template.TemplateVersion == manifest.TemplateVersion);
        }
        catch (TemplateVersionConflictException exception)
        {
            throw ApiException.Conflict("template-version-conflict", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw ApiException.BadRequest("template-refused", exception.Message);
        }
    }

    public async Task<TemplateDto> ApproveTemplateAsync(
        string templateId,
        int version,
        string manifestDigest,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            throw ApiException.BadRequest("confirmation-required", "Template approval requires explicit confirmation of the exact manifest digest.");
        }

        if (!_service.Templates.Approve(templateId, version, manifestDigest))
        {
            throw ApiException.Conflict("template-approval-refused", "The template version or digest is unknown, revoked, or does not match the stored body.");
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return GetTemplates().Single(template => template.TemplateId == templateId && template.TemplateVersion == version);
    }

    public async Task<TemplateDto> RevokeTemplateAsync(string templateId, int version, CancellationToken cancellationToken)
    {
        _service.Templates.Revoke(templateId, version);
        await Task.CompletedTask.ConfigureAwait(false);
        return GetTemplates().SingleOrDefault(template => template.TemplateId == templateId && template.TemplateVersion == version)
               ?? throw ApiException.NotFound("template-not-found", "No such template version is registered.");
    }

    // ------------------------------------------------------------------ meta, health, settings

    public CapabilitiesDto GetCapabilities() => new(
        WorkIntake: true,
        ContractApproval: true,
        ContractRejection: true,
        Dispatch: true,
        MergeApproval: true,
        ServeSupervision: Options.ExecutionEnabled,
        Templates: true,
        AgentProfiles: true,
        Analytics: true,
        Audit: true,
        Cancellation: false,
        PauseResume: false,
        Scheduling: false,
        Retry: true);

    public PolicyVersionsDto GetPolicyVersions() => new(
        FactoryPolicy.PolicyVersion,
        FactoryPolicy.GuidelinesVersion,
        FactoryPolicy.ProfileVersion,
        FactoryPolicy.RunnerVersion,
        ReviewPolicy.CheckRunnerVersion,
        ReviewPolicy.ArchitecturePolicy,
        ReviewPolicy.SecurityPolicyVersion);

    public MetaDto GetMeta() => new(
        ApiSchema.Version,
        Options.AdapterVersion,
        ApiSchema.ProtocolVersion,
        Options.InstanceId,
        Options.ImplementationRunnerName,
        Options.ExecutionEnabled,
        GetCapabilities(),
        GetPolicyVersions(),
        DateTimeOffset.UtcNow);

    public FactoryDto GetFactory() => new(
        Options.InstanceId,
        Options.Label,
        Options.Factory.Root,
        Options.Factory.TargetRepository,
        "main",
        SupportedAppClass.AspNetCoreHttpNet10,
        Options.ImplementationRunnerName,
        Options.ExecutionEnabled,
        GetCapabilities(),
        GetPolicyVersions(),
        DateTimeOffset.UtcNow);

    public async Task<HealthDto> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var statuses = await Runtime.StatusAsync(cancellationToken).ConfigureAwait(false);
            var events = await Runtime.Store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
            var dispatch = await _service.Dispatch.StatusAsync().ConfigureAwait(false);
            return new HealthDto(
                true,
                true,
                Options.InstanceId,
                statuses.Count,
                events.Count,
                Math.Max(0, events.Count - 1),
                dispatch.Running,
                Options.ImplementationRunnerName,
                Runtime.Store.TornRecordCount,
                DateTimeOffset.UtcNow,
                null);
        }
        catch (Exception exception)
        {
            return new HealthDto(
                false,
                false,
                Options.InstanceId,
                0,
                0,
                0,
                false,
                Options.ImplementationRunnerName,
                0,
                DateTimeOffset.UtcNow,
                exception.GetType().Name + ": " + exception.Message);
        }
    }

    public SettingsDto GetSettings()
    {
        var settings = new List<SettingDto>
        {
            Setting("factory.controlRoot", Options.Factory.Root, "adapter startup", false, "Resolved and validated to never overlap the target repository."),
            Setting("factory.targetRepository", Options.Factory.TargetRepository, "adapter startup", false, "This adapter instance is bound to one repository. Register another adapter instance for another repository."),
            Setting("factory.targetBranch", "main", "factory policy", false, "The factory alpha merges into main."),
            Setting("factory.supportedClass", SupportedAppClass.AspNetCoreHttpNet10, "factory policy", false, "The only declared generated-application class."),
            Setting("execution.enabled", Options.ExecutionEnabled ? "true" : "false", "adapter startup", false, "Controls candidate hosting, builds and merges; the factory still enforces each gate."),
            Setting("implementation.runner", Options.ImplementationRunnerName, "adapter startup", false, Options.Implementation == ImplementationMode.DeterministicDemo
                ? "Deterministic engineering runner: real worktree/build/commit, not a model and not independent acceptance."
                : Options.Implementation == ImplementationMode.DeepSeek
                    ? "Real DeepSeek-backed implementation runner."
                    : "No implementation runner. Implementation stages fail with implementation-runner-unavailable."),
            Setting("model.credentials", Options.ModelApiKey is not null ? "configured (redacted)" : "not configured", "environment", false, "Secrets are never sent to the browser."),
            Setting("serve.maxDurationSeconds", Options.MaxServeSeconds.ToString(CultureInfo.InvariantCulture), "adapter startup", false, "Upper bound for one supervised serve window."),
            Setting("retention.events", "factory journal retention", "factory policy", false, "The adapter exposes bounded pages and a durable cursor; it does not truncate the factory journal."),
            Setting("retention.audit", "48h idempotency records; append-only audit", "adapter policy", false, "Audit records remain until an operator removes them."),
            Setting("policy.mandatory", FactoryPolicy.PolicyVersion, "factory code", false, "Policy versions are build-time constants in the factory alpha."),
            Setting("review.checks", ReviewPolicy.CheckRunnerVersion, "factory code", false, "Four mandatory deterministic checks.")
        };

        return new SettingsDto(
            Options.InstanceId,
            Options.Factory.Root,
            Options.Factory.TargetRepository,
            "main",
            SupportedAppClass.AspNetCoreHttpNet10,
            Options.ImplementationRunnerName,
            Options.ExecutionEnabled,
            Options.ModelApiKey is not null,
            "deepseek",
            GetPolicyVersions(),
            GetCapabilities(),
            settings);
    }

    private static SettingDto Setting(string key, string value, string source, bool mutable, string? note)
        => new(key, value, source, mutable, note);

    private static string ShortSha(string? sha)
        => string.IsNullOrWhiteSpace(sha) ? "unknown" : sha.Length <= 12 ? sha : sha[..12];
}
