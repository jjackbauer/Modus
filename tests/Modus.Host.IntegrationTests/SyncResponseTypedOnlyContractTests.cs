using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.WebApi;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class SyncResponseTypedOnlyContractTests
{
    [Fact]
    public void SyncResponseContract_GivenTypedPayloadObject_ExpectedPayloadIsReturnedAsTypedObject()
    {
        var expectedPayload = new DispatchProbePayload(Owner: "Plugin.Typed", Value: 42);
        var dispatcher = new PluginOperationSyncResponderDispatcher(
        [
            new DelegateResponder(_ => new SyncResponse(
                Success: true,
                Payload: expectedPayload,
                Status: SyncResponseStatus.Success))
        ]);

        var response = dispatcher.Handle(SyncRequest.ForStandardPath(new OperationName("typed-op")));

        var payload = Assert.IsType<DispatchProbePayload>(response.Payload);
        Assert.Equal("Plugin.Typed", payload.Owner);
        Assert.Equal(42, payload.Value);
        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
    }

    [Fact]
    public void SyncResponseContract_GivenNoResponderMatch_ExpectedTypedFailurePayloadAndRejectedStatus()
    {
        var correlationId = new CorrelationId("corr-typed-only");
        var dispatcher = new PluginOperationSyncResponderDispatcher([]);

        var response = dispatcher.Handle(
            SyncRequest.ForStandardPath(new OperationName("missing-op"), correlationId));

        var payload = Assert.IsType<SyncErrorPayload>(response.Payload);
        Assert.Equal("operation-not-found", payload.Code);
        Assert.Contains("missing-op", payload.Message, StringComparison.Ordinal);
        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.Equal(correlationId, response.CorrelationId);
    }

    [Fact]
    public void SyncResponseContract_GivenMessagingAssembly_ExpectedOnlyGenericSyncResponseTypeAndNoStringPayloadCtor()
    {
        var messagingAssembly = typeof(SyncRequest).Assembly;
        var legacySyncResponseType = messagingAssembly.GetType("Modus.Core.Messaging.SyncResponse", throwOnError: false, ignoreCase: false);

        Assert.Null(legacySyncResponseType);

        var genericSyncResponseType = messagingAssembly.GetType("Modus.Core.Messaging.SyncResponse`1", throwOnError: true, ignoreCase: false)!;
        var closedCanonicalResponseType = genericSyncResponseType.MakeGenericType(typeof(object));
        var constructors = closedCanonicalResponseType.GetConstructors();

        Assert.DoesNotContain(
            constructors,
            constructor => constructor.GetParameters().Skip(1).Any(parameter => parameter.ParameterType == typeof(string)));
    }

    [Fact]
    public void SyncResponseContract_GivenMessagingAssembly_ExpectedOnlyGenericSyncResponderContract()
    {
        var messagingAssembly = typeof(SyncRequest).Assembly;
        var legacySyncResponderType = messagingAssembly.GetType("Modus.Core.Messaging.ISyncResponder", throwOnError: false, ignoreCase: false);

        Assert.Null(legacySyncResponderType);

        var genericSyncResponderType = messagingAssembly.GetType("Modus.Core.Messaging.ISyncResponder`2", throwOnError: true, ignoreCase: false)!;
        Assert.True(genericSyncResponderType.IsInterface);
    }

    private sealed record DispatchProbePayload(string Owner, int Value);

    private sealed class DelegateResponder : ISyncResponder
    {
        private readonly Func<SyncRequest, SyncResponse> _handler;

        public DelegateResponder(Func<SyncRequest, SyncResponse> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public SyncResponse Handle(SyncRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _handler(request);
        }
    }
}
