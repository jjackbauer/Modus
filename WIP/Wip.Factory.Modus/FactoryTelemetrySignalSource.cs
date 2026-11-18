using Wip.Factory.Contracts.Events;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Triggers;

namespace Wip.Factory.Modus;

/// <summary>
/// Turns an operational observation into an operations-role trigger only when the signal actually
/// breaches its threshold (F-G.9). A signal inside its band produces no work, so the role is
/// event-driven rather than polling.
/// </summary>
public sealed class FactoryTelemetrySignalSource
{
    private readonly ISettledSignalTracker _tracker;

    public FactoryTelemetrySignalSource(ISettledSignalTracker? tracker = null)
        => _tracker = tracker ?? new InMemorySettledSignalTracker();

    public FactoryEventEnvelope? Emit(TelemetrySignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (!signal.BreachesThreshold)
        {
            return null;
        }

        // The same breached observation must not open a second diagnosis of the same incident.
        if (!_tracker.TryRegister(signal.SignalName, signal.WorkItemId.Value))
        {
            return null;
        }

        return new FactoryEventEnvelope(
            new FactoryEventId("evt-" + Guid.NewGuid().ToString("N")),
            FactoryEventTypes.OperationsAnomalyDetected,
            "/factory/telemetry",
            $"work/{signal.WorkItemId.Value}",
            DateTimeOffset.UtcNow,
            new CorrelationId("telemetry:" + signal.SignalName),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workItemId"] = signal.WorkItemId.Value,
                ["trigger"] = "telemetry",
                ["signal"] = signal.SignalName,
                ["observed"] = signal.ObservedValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["threshold"] = signal.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }
}

/// <summary>Remembers which (signal, work item) pairs already produced a diagnosis.</summary>
public interface ISettledSignalTracker
{
    bool TryRegister(string signalName, string workItemId);
}

public sealed class InMemorySettledSignalTracker : ISettledSignalTracker
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public bool TryRegister(string signalName, string workItemId)
        => _seen.Add(signalName + "\u001f" + workItemId);
}
