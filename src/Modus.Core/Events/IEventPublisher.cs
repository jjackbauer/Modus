namespace Modus.Core.Events;

public interface IEventPublisher
{
    void Publish(DomainEvent @event);

    void Publish(DomainEventEnvelope envelope)
    {
        Publish(envelope.Event);
    }

    void Publish<TPayload>(DomainEvent<TPayload> @event);

    void Publish<TPayload>(DomainEventEnvelope<TPayload> envelope)
    {
        Publish(envelope.Event);
    }
}
