namespace Modus.Core.Events;

public interface IEventSubscriber
{
    void Subscribe(IEventPublisher publisher);

    void OnEvent(DomainEventEnvelope envelope)
    {
    }
}
