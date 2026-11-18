using Modus.Core.Plugins;
using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Scheduling;

namespace Wip.Factory.Modus;

/// <summary>
/// Emits scheduled factory triggers through Modus' <see cref="IPluginScheduler"/> while the factory
/// owns occurrence identity (F-N.3). Modus provides the cadence; the factory persists which due
/// instants were already turned into work, so a host restart replays exactly one occurrence.
/// </summary>
public sealed class FactoryPluginScheduleSource
{
    private readonly IPluginScheduler _scheduler;
    private readonly IScheduleOccurrenceStore _occurrences;

    public FactoryPluginScheduleSource(IPluginScheduler scheduler, IScheduleOccurrenceStore occurrences)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _occurrences = occurrences ?? throw new ArgumentNullException(nameof(occurrences));
    }

    /// <summary>Declares the cadence on the Modus scheduler.</summary>
    public void Register(ScheduleId scheduleId, JobName jobName, TimeSpan interval, OperationName operation)
    {
        ArgumentNullException.ThrowIfNull(interval);
        _scheduler.ScheduleRecurring(jobName, interval, operation);
    }

    /// <summary>
    /// Converts one due instant into exactly one committed fact. Returns null when that due instant was
    /// already claimed, which is what makes a restart idempotent.
    /// </summary>
    public async ValueTask<FactoryEventEnvelope?> EmitDueAsync(
        ScheduleId scheduleId,
        DateTimeOffset dueAtUtc,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        var (isNew, occurrence) = await _occurrences
            .ClaimAsync(scheduleId, dueAtUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!isNew)
        {
            return null;
        }

        return new FactoryEventEnvelope(
            occurrence.EventId,
            FactoryEventTypes.WorkReady,
            "/factory/scheduler",
            $"work/{workItemId}",
            occurrence.RecordedAtUtc,
            new CorrelationId("schedule:" + scheduleId.Value),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workItemId"] = workItemId,
                ["trigger"] = "schedule",
                ["scheduleId"] = scheduleId.Value,
                ["dueAtUtc"] = dueAtUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            });
    }
}
