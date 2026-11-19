using System.Text;
using System.Text.Json;
using Wip.Factory.ControlApi;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;

namespace Wip.Factory.ControlApi.Services;

/// <summary>
/// Control-plane planning metadata. This is deliberately stored outside the factory's journal and is marked
/// non-authoritative on every wire response: it never becomes scheduling or execution authority.
/// </summary>
public sealed class PlanningStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, StoredPlanning>? _cache;

    public PlanningStore(string adapterDataDirectory)
    {
        Directory.CreateDirectory(adapterDataDirectory);
        _path = Path.Combine(adapterDataDirectory, "planning.json");
    }

    public async Task<PlanningDto> GetAsync(string workItemId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return records.TryGetValue(workItemId, out var record)
                ? ToDto(record)
                : Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlanningDto> SetAsync(
        string workItemId,
        PlanningInputDto input,
        string actor,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var record = new StoredPlanning(
                Normalize(input.Priority),
                Normalize(input.DeadlineUtc),
                Clean(input.Tags, 20, 64),
                Clean(input.Links, 20, 512),
                Bound(Normalize(input.Notes), 4000),
                actor,
                DateTimeOffset.UtcNow.ToString("O"));
            records[workItemId] = record;
            await SaveAsync(records, cancellationToken).ConfigureAwait(false);
            return ToDto(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static readonly PlanningDto Empty = new(
        null, null, [], [], null, null, null, Authoritative: false);

    private static PlanningDto ToDto(StoredPlanning record) => new(
        record.Priority,
        record.DeadlineUtc,
        record.Tags,
        record.Links,
        record.Notes,
        record.UpdatedBy,
        record.UpdatedAtUtc,
        Authoritative: false);

    private async Task<Dictionary<string, StoredPlanning>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_path))
        {
            return _cache = new Dictionary<string, StoredPlanning>(StringComparer.Ordinal);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            _cache = JsonSerializer.Deserialize<Dictionary<string, StoredPlanning>>(json, ApiJson.Options)
                     ?? new Dictionary<string, StoredPlanning>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            _cache = new Dictionary<string, StoredPlanning>(StringComparer.Ordinal);
        }

        return _cache;
    }

    private async Task SaveAsync(Dictionary<string, StoredPlanning> records, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(records, ApiJson.Indented),
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Bound(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values, int maxItems, int maxLength)
        => values is null
            ? []
            : values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Select(value => value.Length <= maxLength ? value : value[..maxLength])
                .Distinct(StringComparer.Ordinal)
                .Take(maxItems)
                .ToArray();

    private sealed record StoredPlanning(
        string? Priority,
        string? DeadlineUtc,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Links,
        string? Notes,
        string UpdatedBy,
        string UpdatedAtUtc);
}
