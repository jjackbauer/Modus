using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Scheduling;

namespace Wip.Factory.Persistence;

/// <summary>
/// Durable occurrence identity for scheduler-driven work (F-N.3). One append-only file records each
/// claimed due instant, so a restarted host replays exactly one occurrence for a missed due time —
/// neither zero (lost work) nor two (duplicated work).
/// </summary>
public sealed class LocalScheduleOccurrenceStore : IScheduleOccurrenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ScheduleOccurrence> _occurrences = new(StringComparer.Ordinal);
    private bool _loaded;

    public LocalScheduleOccurrenceStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "schedule-occurrences.jsonl");
    }

    public async ValueTask<(bool IsNew, ScheduleOccurrence Occurrence)> ClaimAsync(
        ScheduleId scheduleId,
        DateTimeOffset dueAtUtc,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();

            var key = Key(scheduleId, dueAtUtc);
            if (_occurrences.TryGetValue(key, out var existing))
            {
                return (false, existing);
            }

            var occurrence = new ScheduleOccurrence(
                scheduleId,
                dueAtUtc,
                new FactoryEventId("evt-" + Guid.NewGuid().ToString("N")),
                DateTimeOffset.UtcNow);

            var line = JsonSerializer.Serialize(occurrence, SerializerOptions) + "\n";
            await File.AppendAllTextAsync(_path, line, cancellationToken).ConfigureAwait(false);

            _occurrences[key] = occurrence;
            return (true, occurrence);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ScheduleOccurrence>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _occurrences.Values.OrderBy(static occurrence => occurrence.DueAtUtc).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ScheduleOccurrence? occurrence;
            try
            {
                occurrence = JsonSerializer.Deserialize<ScheduleOccurrence>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                // A torn trailing record was never a committed occurrence.
                continue;
            }

            if (occurrence is not null)
            {
                _occurrences[Key(occurrence.ScheduleId, occurrence.DueAtUtc)] = occurrence;
            }
        }
    }

    private static string Key(ScheduleId scheduleId, DateTimeOffset dueAtUtc)
        => scheduleId.Value + "\u001f" + dueAtUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
