using Modus.Core.Events;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Events;

public sealed class EventPublisherSubscriberGenericTests
{
    // ── IEventPublisher — Generic Overload Contract ────────────────────────

    [Fact]
    public void PublisherContract_GivenCoreAssembly_ExpectedGenericPublishOverloadsExist()
    {
        var publisherType = typeof(IEventPublisher);
        var methods = publisherType.GetMethods();

        var hasTypedEventOverload = methods.Any(m =>
            m.Name == nameof(IEventPublisher.Publish) &&
            m.IsGenericMethodDefinition &&
            m.GetGenericArguments().Length == 1 &&
            m.GetParameters() is { Length: 1 } ps &&
            ps[0].ParameterType.IsGenericType &&
            ps[0].ParameterType.GetGenericTypeDefinition() == typeof(DomainEvent<>));

        var hasTypedEnvelopeOverload = methods.Any(m =>
            m.Name == nameof(IEventPublisher.Publish) &&
            m.IsGenericMethodDefinition &&
            m.GetGenericArguments().Length == 1 &&
            m.GetParameters() is { Length: 1 } ps &&
            ps[0].ParameterType.IsGenericType &&
            ps[0].ParameterType.GetGenericTypeDefinition() == typeof(DomainEventEnvelope<>));

        Assert.True(hasTypedEventOverload,
            "IEventPublisher must declare Publish<TPayload>(DomainEvent<TPayload>).");
        Assert.True(hasTypedEnvelopeOverload,
            "IEventPublisher must declare Publish<TPayload>(DomainEventEnvelope<TPayload>).");
    }

    [Fact]
    public void Publish_GivenTypedEnvelope_ExpectedDefaultImplementationDelegatesToTypedEventOverload()
    {
        var publisher = new RecordingPublisher();
        var ev = new DomainEvent<string>("OrderCreated", "payload-value");
        var envelope = new DomainEventEnvelope<string>(ev, new PluginId("Plugin.Orders"));

        ((IEventPublisher)publisher).Publish(envelope);

        var recorded = Assert.Single(publisher.PublishedTypedEvents);
        Assert.Same(ev, recorded);
    }

    // ── IEventSubscriber — Generic OnEvent Contract ────────────────────────

    [Fact]
    public void SubscriberContract_GivenCoreAssembly_ExpectedGenericOnEventMethodExists()
    {
        var subscriberType = typeof(IEventSubscriber);
        var methods = subscriberType.GetMethods();

        var hasGenericOnEvent = methods.Any(m =>
            m.Name == nameof(IEventSubscriber.OnEvent) &&
            m.IsGenericMethodDefinition &&
            m.GetGenericArguments().Length == 1 &&
            m.GetParameters() is { Length: 1 } ps &&
            ps[0].ParameterType.IsGenericType &&
            ps[0].ParameterType.GetGenericTypeDefinition() == typeof(DomainEventEnvelope<>));

        Assert.True(hasGenericOnEvent,
            "IEventSubscriber must declare OnEvent<TPayload>(DomainEventEnvelope<TPayload>).");
    }

    [Fact]
    public void OnEvent_GivenTypedEnvelopeAndDefaultImpl_ExpectedNoExceptionThrown()
    {
        IEventSubscriber subscriber = new MinimalSubscriber();
        var envelope = new DomainEventEnvelope<string>(
            new DomainEvent<string>("OrderCreated", "payload"),
            new PluginId("Plugin.Orders"));

        var exception = Record.Exception(() => subscriber.OnEvent(envelope));

        Assert.Null(exception);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> PublishedTypedEvents { get; } = [];

        public void Publish(DomainEvent @event) { }

        public void Publish<TPayload>(DomainEvent<TPayload> @event)
        {
            PublishedTypedEvents.Add(@event);
        }
    }

    private sealed class MinimalSubscriber : IEventSubscriber
    {
        public void Subscribe(IEventPublisher publisher) { }
    }
}
