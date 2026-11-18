using System.Text.Json;
using System.Text.Json.Serialization;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Telemetry;
using Wip.Runtime.Runtime;

namespace Wip.Factory.Telemetry;

/// <summary>
/// File-backed session trace store. Every trace is sanitized with WIP's own redactor before it is
/// written, so a secret cannot be recovered from the evidence store (F-I.5).
/// </summary>
public sealed class LocalAgentSessionTraceStore : IAgentSessionTraceStore, IAgentExperienceIndex
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly string _path;
    private readonly SecretRedactor _redactor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<AgentSessionTrace> _traces = [];
    private bool _loaded;

    public LocalAgentSessionTraceStore(string directory, SecretRedactor? redactor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "session-traces.jsonl");
        _redactor = redactor ?? SecretRedactor.Default;
    }

    public async ValueTask AppendAsync(AgentSessionTrace trace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();

            var sanitized = trace with
            {
                Summary = Sanitize(trace.Summary),
                FailureCategory = trace.FailureCategory is null ? null : Sanitize(trace.FailureCategory)
            };

            var line = JsonSerializer.Serialize(sanitized, SerializerOptions) + "\n";
            await File.AppendAllTextAsync(_path, line, cancellationToken).ConfigureAwait(false);
            _traces.Add(sanitized);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<AgentSessionTrace>> QueryAsync(
        WorkItemId workItemId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _traces.Where(trace => trace.WorkItemId.Equals(workItemId)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<FailureCluster>> FailureClustersAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _traces
                .Where(static trace => trace.FailureCategory is not null)
                .GroupBy(static trace => trace.FailureCategory!, StringComparer.Ordinal)
                .Select(group => new FailureCluster(
                    group.Key,
                    group.Count(),
                    group.Select(static trace => trace.SessionId).Distinct().ToArray()))
                .OrderByDescending(static cluster => cluster.Occurrences)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<AgentSessionTrace>> RecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _traces
                .OrderByDescending(static trace => trace.RecordedAtUtc)
                .Take(Math.Max(count, 0))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Sanitize(string value)
    {
        var result = _redactor.Redact(value);
        return result.Value;
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
                var trace = JsonSerializer.Deserialize<AgentSessionTrace>(line, SerializerOptions);
                if (trace is not null)
                {
                    _traces.Add(trace);
                }
            }
            catch (JsonException)
            {
                // A torn trailing record was never a committed trace.
            }
        }
    }
}
