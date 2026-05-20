using System.Reflection;
using Modus.Core.Events;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Events;

public sealed class BackwardCompatibilityTests
{
    [Fact]
    public void DomainEvent_GivenExistingNonGenericApi_ExpectedNameOnlyRecordPreserved()
    {
        var type = typeof(DomainEvent);

        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetProperty(nameof(DomainEvent.Name)));
        Assert.Null(type.GetProperty("Payload"));

        var evt = new DomainEvent("order.placed");
        Assert.Equal("order.placed", evt.Name);
    }

    [Fact]
    public void DomainEventEnvelope_GivenExistingNonGenericApi_ExpectedEnvelopeUnchanged()
    {
        var type = typeof(DomainEventEnvelope);

        Assert.True(type.IsSealed);
        var eventProp = type.GetProperty(nameof(DomainEventEnvelope.Event));
        Assert.NotNull(eventProp);
        Assert.Equal(typeof(DomainEvent), eventProp!.PropertyType);

        var domainEvent = new DomainEvent("order.shipped");
        var pluginId = new PluginId("shipping-plugin");
        var envelope = new DomainEventEnvelope(domainEvent, pluginId);

        Assert.Equal(domainEvent, envelope.Event);
        Assert.Equal(pluginId, envelope.SourcePluginId);
        Assert.NotEqual(Guid.Empty, envelope.EventId);
    }

    [Fact]
    public void IEventPublisher_GivenExistingNonGenericOverloads_ExpectedBothPreserved()
    {
        var publisherType = typeof(IEventPublisher);

        var publishEvent = publisherType.GetMethod(nameof(IEventPublisher.Publish), [typeof(DomainEvent)]);
        var publishEnvelope = publisherType.GetMethod(nameof(IEventPublisher.Publish), [typeof(DomainEventEnvelope)]);

        Assert.NotNull(publishEvent);
        Assert.NotNull(publishEnvelope);
    }
}
