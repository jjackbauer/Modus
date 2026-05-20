using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public sealed class SyncRequestGenericTests
{
    [Fact]
    public void Constructor_GivenTypedReferencePayload_ExpectedPayloadPropertyAccessible()
    {
        var request = new SyncRequest<string>(
            Operation: new OperationName("process-order"),
            Payload: "order-payload",
            IsFallbackExplicit: false);

        Assert.Equal("order-payload", request.Payload);
    }

    [Fact]
    public void Constructor_GivenNullReferenceTypePayload_ExpectedArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SyncRequest<string>(
                Operation: new OperationName("process-order"),
                Payload: null!,
                IsFallbackExplicit: false));
    }

    [Fact]
    public void Constructor_GivenValueTypePayload_ExpectedDefaultValueAccepted()
    {
        var request = new SyncRequest<int>(
            Operation: new OperationName("process-order"),
            Payload: default(int),
            IsFallbackExplicit: false);

        Assert.Equal(0, request.Payload);
    }

    [Fact]
    public void Constructor_GivenFallbackFields_ExpectedFallbackValidationStillApplied()
    {
        Assert.Throws<ArgumentException>(() =>
            new SyncRequest<string>(
                Operation: new OperationName("process-order"),
                Payload: "order-payload",
                IsFallbackExplicit: true,
                FallbackReason: SyncFallbackReason.None));
    }

    [Fact]
    public void ForStandardPath_GivenOperationAndPayload_ExpectedNonFallbackRequestWithPayload()
    {
        var request = SyncRequest<string>.ForStandardPath(
            operation: new OperationName("process-order"),
            payload: "order-payload");

        Assert.Equal("order-payload", request.Payload);
        Assert.False(request.IsFallbackExplicit);
        Assert.Equal(SyncFallbackReason.None, request.FallbackReason);
    }

    [Fact]
    public void ForStandardPath_GivenOptionalCorrelationId_ExpectedCorrelationIdCaptured()
    {
        var correlationId = new CorrelationId("corr-123");
        var request = SyncRequest<string>.ForStandardPath(
            operation: new OperationName("process-order"),
            payload: "order-payload",
            correlationId: correlationId);

        Assert.Equal(correlationId, request.CorrelationId);
    }

    [Fact]
    public void ForStandardPath_GivenNoCorrelationId_ExpectedCorrelationIdIsNull()
    {
        var request = SyncRequest<string>.ForStandardPath(
            operation: new OperationName("process-order"),
            payload: "order-payload");

        Assert.Null(request.CorrelationId);
    }

    [Fact]
    public void ForExplicitFallback_GivenTypedPayload_ExpectedFallbackFlagsAndPayloadSet()
    {
        var request = SyncRequest<string>.ForExplicitFallback(
            operation: new OperationName("process-order"),
            payload: "order-payload",
            fallbackReason: SyncFallbackReason.SubscriberUnavailable,
            fallbackReasonCode: "SVC_DOWN");

        Assert.Equal("order-payload", request.Payload);
        Assert.True(request.IsFallbackExplicit);
        Assert.Equal(SyncFallbackReason.SubscriberUnavailable, request.FallbackReason);
        Assert.Equal("SVC_DOWN", request.FallbackReasonCode);
    }

    [Fact]
    public void ForExplicitFallback_GivenNullPayload_ExpectedArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SyncRequest<string>.ForExplicitFallback(
                operation: new OperationName("process-order"),
                payload: null!,
                fallbackReason: SyncFallbackReason.SubscriberUnavailable,
                fallbackReasonCode: "SVC_DOWN"));
    }
}
