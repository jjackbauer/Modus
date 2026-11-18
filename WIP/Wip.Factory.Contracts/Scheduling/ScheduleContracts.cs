using Wip.Factory.Contracts.Identifiers;

namespace Wip.Factory.Contracts.Scheduling;

/// <summary>
/// A durable record that one occurrence of a schedule became due. The occurrence identity is derived
/// from the schedule and its due instant, so replaying the same due instant can never create a second
/// occurrence (F-N.3).
/// </summary>
public sealed record ScheduleOccurrence(
    ScheduleId ScheduleId,
    DateTimeOffset DueAtUtc,
    FactoryEventId EventId,
    DateTimeOffset RecordedAtUtc);

/// <summary>Durable occurrence identity for scheduler-driven work.</summary>
public interface IScheduleOccurrenceStore
{
    /// <summary>
    /// Claims the occurrence for <paramref name="dueAtUtc"/>. Returns the existing occurrence when this
    /// due instant was already claimed, so a restart replays exactly one occurrence rather than zero or two.
    /// </summary>
    ValueTask<(bool IsNew, ScheduleOccurrence Occurrence)> ClaimAsync(
        ScheduleId scheduleId,
        DateTimeOffset dueAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ScheduleOccurrence>> ReadAllAsync(CancellationToken cancellationToken = default);
}
