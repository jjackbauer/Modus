namespace Modus.Core.Events;

public interface IEventPublisher
{
    void Publish(DomainEvent @event);

    void Publish(DomainEventEnvelope envelope)
    {
        Publish(envelope.Event);
    }
}
