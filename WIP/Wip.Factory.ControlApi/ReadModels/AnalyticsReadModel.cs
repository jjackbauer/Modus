using System.Globalization;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Work;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Services;
using Wip.Factory.Core;

using FactoryEventEnvelope = Wip.Factory.Contracts.Events.FactoryEventEnvelope;

namespace Wip.Factory.ControlApi.ReadModels;

public sealed partial class FactoryReadModel
{
    private static readonly MetricDefinitionDto[] AnalyticDefinitions =
    [
        new("work_items", "Work items", "The number of distinct durable work items in the selected window.", "count", "Created from the first requirement event timestamp."),
        new("released", "Released work items", "Work items whose ReleaseCompleted event is inside the window.", "count", null),
        new("lead_time", "Lead time (median)", "Elapsed time from requirement submission to ReleaseCompleted.", "seconds", "Only released items are included; no forecast is implied."),
        new("cycle_time", "Cycle time (median)", "Elapsed time from the first implementation request to ReleaseCompleted.", "seconds", "Excludes intake, contract approval and architecture waiting."),
        new("throughput", "Release throughput", "Released work items divided by the window length in days.", "items/day", null),
        new("rework_rate", "Rework rate", "Work items with at least one ReworkRequested event divided by work items that reached implementation.", "ratio", null),
        new("retry_amplification", "Retry amplification", "Implementation attempts beyond the first, divided by all implementation attempts.", "ratio", null),
        new("first_pass_review", "First-pass review acceptance", "Accepted reviews without a prior rework request, divided by all accepted reviews.", "ratio", null),
        new("contract_pass_rate", "Contract approval rate", "Frozen contracts divided by recorded contract proposals.", "ratio", null),
        new("stage_duration", "Stage duration (median)", "Median elapsed time between stage-entry and stage-exit events for completed stages.", "seconds", "Stage definitions are shown in the pipeline view."),
        new("failure_categories", "Failures by category", "StageFailed events grouped by recorded failure category.", "count", null),
        new("agent_sessions", "Agent sessions", "Durable agent session traces plus recorded implementation attempts.", "count", null),
        new("input_tokens", "Input tokens", "Provider-reported input tokens from durable session traces.", "tokens", "Not measured when no provider usage exists; never reported as zero."),
        new("output_tokens", "Output tokens", "Provider-reported output tokens from durable session traces.", "tokens", null),
        new("cost", "Model cost", "Provider billing amount attributable to factory work.", "currency", "Not measured: Wip.Factory has no billing feed and tokens do not have one price."),
        new("cost_per_outcome", "Cost per verified outcome", "Total model cost divided by independently verified engineering outcomes.", "currency", "Not measured: neither billing nor acceptance is available as a price denominator."),
        new("agent_utilization", "Agent utilization", "Busy agent time divided by available execution capacity.", "ratio", "Not measured: the factory does not publish a capacity/occupancy time series."),
        new("queue_aging", "Queue aging", "Open work items older than seven days.", "count", null)
    ];

    public async Task<AnalyticsDto> GetAnalyticsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddDays(-30);
        if (start >= end)
        {
            throw Infrastructure.ApiException.BadRequest("analytics-window-invalid", "The analytics window start must be before its end.");
        }

        var traces = await _service.Traces.RecentAsync(5000, cancellationToken).ConfigureAwait(false);
        var attemptsByItem = snapshot.Statuses.ToDictionary(
            static status => status.WorkItemId,
            status => LoadAttempts(status.WorkItemId),
            StringComparer.Ordinal);

        var created = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var released = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var firstImplementation = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var reworkItems = new HashSet<string>(StringComparer.Ordinal);
        var proposals = 0;
        var frozen = 0;
        var acceptedReviews = 0;
        var firstPassReviews = 0;
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var stageDurations = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var throughputByDay = new Dictionary<string, int>(StringComparer.Ordinal);
        var itemEvents = new Dictionary<string, List<(long Sequence, FactoryEventEnvelope Event)>>(StringComparer.Ordinal);

        foreach (var status in snapshot.Statuses)
        {
            var events = snapshot.For(status.WorkItemId);
            itemEvents[status.WorkItemId] = events.ToList();
            if (events.Count == 0)
            {
                continue;
            }

            created[status.WorkItemId] = events[0].Event.TimeUtc;
            foreach (var (sequence, envelope) in events)
            {
                switch (envelope.Type)
                {
                    case "factory.contract.proposed.v1":
                        proposals++;
                        break;
                    case "factory.contract.approved.v1":
                        frozen++;
                        break;
                    case "factory.release.completed.v1":
                        released[status.WorkItemId] = envelope.TimeUtc;
                        var day = envelope.TimeUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        throughputByDay[day] = throughputByDay.GetValueOrDefault(day) + 1;
                        break;
                    case "factory.implementation.requested.v1":
                        firstImplementation.TryAdd(status.WorkItemId, envelope.TimeUtc);
                        break;
                    case "factory.rework.requested.v1":
                        reworkItems.Add(status.WorkItemId);
                        break;
                    case "factory.review.accepted.v1":
                        acceptedReviews++;
                        if (!reworkItems.Contains(status.WorkItemId))
                        {
                            firstPassReviews++;
                        }

                        break;
                    case "factory.stage.failed.v1":
                        var category = envelope.Data.GetValueOrDefault("failureCategory")
                                       ?? envelope.Data.GetValueOrDefault("rejectReason")
                                       ?? envelope.Data.GetValueOrDefault("reason")
                                       ?? "unnamed";
                        category = category.Split(':', 2)[0];
                        failures[category] = failures.GetValueOrDefault(category) + 1;
                        break;
                }
            }

            AddStageDuration(stageDurations, "implementation", events, "factory.implementation.requested.v1", "factory.implementation.completed.v1");
            AddStageDuration(stageDurations, "review", events, "factory.implementation.completed.v1", "factory.review.accepted.v1");
            AddStageDuration(stageDurations, "release", events, "factory.release.candidate-ready.v1", "factory.release.completed.v1");
            AddStageDuration(stageDurations, "delivery", events, "factory.review.accepted.v1", "factory.release.candidate-ready.v1");
        }

        var createdInWindow = created.Values.Count(value => value >= start && value <= end);
        var releasedInWindow = released.Values.Count(value => value >= start && value <= end);
        var failedInWindow = snapshot.Statuses.Count(status => string.Equals(status.State, "Failed", StringComparison.Ordinal)
                                                              && created.TryGetValue(status.WorkItemId, out var at)
                                                              && at >= start && at <= end);
        var leadTimes = released
            .Where(pair => pair.Value >= start && pair.Value <= end && created.TryGetValue(pair.Key, out _))
            .Select(pair => (pair.Value - created[pair.Key]).TotalSeconds)
            .ToArray();
        var cycleTimes = released
            .Where(pair => pair.Value >= start && pair.Value <= end && firstImplementation.ContainsKey(pair.Key))
            .Select(pair => (pair.Value - firstImplementation[pair.Key]).TotalSeconds)
            .ToArray();

        var implementedItems = firstImplementation.Count;
        var implementations = attemptsByItem.Values.Sum(static attempts => attempts.Count);
        var retries = attemptsByItem.Values.Sum(attempts => Math.Max(0, attempts.Count - 1));
        var measuredTokens = traces.Where(trace => IsMeasuredModel(trace.ModelId.Value)).ToArray();
        var measured = measuredTokens.Length > 0;

        var metrics = new List<MetricDto>
        {
            Metric("work_items", "Work items", "count", createdInWindow, "First requirement event inside the window."),
            Metric("released", "Released work items", "count", releasedInWindow, "ReleaseCompleted event inside the window."),
            Metric("lead_time", "Lead time (median)", "seconds", Median(leadTimes), "Released items only; creation to release."),
            Metric("cycle_time", "Cycle time (median)", "seconds", Median(cycleTimes), "Released items with an implementation request."),
            Metric("throughput", "Release throughput", "items/day", releasedInWindow / Math.Max(1, (end - start).TotalDays), "Released items / window days."),
            Metric("rework_rate", "Rework rate", "ratio", implementedItems == 0 ? null : (double)reworkItems.Count / implementedItems, "Items with rework / items that reached implementation."),
            Metric("retry_amplification", "Retry amplification", "ratio", implementations == 0 ? null : (double)retries / implementations, "Retries beyond the first / all attempts."),
            Metric("first_pass_review", "First-pass review acceptance", "ratio", acceptedReviews == 0 ? null : (double)firstPassReviews / acceptedReviews, "Accepted reviews without prior rework."),
            Metric("contract_pass_rate", "Contract approval rate", "ratio", proposals == 0 ? null : (double)frozen / proposals, "Frozen contracts / proposals."),
            Metric("stage_duration", "Implementation stage (median)", "seconds", Median(stageDurations.GetValueOrDefault("implementation")?.ToArray() ?? []), "Requested to completed."),
            measured
                ? Metric("input_tokens", "Input tokens", "tokens", measuredTokens.Sum(static trace => trace.InputTokens), "Sum of measured provider input usage.")
                : NotMeasured("input_tokens", "Input tokens", "tokens", "No durable provider usage was recorded. Unmeasured is not zero."),
            measured
                ? Metric("output_tokens", "Output tokens", "tokens", measuredTokens.Sum(static trace => trace.OutputTokens), "Sum of measured provider output usage.")
                : NotMeasured("output_tokens", "Output tokens", "tokens", "No durable provider usage was recorded. Unmeasured is not zero."),
            NotMeasured("cost", "Model cost", "currency", "No billing feed is supplied by Wip.Factory."),
            NotMeasured("cost_per_outcome", "Cost per verified outcome", "currency", "Neither billing nor evaluator-owned acceptance is available as a cost denominator."),
            NotMeasured("agent_utilization", "Agent utilization", "ratio", "No execution-capacity time series exists in durable evidence."),
            Metric("queue_aging", "Queue aging", "count", snapshot.Statuses.Count(status => status.State is not ("Released" or "OperationallyValidated" or "Failed" or "Cancelled" or "Closed")
                && created.TryGetValue(status.WorkItemId, out var at) && DateTimeOffset.UtcNow - at > TimeSpan.FromDays(7)), "Open items older than seven days."),
            Metric("failures", "Recorded stage failures", "count", failures.Values.Sum(), "StageFailed events in the selected work history, not a success rate.")
        };

        var tokenSeries = measuredTokens
            .GroupBy(static trace => trace.ModelId.Value, StringComparer.Ordinal)
            .Select(group => new SeriesPointDto(group.Key, group.Sum(static trace => trace.InputTokens + trace.OutputTokens)))
            .OrderByDescending(static point => point.Value)
            .ToArray();

        var states = snapshot.Statuses
            .GroupBy(static status => status.State, StringComparer.Ordinal)
            .Select(group =>
            {
                var times = group
                    .Where(status => released.ContainsKey(status.WorkItemId) && created.ContainsKey(status.WorkItemId))
                    .Select(status => (released[status.WorkItemId] - created[status.WorkItemId]).TotalSeconds)
                    .ToArray();
                return new SeriesPointDto(group.Key, Median(times) ?? 0);
            })
            .ToArray();

        return new AnalyticsDto(
            new AnalyticsWindowDto(start.ToString("O"), end.ToString("O"), createdInWindow, releasedInWindow, failedInWindow),
            metrics,
            throughputByDay.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => new SeriesPointDto(pair.Key, pair.Value)).ToArray(),
            states,
            failures.OrderByDescending(static pair => pair.Value).Select(static pair => new SeriesPointDto(pair.Key, pair.Value)).ToArray(),
            tokenSeries,
            stageDurations
                .Select(pair => new SeriesPointDto(pair.Key, Median(pair.Value.ToArray()) ?? 0))
                .OrderBy(static point => point.Label, StringComparer.Ordinal)
                .ToArray(),
            AnalyticDefinitions,
            metrics.Where(static metric => metric.Status != "measured").Select(static metric => metric.Key).ToArray(),
            DateTimeOffset.UtcNow);
    }

    private static void AddStageDuration(
        Dictionary<string, List<double>> durations,
        string stage,
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events,
        string enterType,
        string exitType)
    {
        var enter = First(events, enterType);
        var exit = Last(events, exitType);
        if (enter is null || exit is null || exit.Value.Sequence <= enter.Value.Sequence)
        {
            return;
        }

        if (!durations.TryGetValue(stage, out var list))
        {
            durations[stage] = list = [];
        }

        list.Add((exit.Value.Event.TimeUtc - enter.Value.Event.TimeUtc).TotalSeconds);
    }

    private static double? Median(double[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(static value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    private static MetricDto Metric(string key, string label, string unit, double? value, string basis)
        => new(key, label, unit, value is null ? "not-measured" : "measured", value, basis, null);

    private static MetricDto NotMeasured(string key, string label, string unit, string basis)
        => new(key, label, unit, "not-measured", null, basis, "Not measured");

    // ------------------------------------------------------------------ pipeline

    public async Task<PipelineDto> GetPipelineAsync(DateTimeOffset? at, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var generated = DateTimeOffset.UtcNow;
        var items = new List<PipelineItemDto>();
        var eventsByItem = snapshot.Statuses.ToDictionary(
            static status => status.WorkItemId,
            status => snapshot.For(status.WorkItemId).ToList(),
            StringComparer.Ordinal);

        foreach (var status in snapshot.Statuses)
        {
            var events = eventsByItem[status.WorkItemId];
            DateTimeOffset? cutoff = at;
            if (cutoff is not null && events.Count > 0 && events[0].Event.TimeUtc > cutoff)
            {
                continue;
            }

            var relevant = cutoff is null
                ? events
                : events.Where(pair => pair.Event.TimeUtc <= cutoff.Value).ToList();
            if (relevant.Count == 0)
            {
                continue;
            }

            var state = cutoff is null ? status.State : InferStateAt(relevant);
            var last = relevant[^1];
            var stage = StageForState(state, relevant);
            var title = relevant
                .Select(pair => pair.Event)
                .FirstOrDefault(envelope => envelope.Type == "factory.requirement.submitted.v1")
                ?.Data.GetValueOrDefault("title") ?? status.WorkItemId;

            items.Add(new PipelineItemDto(
                status.WorkItemId,
                title,
                state,
                stage,
                StageLabels[stage],
                last.Event.TimeUtc.ToString("O"),
                (long)Math.Max(0, ((cutoff ?? generated) - last.Event.TimeUtc).TotalSeconds),
                status.LastFailure,
                status.State is "Failed" or "Blocked" or "NeedsRework" || status.LastFailure is not null));
        }

        var stages = new List<PipelineStageDto>();
        foreach (var stage in StageOrder)
        {
            var inStage = items.Where(item => string.Equals(item.Stage, stage, StringComparison.Ordinal)).ToArray();
            var stageDurations = new List<double>();
            var stageWaiting = new List<double>();
            foreach (var item in inStage)
            {
                var events = eventsByItem[item.WorkItemId];
                var enter = EnterEventForStage(stage, events);
                var exit = ExitEventForStage(stage, events);
                if (enter is not null && exit is not null && exit.Value.Event.TimeUtc > enter.Value.Event.TimeUtc)
                {
                    stageDurations.Add((exit.Value.Event.TimeUtc - enter.Value.Event.TimeUtc).TotalSeconds);
                }
                else if (enter is not null)
                {
                    stageWaiting.Add((generated - enter.Value.Event.TimeUtc).TotalSeconds);
                }
            }

            stages.Add(new PipelineStageDto(
                stage,
                StageLabels[stage],
                QueueDepth: inStage.Count(static item => item.State is "Proposed" or "RequirementsAligned" or "ArchitectureRefined" or "ReadyForImplementation"),
                InFlight: inStage.Count(static item => item.State is "InImplementation" or "InReview" or "DeliveryValidation" or "ReleaseCandidate"),
                WaitingForAuthorization: inStage.Count(static item => (item.State is "Proposed" or "ReadyForDelivery") && item.NeedsAttention),
                Failed: inStage.Count(static item => item.State is "Failed" or "Blocked"),
                AverageProcessingSeconds: Average(stageDurations),
                AverageWaitingSeconds: Average(stageWaiting),
                CompletedInWindow: inStage.Count(static item => item.State is not ("Proposed" or "RequirementsAligned" or "ArchitectureRefined" or "ReadyForImplementation" or "InImplementation" or "InReview" or "DeliveryValidation" or "ReleaseCandidate" or "Blocked" or "Failed")),
                WorkItemIds: inStage.Select(static item => item.WorkItemId).ToArray()));
        }

        return new PipelineDto(
            at?.ToString("O"),
            at is not null,
            generated,
            stages,
            items,
            [.. StageOrder.Zip(StageOrder.Skip(1), static (left, right) => new GraphEdgeDto(left, right))],
            at is null ? null : "Historical playback reconstructs each item's stage and state from durable events up to the selected instant. It does not query a historical database.");
    }

    private static string StageForState(string state, IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events)
        => state switch
        {
            "Proposed" => "contract",
            "RequirementsAligned" => "requirements",
            "ArchitectureRefined" => "architecture",
            "ReadyForImplementation" or "InImplementation" or "NeedsRework" => "implementation",
            "InReview" => "review",
            "ReadyForDelivery" or "DeliveryValidation" => "delivery",
            "ReleaseCandidate" => "release",
            "Released" or "OperationallyValidated" => "serving",
            "Failed" => LastFailureStage(events),
            _ => "contract"
        };

    private static string LastFailureStage(IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events)
    {
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (events[index].Event.Type == "factory.stage.failed.v1")
            {
                return events.Take(index).Any(pair => pair.Event.Type == "factory.implementation.completed.v1")
                    ? "review"
                    : events.Take(index).Any(pair => pair.Event.Type == "factory.contract.approved.v1") ? "implementation" : "contract";
            }
        }

        return "implementation";
    }

    private static string InferStateAt(IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events)
    {
        var state = "Proposed";
        foreach (var (_, envelope) in events)
        {
            state = envelope.Type switch
            {
                "factory.requirement.submitted.v1" => "Proposed",
                "factory.requirement.aligned.v1" => "RequirementsAligned",
                "factory.architecture.aligned.v1" => "ArchitectureRefined",
                "factory.implementation.ready.v1" => "ReadyForImplementation",
                "factory.implementation.requested.v1" => "InImplementation",
                "factory.implementation.completed.v1" => "InReview",
                "factory.review.accepted.v1" => "ReadyForDelivery",
                "factory.delivery.requested.v1" => "DeliveryValidation",
                "factory.release.candidate-ready.v1" => "ReleaseCandidate",
                "factory.release.completed.v1" => "Released",
                "factory.release.observed.v1" or "factory.operations.validated.v1" => "OperationallyValidated",
                "factory.rework.requested.v1" => "NeedsRework",
                "factory.stage.failed.v1" => "Failed",
                _ => state
            };
        }

        return state;
    }

    private static (long Sequence, FactoryEventEnvelope Event)? EnterEventForStage(
        string stage,
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events)
        => stage switch
        {
            "intake" => First(events, "factory.requirement.submitted.v1"),
            "contract" => First(events, "factory.contract.proposed.v1"),
            "requirements" => First(events, "factory.contract.approved.v1"),
            "architecture" => First(events, "factory.requirement.aligned.v1"),
            "implementation" => First(events, "factory.implementation.requested.v1") ?? First(events, "factory.implementation.ready.v1"),
            "review" => First(events, "factory.implementation.completed.v1"),
            "delivery" => First(events, "factory.review.accepted.v1"),
            "release" => First(events, "factory.release.candidate-ready.v1"),
            "serving" => First(events, "factory.release.completed.v1"),
            _ => null
        };

    private static (long Sequence, FactoryEventEnvelope Event)? ExitEventForStage(
        string stage,
        IReadOnlyList<(long Sequence, FactoryEventEnvelope Event)> events)
        => stage switch
        {
            "intake" => First(events, "factory.contract.proposed.v1"),
            "contract" => First(events, "factory.contract.approved.v1"),
            "requirements" => First(events, "factory.requirement.aligned.v1"),
            "architecture" => First(events, "factory.architecture.aligned.v1"),
            "implementation" => First(events, "factory.implementation.completed.v1"),
            "review" => First(events, "factory.review.accepted.v1"),
            "delivery" => First(events, "factory.release.candidate-ready.v1"),
            "release" => First(events, "factory.release.completed.v1"),
            "serving" => First(events, "factory.operations.validated.v1"),
            _ => null
        };

    private static double? Average(IReadOnlyList<double> values)
        => values.Count == 0 ? null : values.Average();
}
