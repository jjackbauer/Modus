using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Telemetry;
using Wip.Factory.Contracts.Work;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Core;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Governance;

namespace Wip.Factory.ControlApi.ReadModels;

public sealed partial class FactoryReadModel
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Regex ShaPattern = new("^[0-9a-fA-F]{7,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<WorkItemDetailDto> GetDetailAsync(string workItemId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = snapshot.Statuses.FirstOrDefault(candidate =>
            string.Equals(candidate.WorkItemId, workItemId, StringComparison.Ordinal));
        if (status is null)
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        var summary = await BuildSummaryAsync(status, snapshot, cancellationToken).ConfigureAwait(false);
        var events = snapshot.For(workItemId);
        var submitted = Last(events, "factory.requirement.submitted.v1");
        var requirementText = Value(submitted, "requirement") ?? string.Empty;
        var titleOverride = Value(submitted, "title");
        var (document, _) = RequirementDocument.Parse(requirementText, titleOverride);

        var contractJson = Value(submitted, "acceptanceContract");
        var parsedContract = contractJson is null
            ? ((AcceptanceContract? Contract, IReadOnlyList<string> Errors))(null, Array.Empty<string>())
            : AcceptanceContract.Parse(contractJson);
        var acceptanceContract = parsedContract.Contract;

        var proposalEvent = Last(events, "factory.contract.proposed.v1");
        var proposal = proposalEvent is not null && Value(proposalEvent, "proposal") is { } proposalJson
            ? ContractProposal.FromJson(proposalJson)
            : null;

        var contract = BuildContract(status, events, proposal, acceptanceContract, contractJson);
        var attempts = LoadAttempts(workItemId);
        var executions = _service.ReviewEvidence.ReadFor(workItemId);
        var reviews = BuildReviewHistory(events, executions);
        var reviewsDigest = reviews.LastOrDefault(static review => review.Outcome == "accepted")?.ReviewDigest;
        var release = BuildRelease(workItemId, events);
        var serving = _service.Serving.GetServing(workItemId, release?.MergedSha ?? string.Empty);
        var stages = BuildStages(status, events, attempts, executions, release, serving);
        var attention = BuildAttention(status, summary, events, contract, reviewsDigest, release, serving);
        var dispatch = await _service.Dispatch.StatusAsync().ConfigureAwait(false);
        var dispatchActive = dispatch.Running;
        var nextActions = BuildNextActions(status, summary, contract, reviewsDigest, release, serving, dispatchActive);

        var requirement = document is null
            ? null
            : new RequirementDto(
                document.Title,
                document.SupportedClassId,
                document.Digest,
                document.RawText,
                document.Expectations,
                document.AcceptanceContractReference,
                document.TemplateReference,
                document.TemplateParametersJson);

        var eventDtos = events
            .Skip(Math.Max(0, events.Count - 500))
            .Select(pair => ToEventDto(pair.Sequence, pair.Event, snapshot))
            .ToArray();

        var detail = new WorkItemDetailDto(
            summary,
            Options.InstanceId,
            requirement,
            summary.Planning,
            contract,
            eventDtos,
            stages,
            attempts.Select(ToAttemptDto).ToArray(),
            reviews,
            release is null ? [] : [release],
            release,
            serving,
            attention,
            nextActions,
            WorkStateMachine.LegalTargets(ParseState(status.State)).Select(static state => state.ToString()).ToArray(),
            ExecutionHealth(status, contract, reviewsDigest, serving, dispatchActive),
            null,
            dispatchActive,
            events.Count,
            events.Count == 0 ? null : events[^1].Sequence,
            DateTimeOffset.UtcNow);

        return detail;
    }

    public async Task<ContractDto> GetContractAsync(string workItemId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = snapshot.Statuses.FirstOrDefault(candidate =>
            string.Equals(candidate.WorkItemId, workItemId, StringComparison.Ordinal));
        if (status is null)
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        var events = snapshot.For(workItemId);
        var submitted = Last(events, "factory.requirement.submitted.v1");
        var contractJson = Value(submitted, "acceptanceContract");
        var parsedContract = contractJson is null
            ? ((AcceptanceContract? Contract, IReadOnlyList<string> Errors))(null, Array.Empty<string>())
            : AcceptanceContract.Parse(contractJson);
        var acceptanceContract = parsedContract.Contract;
        var proposalEvent = Last(events, "factory.contract.proposed.v1");
        var proposal = proposalEvent is not null && Value(proposalEvent, "proposal") is { } proposalJson
            ? ContractProposal.FromJson(proposalJson)
            : null;
        return BuildContract(status, events, proposal, acceptanceContract, contractJson);
    }

    private static ContractDto BuildContract(
        WorkItemStatus status,
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        ContractProposal? proposal,
        AcceptanceContract? acceptanceContract,
        string? contractJson)
    {
        var approved = Last(events, "factory.contract.approved.v1");
        var proposed = Last(events, "factory.contract.proposed.v1");
        var rejection = Last(events, "factory.contract.rejected.v1");
        var rejectedAfterProposal = rejection is not null
                                    && proposed is not null
                                    && rejection.Value.Sequence > proposed.Value.Sequence
                                    && proposal is not null
                                    && string.Equals(Value(rejection, "proposalDigest"), proposal.ProposalDigest, StringComparison.Ordinal);

        var scenarios = proposal?.Scenarios
            .Select(static scenario => new ContractScenarioDto(scenario.Id, scenario.Kind, scenario.Path, scenario.Expectation))
            .ToArray()
            ?? acceptanceContract?.Scenarios
                .Select((scenario, index) => new ContractScenarioDto(
                    "AC-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    scenario.Method,
                    scenario.Path,
                    "Expected HTTP " + scenario.ExpectedStatus.ToString(CultureInfo.InvariantCulture) + " at " + scenario.Path))
                .ToArray()
            ?? [];

        var frozen = status.FrozenContractDigest;
        return new ContractDto(
            proposal?.ProposalDigest ?? status.ProposalDigest,
            proposal?.RequirementDigest ?? status.RequirementDigest,
            proposal?.Executable ?? contractJson is not null,
            scenarios,
            proposal?.UnresolvedQuestions ?? [],
            frozen,
            acceptanceContract?.Digest,
            Value(approved, "approvedAtUtc"),
            frozen is not null,
            frozen is null && proposal is not null && proposed is not null && !rejectedAfterProposal,
            rejectedAfterProposal ? proposal?.ProposalDigest : null,
            rejectedAfterProposal ? Value(rejection, "rejectedAtUtc") : null,
            rejectedAfterProposal ? Value(rejection, "reason") : null);
    }

    private IReadOnlyList<ImplementationAttemptRecord> LoadAttempts(string workItemId)
    {
        try
        {
            return new ImplementationAttemptJournal(
                    ImplementationAttemptJournal.PathFor(Options.Factory.Root, workItemId))
                .ReadAll();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private IReadOnlyList<ReviewHistoryDto> BuildReviewHistory(
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        IReadOnlyList<ReviewCheckExecution> currentExecutions)
    {
        var history = new List<ReviewHistoryDto>();
        foreach (var (sequence, envelope) in events)
        {
            if (envelope.Type is not ("factory.review.accepted.v1" or "factory.rework.requested.v1" or "factory.stage.failed.v1"))
            {
                continue;
            }

            var accepted = envelope.Type == "factory.review.accepted.v1";
            var executions = currentExecutions;
            if (accepted && envelope.Data.TryGetValue("evidence", out var evidenceJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<ReviewCheckExecution[]>(evidenceJson, EventJson);
                    if (parsed is { Length: > 0 })
                    {
                        executions = parsed;
                    }
                }
                catch (JsonException)
                {
                }
            }

            var outcome = accepted
                ? "accepted"
                : envelope.Type == "factory.rework.requested.v1" ? "rejected" : "failed";
            history.Add(new ReviewHistoryDto(
                ReviewId: "review-" + sequence.ToString(CultureInfo.InvariantCulture),
                ReviewDigest: envelope.Data.GetValueOrDefault("reviewDigest"),
                HeadSha: envelope.Data.GetValueOrDefault("reviewedSha") ?? envelope.Data.GetValueOrDefault("headSha"),
                BaseSha: envelope.Data.GetValueOrDefault("baseSha"),
                ContractDigest: envelope.Data.GetValueOrDefault("contractDigest"),
                EvidenceDigest: envelope.Data.GetValueOrDefault("evidenceDigest"),
                Outcome: outcome,
                Reason: envelope.Data.GetValueOrDefault("rejectReason") ?? envelope.Data.GetValueOrDefault("failureCategory"),
                ChecksSummary: envelope.Data.GetValueOrDefault("checks"),
                RecordedAtUtc: envelope.TimeUtc.ToString("O"),
                Executions: accepted || outcome == "failed" ? executions.Select(ToExecutionDto).ToArray() : []));
        }

        return history;
    }

    private static AttemptDto ToAttemptDto(ImplementationAttemptRecord attempt) => new(
        attempt.AttemptId,
        attempt.WorkItemId,
        attempt.AttemptNumber,
        attempt.SessionId,
        attempt.Outcome,
        attempt.FailureCategory,
        attempt.FailureRole,
        attempt.Detail,
        attempt.WorktreePath,
        attempt.BaseSha,
        attempt.HeadSha,
        attempt.PreviousAttemptId,
        attempt.StartedAtUtc,
        attempt.FinishedAtUtc);

    private static ReviewExecutionDto ToExecutionDto(ReviewCheckExecution execution) => new(        execution.Kind.ToString(),
        execution.CheckId,
        execution.ExecutedBy,
        execution.ExecutedVersion,
        execution.ReviewedSha,
        execution.PolicyVersion,
        execution.Outcome,
        execution.Passed,
        execution.SubjectDigest,
        execution.ArtifactDigest,
        execution.ExecutedAtUtc,
        execution.Detail);

    private ReleaseDto? BuildRelease(string workItemId, IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events)
    {
        var released = Last(events, "factory.release.completed.v1");
        var review = Last(events, "factory.review.accepted.v1");
        if (released is null && review is null)
        {
            return null;
        }

        var mergedSha = Value(released, "mergedSha");
        var verification = mergedSha is null
            ? null
            : _service.ReleaseVerifications.ReadAll()
                .LastOrDefault(record => string.Equals(record.WorkItemId, workItemId, StringComparison.Ordinal)
                                         && string.Equals(record.MergedSha, mergedSha, StringComparison.Ordinal));

        var release = new ReleaseDto(
            workItemId,
            mergedSha,
            Value(released, "mergedTreeSha"),
            Value(released, "headSha") ?? Value(review, "headSha"),
            Value(released, "baseSha") ?? Value(review, "baseSha"),
            Value(released, "contractDigest") ?? Value(review, "contractDigest"),
            Value(released, "postMergeDetail"),
            Value(released, "postMergeScenarios"),
            Value(released, "reconciled"),
            Value(released, "operationId"),
            Value(released, "verifiedAtUtc"),
            verification is null ? null : ToVerificationDto(verification),
            _service.Serving.GetServing(workItemId, mergedSha ?? string.Empty),
            released is not null);

        return release;
    }

    private static ReleaseVerificationDto ToVerificationDto(ReleaseVerificationRecord record) => new(
        record.WorkItemId,
        record.MergeOperationId,
        record.ApprovedHeadSha,
        record.BaseSha,
        record.MergedSha,
        record.MergedTreeSha,
        record.ContractDigest,
        record.BuildEvidenceDigest,
        record.RegressionEvidenceDigest,
        record.ContractEvidenceDigest,
        record.ScenarioSummary,
        record.VerifierVersion,
        record.Outcome,
        record.Verified,
        record.Detail,
        record.VerifiedAtUtc,
        record.RecordDigest);

    // ------------------------------------------------------------------ stages

    private IReadOnlyList<StageDto> BuildStages(
        WorkItemStatus status,
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        IReadOnlyList<ImplementationAttemptRecord> attempts,
        IReadOnlyList<ReviewCheckExecution> executions,
        ReleaseDto? release,
        ServingDto serving)
    {
        var submitted = Last(events, "factory.requirement.submitted.v1");
        var proposed = Last(events, "factory.contract.proposed.v1");
        var approved = Last(events, "factory.contract.approved.v1");
        var rejected = Last(events, "factory.contract.rejected.v1");
        var aligned = Last(events, "factory.requirement.aligned.v1");
        var architecture = Last(events, "factory.architecture.aligned.v1");
        var ready = Last(events, "factory.implementation.ready.v1");
        var requested = Last(events, "factory.implementation.requested.v1");
        var completed = Last(events, "factory.implementation.completed.v1");
        var review = Last(events, "factory.review.accepted.v1");
        var rework = Last(events, "factory.rework.requested.v1");
        var mergeApproval = Last(events, "factory.merge.approved.v1");
        var deliveryRequested = Last(events, "factory.delivery.requested.v1");
        var candidate = Last(events, "factory.release.candidate-ready.v1");
        var released = Last(events, "factory.release.completed.v1");
        var observed = Last(events, "factory.release.observed.v1");
        var operational = Last(events, "factory.operations.validated.v1");
        var failure = Last(events, "factory.stage.failed.v1");

        var state = status.State;
        var failureStage = DetermineFailureStage(state, failure, completed, approved);
        var reviewFailed = failure is not null && completed is not null
                           && failure.Value.Sequence > completed.Value.Sequence
                           && (review is null || failure.Value.Sequence > review.Value.Sequence);
        var contractRejected = rejected is not null && proposed is not null && rejected.Value.Sequence > proposed.Value.Sequence;

        var stages = new List<StageDto>
        {
            Stage(
                "intake",
                proposed is not null ? "completed" : state is "Proposed" ? "completed" : "completed",
                Time(submitted),
                Time(proposed),
                "operator",
                1,
                "Requirement parsed into the supported class and observable expectations",
                null,
                ["requirement-document"],
                ["requirement-digest"],
                Ids(submitted),
                Evidence("requirement", null, null, Value(submitted, "requirementDigest"), "Canonical requirement digest")),

            Stage(
                "contract",
                contractRejected ? "rejected"
                    : status.FrozenContractDigest is not null ? "completed"
                    : proposed is not null ? "waiting-authorization"
                    : "pending",
                Time(proposed),
                status.FrozenContractDigest is not null ? Time(approved) : contractRejected ? Time(rejected) : null,
                contractRejected ? Value(rejected, "rejectedBy") ?? "operator" : Value(approved, "approvedBy") ?? "operator",
                1,
                contractRejected
                    ? "Rejected: " + (Value(rejected, "reason") ?? "operator decision")
                    : status.FrozenContractDigest is not null ? "Frozen at " + status.FrozenContractDigest
                    : proposed is not null ? "Awaiting exact-digest operator approval"
                    : null,
                contractRejected ? Value(rejected, "reason") : null,
                ["requirement-digest"],
                status.FrozenContractDigest is null ? ["proposal-digest"] : ["proposal-digest", "frozen-contract-digest"],
                Ids(proposed).Concat(Ids(approved)).Concat(Ids(rejected)).ToArray(),
                Evidence("contract", null, null, status.FrozenContractDigest ?? status.ProposalDigest, "Contract proposal and approval binding")),

            Stage(
                "requirements",
                aligned is not null ? "completed" : status.FrozenContractDigest is not null ? "pending" : "pending",
                Time(approved),
                Time(aligned),
                "factory.stage-handler",
                1,
                aligned is null ? null : "Requirement aligned to the frozen contract",
                null,
                ["frozen-contract"],
                aligned is null ? [] : ["aligned-requirement"],
                Ids(aligned),
                Evidence("requirements", null, null, Value(aligned, "contractDigest"), "Aligned requirement revision")),

            Stage(
                "architecture",
                architecture is not null ? "completed" : aligned is not null ? "pending" : "pending",
                Time(aligned),
                Time(architecture),
                "factory-architecture-inspector",
                1,
                architecture is null ? null : "Deterministic architecture checks passed",
                null,
                ["aligned-requirement"],
                architecture is null ? [] : ["architecture-evidence"],
                Ids(architecture),
                Evidence("architecture", null, null, Value(architecture, "contractDigest"), "Architecture decision evidence")),

            Stage(
                "implementation",
                state is "InImplementation" ? "active"
                    : state is "NeedsRework" ? "needs-rework"
                    : failureStage == "implementation" ? "failed"
                    : completed is not null || review is not null || state is "InReview" or "ReadyForDelivery" or "DeliveryValidation" or "ReleaseCandidate" or "Released" or "OperationallyValidated" ? "completed"
                    : requested is not null ? "active"
                    : "pending",
                Time(ready) ?? Time(requested),
                Time(completed) ?? Time(rework),
                attempts.Count > 0 ? attempts[^1].SessionId : requested is null ? null : "session-impl-" + status.WorkItemId.ToLowerInvariant(),
                Math.Max(1, attempts.Count),
                completed is null ? null : Value(completed, "changedFiles") + " file(s) changed; head " + Value(completed, "headSha"),
                failureStage == "implementation" ? status.LastFailure : null,
                ["frozen-contract", "aligned-requirement"],
                completed is null ? [] : ["source-diff", "implementation-commit"],
                Ids(ready).Concat(Ids(requested)).Concat(Ids(completed)).Concat(Ids(rework)).ToArray(),
                attempts.Select(attempt => new StageEvidenceDto(
                    "implementation-agent",
                    attempt.SessionId,
                    attempt.AttemptId,
                    attempt.HeadSha,
                    attempt.Outcome + (attempt.FailureCategory is null ? string.Empty : ": " + attempt.FailureCategory))).ToArray()),

            Stage(
                "review",
                reviewFailed ? "failed"
                    : state is "InReview" ? "active"
                    : state is "NeedsRework" ? "needs-rework"
                    : review is not null ? "completed"
                    : failureStage == "review" ? "failed"
                    : rework is not null ? "needs-rework"
                    : "pending",
                Time(completed),
                Time(review) ?? Time(rework) ?? (failureStage == "review" ? Time(failure) : null),
                "factory-review-checks@3",
                1,
                review is not null ? "All four mandatory checks passed" : rework is not null ? "Review rejected; rework counter advanced" : null,
                failureStage == "review" ? status.LastFailure : null,
                ["implementation-commit", "frozen-contract"],
                review is null ? [] : ["review-evidence", "review-digest"],
                Ids(review).Concat(Ids(rework)).Concat(Ids(failure)).ToArray(),
                executions.Select(ToExecutionDto).Select(execution => new StageEvidenceDto(
                    execution.ExecutedBy,
                    null,
                    null,
                    execution.ArtifactDigest,
                    execution.Kind + "=" + execution.Outcome)).ToArray()),

            Stage(
                "delivery",
                status.State is "ReadyForDelivery" ? "waiting-authorization"
                    : status.State is "DeliveryValidation" ? "active"
                    : candidate is not null || released is not null || status.State is "ReleaseCandidate" or "Released" or "OperationallyValidated" ? "completed"
                    : failureStage == "delivery" ? "failed"
                    : "pending",
                Time(review),
                Time(candidate),
                Value(mergeApproval, "approvedBy") ?? "operator",
                mergeApproval is null ? 0 : 1,
                mergeApproval is null ? null : "Merge authorized for review " + Value(mergeApproval, "approvalDigest"),
                failureStage == "delivery" ? status.LastFailure : null,
                ["review-digest", "review-evidence"],
                mergeApproval is null ? [] : ["merge-authorization"],
                Ids(mergeApproval).Concat(Ids(deliveryRequested)).Concat(Ids(candidate)).ToArray(),
                Evidence("delivery", null, null, Value(mergeApproval, "approvalDigest"), "Operator merge authorization bound to review evidence")),

            Stage(
                "release",
                status.State is "ReleaseCandidate" ? "active"
                    : status.State is "Released" or "OperationallyValidated" ? "completed"
                    : "pending",
                Time(candidate),
                Time(released),
                Value(released, "verifierVersion") ?? "post-merge-verifier@1",
                released is null ? 0 : 1,
                released is null ? null : "Merged " + (Value(released, "mergedSha") ?? "unknown") + " verified: " + (Value(released, "postMergeDetail") ?? "verified"),
                failureStage == "release" ? status.LastFailure : null,
                ["approved-head", "frozen-contract"],
                released is null ? [] : ["merged-revision", "release-verification"],
                Ids(candidate).Concat(Ids(released)).ToArray(),
                release?.Verification is null
                    ? []
                    : [new StageEvidenceDto(
                        release.Verification.VerifierVersion,
                        null,
                        null,
                        release.Verification.RecordDigest,
                        "build=" + release.Verification.BuildEvidenceDigest + "; regression=" + release.Verification.RegressionEvidenceDigest + "; contract=" + release.Verification.ContractEvidenceDigest)]),

            Stage(
                "serving",
                status.State is "OperationallyValidated" ? "completed"
                    : serving.Active ? "active"
                    : status.State is "Released" ? "pending"
                    : "pending",
                Time(released),
                Time(operational),
                "factory-serve-supervisor",
                serving.Active ? 1 : 0,
                serving.Active ? "Serving " + serving.Url + " (pid " + serving.ProcessId + ")" : serving.Detail,
                serving.Status == "failed" || serving.Status == "crashed" ? serving.Detail : null,
                ["merged-revision"],
                serving.Active ? ["served-endpoint"] : [],
                Ids(observed).Concat(Ids(operational)).ToArray(),
                [new StageEvidenceDto(
                    "factory-serve-supervisor",
                    null,
                    null,
                    release?.Verification?.RecordDigest,
                    serving.Status ?? "not-supervised")])
        };

        return stages;
    }

    private static string DetermineFailureStage(
        string state,
        (long Sequence, FactoryEventEnvelope Event)? failure,
        (long Sequence, FactoryEventEnvelope Event)? completed,
        (long Sequence, FactoryEventEnvelope Event)? approved)
    {
        if (state != "Failed" || failure is null)
        {
            return string.Empty;
        }

        if (completed is not null && failure.Value.Sequence > completed.Value.Sequence)
        {
            return "review";
        }

        return approved is not null && failure.Value.Sequence > approved.Value.Sequence ? "implementation" : "contract";
    }

    private static StageDto Stage(
        string id,
        string state,
        DateTimeOffset? entered,
        DateTimeOffset? exited,
        string? actor,
        int attempts,
        string? decision,
        string? failure,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        IReadOnlyList<string> eventIds,
        IReadOnlyList<StageEvidenceDto> evidence) => new(
        id,
        StageLabels[id],
        state,
        entered?.ToString("O"),
        exited?.ToString("O"),
        entered is not null && exited is not null ? (long)(exited.Value - entered.Value).TotalSeconds : null,
        actor,
        attempts,
        decision,
        failure,
        inputs,
        outputs,
        eventIds.Where(static eventId => !string.IsNullOrWhiteSpace(eventId)).Distinct(StringComparer.Ordinal).ToArray(),
        evidence);

    private static IReadOnlyList<string> Ids((long Sequence, FactoryEventEnvelope Event)? match)
        => match is null ? [] : [match.Value.Event.Id.Value];

    private static IReadOnlyList<StageEvidenceDto> Evidence(
        string role,
        string? sessionId,
        string? attemptId,
        string? digest,
        string? summary)
        => digest is null && summary is null
            ? []
            : [new StageEvidenceDto(role, sessionId, attemptId, digest, summary)];

    // ------------------------------------------------------------------ health, attention, actions

    private static string ExecutionHealth(
        WorkItemStatus status,
        ContractDto contract,
        string? reviewDigest,
        ServingDto serving,
        bool dispatchActive)
    {
        if (serving.Active)
        {
            return "serving";
        }

        if (status.State == "Failed")
        {
            return "terminal-failure";
        }

        if (status.State == "Blocked")
        {
            return "blocked";
        }

        if (status.State == "NeedsRework")
        {
            return "awaiting-rework";
        }

        if (contract.AwaitingApproval || (status.State == "ReadyForDelivery" && reviewDigest is not null))
        {
            return "waiting-authorization";
        }

        if (dispatchActive && status.State is "InImplementation" or "InReview" or "DeliveryValidation" or "ReleaseCandidate")
        {
            return "executing";
        }

        if (status.State == "OperationallyValidated")
        {
            return "operational";
        }

        if (status.State == "Released")
        {
            return "release-verified";
        }

        return status.State == "Proposed" ? "awaiting-dispatch" : "idle";
    }

    private IReadOnlyList<AttentionDto> BuildAttention(
        WorkItemStatus status,
        WorkItemSummaryDto summary,
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        ContractDto contract,
        string? reviewDigest,
        ReleaseDto? release,
        ServingDto serving)
    {
        var items = new List<AttentionDto>();


        if (contract.IsFrozen && !contract.Executable)
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-contract-not-executable",
                "critical",
                "Frozen contract is not executable",
                "A contract was frozen for " + status.WorkItemId + " without executable HTTP scenarios.",
                "Requirement coverage and post-merge verification cannot run a non-executable oracle, so further dispatch will fail.",
                "Inspect the frozen contract evidence and submit a revised requirement with an explicit acceptance contract.",
                "A new requirement revision creates a new work item; the frozen oracle on this item remains immutable evidence.",
                status.WorkItemId,
                "inspect-events",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/events"));
        }
        if (contract.AwaitingApproval)
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-contract",
                "warning",
                "Contract approval required",
                "A contract proposal was recorded for " + status.WorkItemId + " and no contract is frozen.",
                "The factory will not dispatch implementation until an operator approves the exact proposal digest.",
                "Inspect the proposal, its scenarios and unresolved questions, then approve the exact digest or reject it with a reason.",
                "Approval freezes the contract and makes the work item eligible for dispatch. Rejection leaves the item proposed and records the rejection.",
                status.WorkItemId,
                "approve-contract",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/contract"));
        }

        if (reviewDigest is not null && status.State == "ReadyForDelivery")
        {
            var evidenceDigest = ReviewEvidenceStore.AggregateDigest(_service.ReviewEvidence.ReadFor(status.WorkItemId));
            var approval = Last(events, "factory.merge.approved.v1");
            var approved = approval is not null && string.Equals(Value(approval, "approvalDigest"), reviewDigest, StringComparison.Ordinal);
            if (!approved)
            {
                items.Add(new AttentionDto(
                    "attention-" + status.WorkItemId + "-merge",
                    "warning",
                    "Merge authorization required",
                    "Independent review accepted revision " + (reviewDigest.Length > 20 ? reviewDigest[..20] + "…" : reviewDigest) + ".",
                    "The factory will not merge without a separate human authorization bound to this exact review and evidence digest.",
                    "Inspect the four review checks and diff, then authorize the merge by typing the review digest.",
                    "Authorization permits the factory to merge the reviewed head into main and run post-merge verification.",
                    status.WorkItemId,
                    "approve-merge",
                    "/api/instances/{instance}/work-items/" + status.WorkItemId + "/merge-approvals"));
            }
        }

        if (status.State == "Failed")
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-failed",
                "critical",
                "Terminal failure: " + (status.LastFailure ?? "unnamed category"),
                "Work item " + status.WorkItemId + " reached Failed with failure category " + (status.LastFailure ?? "unnamed") + ".",
                "Failed is terminal in the current factory state machine. Retrying under the same item is not a supported transition.",
                "Inspect the failing stage evidence and attempt history, then submit a revised requirement as a new work item.",
                "A new work item gets a new requirement digest and contract; it does not silently reopen the failed item.",
                status.WorkItemId,
                "inspect-events",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/events"));
        }

        if (status.State == "Blocked")
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-blocked",
                "warning",
                "Work item blocked",
                "Work item " + status.WorkItemId + " is in the factory's Blocked state.",
                "Blocked work does not advance through a normal dispatch cycle.",
                "Inspect the durable events and failure category to identify the blocking condition. The control plane exposes no transition that the factory has not authorized.",
                "No state change occurs until a governed factory transition is available.",
                status.WorkItemId,
                "inspect-events",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/events"));
        }

        if (status.State == "NeedsRework" && status.LastFailure is not null)
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-rework",
                "warning",
                "Rework requested",
                "Review rejected the implementation: " + status.LastFailure,
                "The factory is waiting for another implementation attempt; the rework budget is bounded.",
                "Inspect the failing check evidence, then start an eligible dispatch cycle to spend a governed rework attempt.",
                "The factory will run another implementation attempt with the rejection context; exhausting the budget moves the item to Failed.",
                status.WorkItemId,
                "dispatch",
                "/api/instances/{instance}/dispatch"));
        }

        if (status.LastFailure is not null && status.LastFailure.Contains("implementation-runner-unavailable", StringComparison.Ordinal))
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-runner",
                "critical",
                "Implementation runner unavailable",
                "The implementation stage failed because the factory has no configured implementation runner.",
                "No agent can perform work until model credentials or an explicitly selected runner are configured.",
                "Configure the runner for the adapter instance and restart it, or use the factory CLI with credentials.",
                "Dispatch can then run; the existing failure evidence remains in the work item's history.",
                status.WorkItemId,
                "inspect-settings",
                "/api/instances/{instance}/settings"));
        }

        if (serving.Status is "failed" or "crashed")
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-serving",
                "critical",
                "Supervised serving unhealthy",
                "The last serve observation for " + status.WorkItemId + " is " + serving.Status + ": " + (serving.Detail ?? "no detail"),
                "A released artifact is not necessarily a running application; the preview is hidden until a reachable verified serve window exists.",
                "Inspect the serve history and the last verification evidence before starting another bounded serve window.",
                "A new serve window re-materialises the merged revision and must pass the frozen contract before it is reported healthy.",
                status.WorkItemId,
                "inspect-release",
                "/api/instances/{instance}/releases"));
        }

        if (status.State == "Released" && !serving.Active)
        {
            items.Add(new AttentionDto(
                "attention-" + status.WorkItemId + "-not-serving",
                "info",
                "Release verified, not currently serving",
                "The merged revision passed post-merge verification; no supervised serve window is active.",
                "A verified release is distinct from a running application. Opening a preview would be misleading.",
                "Start a bounded supervised serve window if you need to exercise the application.",
                "The factory starts the exact merged revision, proves process identity and runs the frozen contract before returning a URL.",
                status.WorkItemId,
                "start-serve",
                "/api/instances/{instance}/releases"));
        }

        return items;
    }

    private IReadOnlyList<NextActionDto> BuildNextActions(
        WorkItemStatus status,
        WorkItemSummaryDto summary,
        ContractDto contract,
        string? reviewDigest,
        ReleaseDto? release,
        ServingDto serving,
        bool dispatchActive)
    {
        var actions = new List<NextActionDto>();
        var runnerAvailable = Options.Implementation != ImplementationMode.None && Options.ExecutionEnabled;


        if (contract.AwaitingApproval)
        {
            actions.Add(new NextActionDto(
                "approve-contract",
                "Approve contract digest",
                "A proposal is frozen-pending operator approval.",
                "Recording approval binds the exact proposal digest; later bytes cannot be approved under the same digest.",
                true,
                "operator",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/contract/approve"));
            actions.Add(new NextActionDto(
                "reject-contract",
                "Reject proposal",
                "The proposal can be explicitly rejected without changing work state.",
                "A rejection is durable; the same proposal digest cannot later be approved.",
                true,
                "operator",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/contract/reject"));
        }

        if (reviewDigest is not null && status.State == "ReadyForDelivery")
        {
            actions.Add(new NextActionDto(
                "approve-merge",
                "Authorize merge",
                "Review evidence is current and merge authorization is the remaining gate.",
                "Recording authorization allows the factory to merge the reviewed head and run post-merge verification.",
                true,
                "operator",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/merge-approvals"));
        }

        if (status.State is "Released" or "OperationallyValidated")
        {
            actions.Add(new NextActionDto(
                serving.Active ? "stop-serve" : "start-serve",
                serving.Active ? "Stop supervised serve" : "Start supervised serve",
                serving.Active ? "A bounded serve window is active." : "The merged revision is verified and eligible for supervision.",
                serving.Active ? "Cancels the serve window; the factory stops the process it owns." : "The factory materialises the merged revision, proves identity and runs the frozen contract.",
                Options.ExecutionEnabled,
                "operator",
                serving.Active
                    ? "/api/instances/{instance}/work-items/" + status.WorkItemId + "/serve"
                    : "/api/instances/{instance}/work-items/" + status.WorkItemId + "/serve"));
        }

        var nonExecutableFrozen = status.FrozenContractDigest is not null && !contract.Executable;
        if (!nonExecutableFrozen
            && status.State is not ("Failed" or "Blocked" or "Cancelled" or "Closed")
            && status.State is not ("Released" or "OperationallyValidated")
            && !dispatchActive)
        {
            actions.Add(new NextActionDto(
                "dispatch",
                contract.AwaitingApproval ? "Dispatch after approval" : "Start dispatch cycle",
                "The factory dispatch worker is the only component that claims events and runs stages.",
                runnerAvailable
                    ? "Runs an exclusive bounded dispatch cycle. Eligible events advance; nothing is guaranteed to complete."
                    : "The cycle will run but implementation stages will fail with implementation-runner-unavailable.",
                runnerAvailable,
                "operator",
                "/api/instances/{instance}/dispatch"));
        }

        if (release is { ReleaseRecorded: true })
        {
            actions.Add(new NextActionDto(
                "inspect-release",
                "Inspect release evidence",
                "A release verification record exists for this work item.",
                "Opens the merged revision, evidence digests and serving history; does not start the application.",
                true,
                "viewer",
                "/api/instances/{instance}/releases"));
        }

        actions.Add(new NextActionDto(
            "inspect-events",
            "Inspect event history",
            "Every displayed transition is backed by a durable event.",
            "Read-only; no factory state changes.",
            true,
            "viewer",
            "/api/instances/{instance}/work-items/" + status.WorkItemId + "/events"));

        if (summary.ReviewDigest is not null)
        {
            actions.Add(new NextActionDto(
                "view-diff",
                "Inspect reviewed diff",
                "The review is bound to a source revision.",
                "Read-only git diff of the reviewed base and head; no filesystem browsing.",
                true,
                "viewer",
                "/api/instances/{instance}/work-items/" + status.WorkItemId + "/diff"));
        }

        return actions;
    }

    // ------------------------------------------------------------------ diff and files

    public async Task<DiffDto> GetDiffAsync(string workItemId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var events = snapshot.For(workItemId);
        if (events.Count == 0)
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        var review = Last(events, "factory.review.accepted.v1");
        var completed = Last(events, "factory.implementation.completed.v1");
        var baseSha = Value(review, "baseSha") ?? Value(completed, "baseSha");
        var headSha = Value(review, "reviewedSha") ?? Value(completed, "headSha");
        if (baseSha is null || headSha is null || !ShaPattern.IsMatch(baseSha) || !ShaPattern.IsMatch(headSha))
        {
            throw ApiException.NotFound("diff-not-available", "This work item has no reviewed base/head revision pair.");
        }

        var names = await GitAsync(["diff", "--name-status", baseSha + ".." + headSha], cancellationToken).ConfigureAwait(false);
        var files = names.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .Select(line =>
            {
                var parts = line.Split('\t');
                return parts.Length >= 3
                    ? new DiffFileDto(parts[2], parts[0], parts[1])
                    : new DiffFileDto(parts.Length >= 2 ? parts[1] : line, parts[0], null);
            })
            .ToArray();

        var diff = await GitAsync(["diff", "--no-ext-diff", "--unified=3", baseSha + ".." + headSha, "--"], cancellationToken).ConfigureAwait(false);
        var truncated = diff.Output.Length > Options.MaxDiffBytes;
        var patch = truncated ? diff.Output[..Options.MaxDiffBytes] : diff.Output;

        return new DiffDto(workItemId, baseSha, headSha, files, patch, truncated, Options.MaxDiffBytes);
    }

    public async Task<FileContentDto> GetFileAsync(string workItemId, string path, CancellationToken cancellationToken)
    {
        var diff = await GetDiffAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains('\0')
            || normalized.Split('/').Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw ApiException.BadRequest("path-invalid", "The requested path is not a repository-relative source path.");
        }

        if (!diff.Files.Any(file => string.Equals(file.Path.Replace('\\', '/'), normalized, StringComparison.Ordinal)))
        {
            throw ApiException.Forbidden("path-not-in-reviewed-change", "Only files changed by this reviewed revision may be inspected.");
        }

        var show = await GitAsync(["show", diff.HeadSha + ":" + normalized], cancellationToken).ConfigureAwait(false);
        if (show.OutputBytes.Any(static value => value == 0))
        {
            throw new ApiException(415, "binary-artifact", "The requested file is binary and is not served through the source viewer.", "unsupported");
        }

        var truncated = show.OutputBytes.Length > Options.MaxFileBytes;
        var bytes = truncated ? show.OutputBytes[..Options.MaxFileBytes] : show.OutputBytes;
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        return new FileContentDto(normalized, diff.HeadSha, text, truncated, Options.MaxFileBytes, MediaType(normalized));
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".cs" or ".rs" or ".ts" or ".js" or ".csproj" or ".md" or ".yml" or ".yaml" or ".toml" or ".css" or ".html" => "text/plain",
        _ => "text/plain"
    };

    private async Task<(string Output, byte[] OutputBytes)> GitAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = Options.Factory.TargetRepository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(info)
            ?? throw ApiException.Unavailable("git-unavailable", "git could not be started for read-only evidence inspection.");

        using var stdout = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(stdout, cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw ApiException.Conflict("git-read-failed", "git refused the read-only evidence request: " + (stderr.Length > 500 ? stderr[^500..] : stderr));
        }

        var bytes = stdout.ToArray();
        return (System.Text.Encoding.UTF8.GetString(bytes), bytes);
    }

    private static FactoryWorkState ParseState(string state)
        => Enum.TryParse<FactoryWorkState>(state, ignoreCase: true, out var parsed) ? parsed : FactoryWorkState.Proposed;

    public async Task<LineageDto> GetLineageAsync(string workItemId, CancellationToken cancellationToken)
    {
        var detail = await GetDetailAsync(workItemId, cancellationToken).ConfigureAwait(false);
        var nodes = new List<LineageNodeDto>
        {
            new("work:" + workItemId, "work-item", workItemId, detail.Summary.State, workItemId, null),
            new("requirement:" + workItemId, "requirement", "Requirement " + ShortSha(detail.Summary.RequirementDigest), detail.Summary.RequirementDigest, workItemId, "work:" + workItemId)
        };
        var edges = new List<GraphEdgeDto>
        {
            new GraphEdgeDto("work:" + workItemId, "requirement:" + workItemId)
        };

        if (detail.Contract.ProposalDigest is not null)
        {
            nodes.Add(new LineageNodeDto(
                "contract:" + detail.Contract.ProposalDigest,
                "contract",
                "Contract " + ShortSha(detail.Contract.ProposalDigest),
                detail.Contract.FrozenDigest ?? "proposed",
                workItemId,
                "requirement:" + workItemId));
            edges.Add(new GraphEdgeDto("requirement:" + workItemId, "contract:" + detail.Contract.ProposalDigest));
        }

        foreach (var stage in detail.Stages.Where(static stage => stage.State != "pending"))
        {
            var stageId = "stage:" + stage.Id;
            nodes.Add(new LineageNodeDto(stageId, "stage", stage.Label, stage.State, workItemId, "work:" + workItemId));
            edges.Add(new GraphEdgeDto("work:" + workItemId, stageId));
            if (detail.Contract.ProposalDigest is not null && stage.Id == "contract")
            {
                edges.Add(new GraphEdgeDto("contract:" + detail.Contract.ProposalDigest, stageId));
            }

            foreach (var evidence in stage.Evidence.Where(static evidence => evidence.ArtifactDigest is not null))
            {
                var artifactId = "artifact:" + evidence.ArtifactDigest;
                if (nodes.All(node => node.Id != artifactId))
                {
                    nodes.Add(new LineageNodeDto(artifactId, "artifact", "Evidence " + ShortSha(evidence.ArtifactDigest), evidence.Summary, workItemId, stageId));
                    edges.Add(new GraphEdgeDto(stageId, artifactId));
                }
            }
        }

        foreach (var attempt in detail.Attempts)
        {
            var attemptId = "attempt:" + attempt.AttemptId;
            var sessionId = "session:" + attempt.SessionId;
            if (nodes.All(node => node.Id != sessionId))
            {
                nodes.Add(new LineageNodeDto(sessionId, "agent-session", attempt.SessionId, "Implementation session", workItemId, "stage:implementation"));
                edges.Add(new GraphEdgeDto("stage:implementation", sessionId));
            }

            nodes.Add(new LineageNodeDto(attemptId, "attempt", "Attempt " + attempt.AttemptNumber, attempt.Outcome + (attempt.FailureCategory is null ? string.Empty : ": " + attempt.FailureCategory), workItemId, sessionId));
            edges.Add(new GraphEdgeDto(sessionId, attemptId));

            if (attempt.HeadSha is not null)
            {
                var commitId = "commit:" + attempt.HeadSha;
                if (nodes.All(node => node.Id != commitId))
                {
                    nodes.Add(new LineageNodeDto(commitId, "commit", "Commit " + ShortSha(attempt.HeadSha), attempt.HeadSha, workItemId, attemptId));
                    edges.Add(new GraphEdgeDto(attemptId, commitId));
                }
            }
        }

        var traces = await _service.Traces.QueryAsync(new WorkItemId(workItemId), cancellationToken).ConfigureAwait(false);
        foreach (var trace in traces)
        {
            var sessionId = "session:" + trace.SessionId.Value;
            if (nodes.All(node => node.Id != sessionId))
            {
                nodes.Add(new LineageNodeDto(sessionId, "agent-session", trace.SessionId.Value, trace.ModelId.Value + " · " + trace.Outcome, workItemId, "stage:implementation"));
                edges.Add(new GraphEdgeDto("stage:implementation", sessionId));
            }

            foreach (var invocation in trace.ToolInvocations ?? [])
            {
                var toolId = "tool:" + trace.SessionId.Value + ":" + invocation.ArgumentsDigest;
                if (nodes.All(node => node.Id != toolId))
                {
                    nodes.Add(new LineageNodeDto(
                        toolId,
                        "tool-operation",
                        invocation.ToolName,
                        invocation.Failed ? "failed" : "succeeded",
                        workItemId,
                        sessionId));
                    edges.Add(new GraphEdgeDto(sessionId, toolId));
                }
            }
        }

        foreach (var review in detail.Reviews)
        {
            var reviewId = "review:" + review.ReviewId;
            nodes.Add(new LineageNodeDto(reviewId, "review", "Review " + review.Outcome, review.ReviewDigest, workItemId, "stage:review"));
            edges.Add(new GraphEdgeDto("stage:review", reviewId));
            foreach (var execution in review.Executions)
            {
                var executionId = "review-check:" + review.ReviewId + ":" + execution.Kind;
                nodes.Add(new LineageNodeDto(executionId, "review-check", execution.Kind, execution.Outcome, workItemId, reviewId));
                edges.Add(new GraphEdgeDto(reviewId, executionId));
            }
        }

        if (detail.Release?.MergedSha is not null)
        {
            var releaseId = "release:" + detail.Release.MergedSha;
            nodes.Add(new LineageNodeDto(releaseId, "release", "Release " + ShortSha(detail.Release.MergedSha), detail.Release.PostMergeDetail, workItemId, "stage:release"));
            edges.Add(new GraphEdgeDto("stage:release", releaseId));
        }

        if (detail.Serving.Active)
        {
            var servingId = "serving:" + detail.Summary.WorkItemId;
            nodes.Add(new LineageNodeDto(servingId, "serving", "Serving process", detail.Serving.Url, workItemId, "stage:serving"));
            edges.Add(new GraphEdgeDto("stage:serving", servingId));
        }

        return new LineageDto(workItemId, nodes, edges);
    }
}
