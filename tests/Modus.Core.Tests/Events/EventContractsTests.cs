using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Events;

public sealed class EventContractsTests
{
    [Fact]
    public void PublisherContract_GivenCoreAssembly_ExpectedPublishMethodsSupportEnvelopeAndRawEvent()
    {
        var publisherType = typeof(IEventPublisher);

        Assert.NotNull(publisherType.GetMethod(nameof(IEventPublisher.Publish), [typeof(DomainEvent)]));
        Assert.NotNull(publisherType.GetMethod(nameof(IEventPublisher.Publish), [typeof(DomainEventEnvelope)]));
    }

    [Fact]
    public void SubscriberContract_GivenCoreAssembly_ExpectedSubscriberCanHandleEnvelope()
    {
        var subscriberType = typeof(IEventSubscriber);

        Assert.NotNull(subscriberType.GetMethod(nameof(IEventSubscriber.Subscribe), [typeof(IEventPublisher)]));
        Assert.NotNull(subscriberType.GetMethod(nameof(IEventSubscriber.OnEvent), [typeof(DomainEventEnvelope)]));
    }

    [Fact]
    public void EventEnvelope_GivenDomainEvent_ExpectedEnvelopeCarriesIdentitySourceAndHeaders()
    {
        var occurredAt = new DateTimeOffset(2026, 05, 16, 12, 30, 00, TimeSpan.Zero);
        var eventId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var envelope = new DomainEventEnvelope(
            Event: new DomainEvent("OrderCreated"),
            SourcePluginId: new PluginId("Plugin.Orders"),
            OccurredAtUtc: occurredAt,
            EventId: eventId,
            CorrelationId: new CorrelationId("corr-123"),
            Headers: new Dictionary<string, string> { ["tenant"] = "alpha" });

        Assert.Equal("OrderCreated", envelope.Event.Name);
        Assert.Equal(new PluginId("Plugin.Orders"), envelope.SourcePluginId);
        Assert.Equal(occurredAt, envelope.OccurredAtUtc);
        Assert.Equal(eventId, envelope.EventId);
        Assert.Equal(new CorrelationId("corr-123"), envelope.CorrelationId);
        Assert.Equal("alpha", envelope.Headers["tenant"]);
    }

    [Fact]
    public void EventContract_GivenPublishedEnvelope_ExpectedSubscriberReceivesEnvelopePayload()
    {
        var publisher = new InMemoryEventPublisher();
        var subscriber = new RecordingSubscriber();
        subscriber.Subscribe(publisher);

        publisher.Publish(new DomainEventEnvelope(
            Event: new DomainEvent("InventoryReserved"),
            SourcePluginId: new PluginId("Plugin.Inventory")));

        var received = Assert.Single(subscriber.ReceivedEnvelopes);
        Assert.Equal("InventoryReserved", received.Event.Name);
        Assert.Equal(new PluginId("Plugin.Inventory"), received.SourcePluginId);
        Assert.NotEqual(Guid.Empty, received.EventId);
    }

    [Fact]
    public void DomainEventEnvelope_SourcePluginId_PropertyType_IsPluginId()
    {
        Assert.Equal(typeof(PluginId), typeof(DomainEventEnvelope).GetProperty(nameof(DomainEventEnvelope.SourcePluginId))!.PropertyType);
    }

    [Fact]
    public void DomainEventEnvelope_CorrelationId_PropertyType_IsNullableCorrelationId()
    {
        Assert.Equal(typeof(CorrelationId?), typeof(DomainEventEnvelope).GetProperty(nameof(DomainEventEnvelope.CorrelationId))!.PropertyType);
    }

    private sealed class InMemoryEventPublisher : IEventPublisher
    {
        private readonly List<Action<DomainEventEnvelope>> _handlers = [];

        public void Publish(DomainEvent @event)
        {
            Publish(new DomainEventEnvelope(@event, SourcePluginId: new PluginId("host")));
        }

        public void Publish(DomainEventEnvelope envelope)
        {
            foreach (var handler in _handlers)
            {
                handler(envelope);
            }
        }

        public void Publish<TPayload>(DomainEvent<TPayload> @event)
        {
        }

        public void Subscribe(Action<DomainEventEnvelope> handler)
        {
            _handlers.Add(handler);
        }
    }

    private sealed class RecordingSubscriber : IEventSubscriber
    {
        public List<DomainEventEnvelope> ReceivedEnvelopes { get; } = [];

        public void Subscribe(IEventPublisher publisher)
        {
            if (publisher is InMemoryEventPublisher inMemory)
            {
                inMemory.Subscribe(OnEvent);
            }
        }

        public void OnEvent(DomainEventEnvelope envelope)
        {
            ReceivedEnvelopes.Add(envelope);
        }
    }
}
