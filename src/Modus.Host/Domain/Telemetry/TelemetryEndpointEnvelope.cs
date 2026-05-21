using Modus.Core.Plugins;

namespace Modus.Host.Domain.Telemetry;

internal sealed record TelemetryMeasurementList(
    int Count,
    IReadOnlyList<TelemetryMeasurement> Items);

internal sealed record TelemetryEndpointEnvelope(
    string PluginId,
    string Operation,
    string Source,
    string Category,
    DateTimeOffset CollectedAt,
    TelemetryMeasurementList Measurements,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static TelemetryEndpointEnvelope FromResult(TelemetryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new TelemetryEndpointEnvelope(
            PluginId: result.PluginId,
            Operation: result.Operation,
            Source: result.Source,
            Category: result.Category,
            CollectedAt: result.CollectedAtUtc,
            Measurements: new TelemetryMeasurementList(
                Count: result.Measurements.Count,
                Items: result.Measurements),
            Metadata: result.Metadata);
    }
}