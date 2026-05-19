namespace Modus.Core.Events;

public sealed record DomainEventEnvelope
{
    public DomainEventEnvelope(
        DomainEvent Event,
        string SourcePluginId,
        DateTimeOffset? OccurredAtUtc = null,
        Guid? EventId = null,
        string? CorrelationId = null,
        IReadOnlyDictionary<string, string>? Headers = null)
    {
        if (string.IsNullOrWhiteSpace(SourcePluginId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(SourcePluginId));
        }

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

    public string SourcePluginId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public Guid EventId { get; }

    public string? CorrelationId { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }
}