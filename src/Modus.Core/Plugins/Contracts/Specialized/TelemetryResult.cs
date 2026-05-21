namespace Modus.Core.Plugins;

public sealed record TelemetryMeasurement(
    string Name,
    double Value,
    string Unit,
    string Kind);

public sealed record TelemetryResult(
    string PluginId,
    string Operation,
    string Source,
    string Category,
    DateTimeOffset CollectedAtUtc,
    IReadOnlyList<TelemetryMeasurement> Measurements,
    IReadOnlyDictionary<string, string> Metadata);