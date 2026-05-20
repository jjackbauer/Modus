namespace Modus.Core.Events;

using Modus.Core.Messaging;
using Modus.Core.Plugins;

public sealed record DomainEventEnvelope
{
    public DomainEventEnvelope(
        DomainEvent Event,
        PluginId SourcePluginId,
        DateTimeOffset? OccurredAtUtc = null,
        Guid? EventId = null,
        CorrelationId? CorrelationId = null,
        IReadOnlyDictionary<string, string>? Headers = null)
    {
        this.Event = Event;
        this.SourcePluginId = SourcePluginId;
        this.OccurredAtUtc = OccurredAtUtc ?? DateTimeOffset.UtcNow;
        this.EventId = EventId ?? Guid.NewGuid();
        this.CorrelationId = CorrelationId;
        this.Headers = Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Headers, StringComparer.Ordinal);
    }

    public DomainEvent Event { get; }

    public PluginId SourcePluginId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public Guid EventId { get; }

    public CorrelationId? CorrelationId { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }
}