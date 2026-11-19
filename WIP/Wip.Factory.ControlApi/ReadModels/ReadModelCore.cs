using System.Globalization;
using System.Text.Json;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Work;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.ControlApi.Services;
using Wip.Factory.Core;
using Wip.Factory.Host.Factory;
using Wip.Factory.Wip.Governance;
using Wip.Factory.Wip.Isolation;

namespace Wip.Factory.ControlApi.ReadModels;

public sealed record WorkQuery(
    string? State,
    string? Search,
    string? Sort,
    int Limit,
    int Offset,
    string? Indicator);

/// <summary>
/// Read-only projection over the factory's authoritative commands and durable events. It derives operator-facing
/// indicators; it never invents a <see cref="FactoryWorkState"/> and never writes factory state.
/// </summary>
public sealed partial class FactoryReadModel
{
    private static readonly string[] StageOrder =
        ["intake", "contract", "requirements", "architecture", "implementation", "review", "delivery", "release", "serving"];

    private static readonly Dictionary<string, string> StageLabels = new(StringComparer.Ordinal)
    {
        ["intake"] = "Work intake",
        ["contract"] = "Contract proposal",
        ["requirements"] = "Requirement alignment",
        ["architecture"] = "Architecture refinement",
        ["implementation"] = "Implementation",
        ["review"] = "Independent review",
        ["delivery"] = "Merge authorization",
        ["release"] = "Merge & release verification",
        ["serving"] = "Supervised serving"
    };

    private readonly FactoryService _service;
    private readonly SecretRedactor _redactor = new();

    public FactoryReadModel(FactoryService service) => _service = service;

    private ControlApiOptions Options => _service.Options;
    private FactoryRuntime Runtime => _service.Runtime;

    public sealed record Snapshot(
        IReadOnlyList<FactoryEventEnvelope> Events,
        IReadOnlyList<WorkItemStatus> Statuses,
        IReadOnlyDictionary<string, long> SequenceByEventId)
    {
        public IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> For(string workItemId)
            => Events
                .Select((envelope, index) => (Sequence: (long)index, Event: envelope))
                .Where(pair => string.Equals(pair.Event.Subject, workItemId, StringComparison.Ordinal))
                .ToArray();
    }

    public async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var statuses = await Runtime.StatusAsync(cancellationToken).ConfigureAwait(false);
        var events = await Runtime.Store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var sequences = events
            .Select((envelope, index) => (envelope.Id.Value, Sequence: (long)index))
            .GroupBy(static pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last().Sequence, StringComparer.Ordinal);
        return new Snapshot(events, statuses, sequences);
    }

    public async Task<PageDto<WorkItemSummaryDto>> GetWorkItemsAsync(
        WorkQuery query,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var summaries = new List<WorkItemSummaryDto>(snapshot.Statuses.Count);
        foreach (var status in snapshot.Statuses)
        {
            summaries.Add(await BuildSummaryAsync(status, snapshot, cancellationToken).ConfigureAwait(false));
        }

        IEnumerable<WorkItemSummaryDto> filtered = summaries;

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            filtered = filtered.Where(item => string.Equals(item.State, query.State, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Indicator))
        {
            filtered = filtered.Where(item => string.Equals(item.DerivedIndicator, query.Indicator, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(item =>
                item.WorkItemId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.RequirementDigest.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        filtered = (query.Sort ?? "updated-desc").ToLowerInvariant() switch
        {
            "id" => filtered.OrderBy(static item => item.WorkItemId, StringComparer.Ordinal),
            "id-desc" => filtered.OrderByDescending(static item => item.WorkItemId, StringComparer.Ordinal),
            "state" => filtered.OrderBy(static item => item.State, StringComparer.Ordinal).ThenByDescending(static item => item.UpdatedAtUtc, StringComparer.Ordinal),
            "created-asc" => filtered.OrderBy(static item => item.CreatedAtUtc, StringComparer.Ordinal),
            "created-desc" => filtered.OrderByDescending(static item => item.CreatedAtUtc, StringComparer.Ordinal),
            "title" => filtered.OrderBy(static item => item.Title, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(static item => item.UpdatedAtUtc, StringComparer.Ordinal)
        };

        var all = filtered.ToArray();
        var page = all.Skip(query.Offset).Take(query.Limit).ToArray();
        return new PageDto<WorkItemSummaryDto>(page, query.Limit, query.Offset, all.Length, query.Offset + page.Length < all.Length);
    }

    public async Task<WorkItemSummaryDto?> GetSummaryAsync(string workItemId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var status = snapshot.Statuses.FirstOrDefault(candidate =>
            string.Equals(candidate.WorkItemId, workItemId, StringComparison.Ordinal));
        return status is null ? null : await BuildSummaryAsync(status, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkItemSummaryDto> BuildSummaryAsync(
        WorkItemStatus status,
        Snapshot snapshot,
        CancellationToken cancellationToken)
    {
        var events = snapshot.For(status.WorkItemId);
        var submitted = Last(events, "factory.requirement.submitted.v1");
        var title = Value(submitted, "title") ?? status.WorkItemId;
        var review = Last(events, "factory.review.accepted.v1");
        var reviewDigest = Value(review, "reviewDigest");
        var frozen = status.FrozenContractDigest;
        var proposal = status.ProposalDigest;

        var rejection = Last(events, "factory.contract.rejected.v1");
        var rejectedCurrent = rejection is not null
                              && proposal is not null
                              && string.Equals(Value(rejection, "proposalDigest"), proposal, StringComparison.Ordinal)
                              && rejection.Value.Sequence > Last(events, "factory.contract.proposed.v1")?.Sequence;

        var awaitingContract = string.Equals(status.State, "Proposed", StringComparison.Ordinal)
                               && proposal is not null
                               && frozen is null
                               && !rejectedCurrent;

        var mergeApproval = Last(events, "factory.merge.approved.v1");
        var awaitingMerge = string.Equals(status.State, "ReadyForDelivery", StringComparison.Ordinal)
                            && reviewDigest is not null
                            && !(mergeApproval is not null
                                 && string.Equals(Value(mergeApproval, "approvalDigest"), reviewDigest, StringComparison.Ordinal));

        var planning = await _service.Planner.GetAsync(status.WorkItemId, cancellationToken).ConfigureAwait(false);
        var created = events.Count > 0 ? events[0].Event.TimeUtc : DateTimeOffset.MinValue;
        var updated = events.Count > 0 ? events[^1].Event.TimeUtc : created;

        return new WorkItemSummaryDto(
            status.WorkItemId,
            status.State,
            status.Version,
            title,
            Options.Factory.TargetRepository,
            status.RequirementDigest,
            status.ProposalDigest,
            status.FrozenContractDigest,
            reviewDigest,
            status.LastFailure,
            status.EventCount,
            created == DateTimeOffset.MinValue ? null : created.ToString("O"),
            updated == DateTimeOffset.MinValue ? null : updated.ToString("O"),
            awaitingContract,
            awaitingMerge,
            DerivedIndicator(status, awaitingContract, awaitingMerge),
            planning);
    }

    private static string DerivedIndicator(WorkItemStatus status, bool awaitingContract, bool awaitingMerge)
    {
        if (awaitingContract)
        {
            return "waiting-contract-approval";
        }

        if (awaitingMerge)
        {
            return "waiting-merge-approval";
        }

        if (status.State == "InReview" && status.LastFailure is not null)
        {
            return "verification-failed";
        }

        if (status.State == "InImplementation" && status.LastFailure is not null)
        {
            return "implementation-failed";
        }

        return status.State switch
        {
            "InImplementation" => "active-implementation",
            "InReview" => "running-verification",
            "NeedsRework" => "needs-rework",
            "Blocked" => "blocked",
            "Failed" => "terminal-failure",
            "Released" => "released",
            "OperationallyValidated" => "operationally-validated",
            "DeliveryValidation" => "delivery-in-progress",
            "ReleaseCandidate" => "release-in-progress",
            "Cancelled" => "cancelled",
            "Closed" => "closed",
            "Proposed" => "awaiting-intake-dispatch",
            _ => status.State.ToLowerInvariant()
        };
    }

    // ------------------------------------------------------------------ events

    public async Task<PageDto<EventDto>> GetEventsAsync(
        string workItemId,
        long after,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Statuses.Any(status => string.Equals(status.WorkItemId, workItemId, StringComparison.Ordinal)))
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        var events = snapshot.For(workItemId)
            .Where(pair => pair.Sequence > after)
            .Take(limit)
            .Select(pair => ToEventDto(pair.Sequence, pair.Event, snapshot))
            .ToArray();

        var total = snapshot.For(workItemId).Count;
        return new PageDto<EventDto>(events, limit, (int)after, total, events.Length == limit);
    }

    public async Task<PageDto<EventStreamEnvelopeDto>> GetEventStreamPageAsync(
        long after,
        int limit,
        CancellationToken cancellationToken)
    {
        var events = await Runtime.Store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var selected = events
            .Select((envelope, index) => (Sequence: (long)index, Event: envelope))
            .Where(pair => pair.Sequence > after)
            .Take(limit)
            .Select(pair => ToStreamEnvelope(pair.Sequence, pair.Event))
            .ToArray();

        return new PageDto<EventStreamEnvelopeDto>(selected, limit, (int)after, events.Count, selected.Length == limit);
    }

    public async Task<(long LatestSequence, int EventCount)> GetEventWatermarkAsync(CancellationToken cancellationToken)
    {
        var events = await Runtime.Store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return (events.Count == 0 ? -1 : events.Count - 1, events.Count);
    }

    private EventDto ToEventDto(long sequence, FactoryEventEnvelope envelope, Snapshot snapshot)
        => new(
            sequence,
            envelope.Id.Value,
            envelope.Type,
            envelope.Source,
            envelope.Subject,
            envelope.TimeUtc,
            envelope.CorrelationId?.Value,
            envelope.CausationId?.Value,
            envelope.Data.TryGetValue("workItemId", out var workItemId) ? workItemId : envelope.Subject,
            StageForEvent(envelope.Type),
            string.IsNullOrWhiteSpace(envelope.Data.GetValueOrDefault("agentDefinitionId")) ? null : envelope.Data["agentDefinitionId"],
            SessionForEvent(envelope),
            envelope.Data.GetValueOrDefault("attemptId"),
            envelope.Data.GetValueOrDefault("operationId"),
            SummaryForEvent(envelope),
            BoundData(envelope.Data));

    private EventStreamEnvelopeDto ToStreamEnvelope(long sequence, FactoryEventEnvelope envelope)
        => new(
            sequence,
            envelope.Id.Value,
            Options.InstanceId,
            envelope.Subject,
            envelope.Data.GetValueOrDefault("agentDefinitionId"),
            SessionForEvent(envelope),
            envelope.Data.GetValueOrDefault("attemptId"),
            envelope.Data.GetValueOrDefault("operationId"),
            envelope.CorrelationId?.Value,
            envelope.CausationId?.Value,
            envelope.Type,
            StageForEvent(envelope.Type),
            envelope.TimeUtc,
            SummaryForEvent(envelope),
            BoundData(envelope.Data));

    private IReadOnlyDictionary<string, string> BoundData(IReadOnlyDictionary<string, string> data)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in data)
        {
            if (key is "requirement" or "acceptanceContract" or "proposal" or "evidence")
            {
                result[key] = value.Length <= 180
                    ? value
                    : "[" + key + " omitted from event payload; " + value.Length.ToString(CultureInfo.InvariantCulture) + " bytes available through the typed view]";
                continue;
            }

            var redacted = _redactor.Redact(value, out _);
            result[key] = redacted.Length <= Options.MaxEventDataLength
                ? redacted
                : redacted[..Options.MaxEventDataLength] + "…";
        }

        return result;
    }

    private string? SessionForEvent(FactoryEventEnvelope envelope)
    {
        if (envelope.Data.TryGetValue("sessionId", out var session) && !string.IsNullOrWhiteSpace(session))
        {
            return session;
        }

        if (envelope.Type.StartsWith("factory.implementation.", StringComparison.Ordinal)
            || envelope.Type.StartsWith("factory.rework.", StringComparison.Ordinal))
        {
            return "session-impl-" + envelope.Subject.ToLowerInvariant();
        }

        if (envelope.Type.StartsWith("factory.review.", StringComparison.Ordinal))
        {
            return "review-" + envelope.Subject.ToLowerInvariant();
        }

        return null;
    }

    private static string StageForEvent(string type) => type switch
    {
        "factory.requirement.submitted.v1" => "intake",
        "factory.contract.proposed.v1" or "factory.contract.approved.v1" or "factory.contract.rejected.v1" => "contract",
        "factory.requirement.aligned.v1" => "requirements",
        "factory.architecture.aligned.v1" => "architecture",
        "factory.implementation.ready.v1" or "factory.implementation.requested.v1"
            or "factory.implementation.completed.v1" or "factory.rework.requested.v1" => "implementation",
        "factory.review.accepted.v1" or "factory.review.rejected.v1" => "review",
        "factory.merge.approved.v1" or "factory.delivery.requested.v1" => "delivery",
        "factory.release.candidate-ready.v1" or "factory.release.completed.v1" => "release",
        "factory.release.observed.v1" or "factory.operations.validated.v1" => "serving",
        _ => "stage"
    };

    private static string? SummaryForEvent(FactoryEventEnvelope envelope) => envelope.Type switch
    {
        "factory.requirement.submitted.v1" => "Requirement submitted for " + envelope.Subject,
        "factory.contract.proposed.v1" => "Contract proposal recorded at " + (envelope.Data.GetValueOrDefault("proposalDigest") ?? "unknown digest"),
        "factory.contract.approved.v1" => "Contract frozen at " + (envelope.Data.GetValueOrDefault("contractDigest") ?? "unknown digest"),
        "factory.contract.rejected.v1" => "Contract proposal rejected: " + (envelope.Data.GetValueOrDefault("reason") ?? "operator decision"),
        "factory.requirement.aligned.v1" => "Requirement aligned and contract bound",
        "factory.architecture.aligned.v1" => "Architecture refinement passed deterministic checks",
        "factory.implementation.ready.v1" => "Implementation planned and ready",
        "factory.implementation.requested.v1" => "Implementation attempt requested",
        "factory.implementation.completed.v1" => "Implementation produced " + (envelope.Data.GetValueOrDefault("changedFiles") ?? "?") + " changed file(s)",
        "factory.review.accepted.v1" => "Four mandatory review checks accepted at " + (envelope.Data.GetValueOrDefault("reviewedSha") ?? "unknown revision"),
        "factory.review.rejected.v1" or "factory.rework.requested.v1" => "Review rejected; rework requested: " + (envelope.Data.GetValueOrDefault("rejectReason") ?? "see evidence"),
        "factory.merge.approved.v1" => "Operator authorized merge for review " + (envelope.Data.GetValueOrDefault("approvalDigest") ?? "unknown digest"),
        "factory.delivery.requested.v1" => "Delivery validation started for the approved merge",
        "factory.release.candidate-ready.v1" => "Drift checks passed; release candidate reached",
        "factory.release.completed.v1" => "Merged revision verified at " + (envelope.Data.GetValueOrDefault("mergedSha") ?? "unknown sha"),
        "factory.release.observed.v1" => "Released revision observed serving and passing its contract",
        "factory.operations.validated.v1" => "Release operationally validated",
        "factory.stage.failed.v1" => "Stage failed: " + (envelope.Data.GetValueOrDefault("failureCategory")
            ?? envelope.Data.GetValueOrDefault("rejectReason")
            ?? envelope.Data.GetValueOrDefault("reason") ?? "unnamed category"),
        _ => null
    };

    // ------------------------------------------------------------------ helpers

    private static (long Sequence, FactoryEventEnvelope Event)? Last(
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        string type)
    {
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (string.Equals(events[index].Event.Type, type, StringComparison.Ordinal))
            {
                return events[index];
            }
        }

        return null;
    }

    private static (long Sequence, FactoryEventEnvelope Event)? First(
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        string type)
        => events.FirstOrDefault(pair => string.Equals(pair.Event.Type, type, StringComparison.Ordinal)) is var match
           && match.Event is not null
            ? match
            : null;

    private static string? Value((long Sequence, FactoryEventEnvelope Event)? match, string key)
        => match is not null && match.Value.Event.Data.TryGetValue(key, out var value) ? value : null;

    private static string? Value(FactoryEventEnvelope envelope, string key)
        => envelope.Data.TryGetValue(key, out var value) ? value : null;

    private static DateTimeOffset? Time((long Sequence, FactoryEventEnvelope Event)? match)
        => match?.Event.TimeUtc;
}
