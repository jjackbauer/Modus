using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Telemetry;

namespace Wip.Factory.Telemetry;

/// <summary>
/// Append-only outcome attribution (F-I.3, F-I.6). Linking an observation never rewrites an earlier
/// link, and every link must state its attribution uncertainty.
/// </summary>
public sealed class LocalOutcomeAttributionStore : IOutcomeAttributionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<DownstreamOutcome> _outcomes = [];
    private bool _loaded;

    public LocalOutcomeAttributionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "downstream-outcomes.jsonl");
    }

    public async ValueTask LinkAsync(DownstreamOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (string.IsNullOrWhiteSpace(outcome.AttributionUncertainty))
        {
            throw new ArgumentException(
                "An attribution link must state its uncertainty; an implicit certainty claim is not allowed.",
                nameof(outcome));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();

            if (_outcomes.Any(existing => string.Equals(existing.OutcomeId.Value, outcome.OutcomeId.Value, StringComparison.Ordinal)))
            {
                // An existing observation is immutable: linking the same id again is a no-op, not a rewrite.
                return;
            }

            var line = JsonSerializer.Serialize(outcome, SerializerOptions) + "\n";
            await File.AppendAllTextAsync(_path, line, cancellationToken).ConfigureAwait(false);
            _outcomes.Add(outcome);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<DownstreamOutcome>> LinksForAsync(
        WorkItemId workItemId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _outcomes.Where(outcome => outcome.WorkItemId.Equals(workItemId)).ToArray();
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

            try
            {
                var outcome = JsonSerializer.Deserialize<DownstreamOutcome>(line, SerializerOptions);
                if (outcome is not null)
                {
                    _outcomes.Add(outcome);
                }
            }
            catch (JsonException)
            {
            }
        }
    }
}
