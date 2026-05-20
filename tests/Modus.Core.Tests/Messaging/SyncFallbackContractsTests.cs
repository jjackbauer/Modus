using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public sealed class SyncFallbackContractsTests
{
    [Fact]
    public void SyncRequest_GivenExplicitFallback_ExpectedReasonAndCodeCaptured()
    {
        var request = SyncRequest.ForExplicitFallback(
            operation: new OperationName("reconcile-order"),
            fallbackReason: SyncFallbackReason.ConsistencyRequirement,
            fallbackReasonCode: "EVENTUAL_CONSISTENCY_GAP",
            correlationId: new CorrelationId("corr-42"));

        Assert.Equal(new OperationName("reconcile-order"), request.Operation);
        Assert.True(request.IsFallbackExplicit);
        Assert.Equal(SyncFallbackReason.ConsistencyRequirement, request.FallbackReason);
        Assert.Equal("EVENTUAL_CONSISTENCY_GAP", request.FallbackReasonCode);
        Assert.Equal(new CorrelationId("corr-42"), request.CorrelationId);
    }

    [Fact]
    public void SyncRequest_GivenNonFallbackWithFallbackReason_ExpectedValidationException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SyncRequest(
                Operation: new OperationName("reconcile-order"),
                IsFallbackExplicit: false,
                FallbackReason: SyncFallbackReason.ManualOverride,
                FallbackReasonCode: null));

        Assert.Contains("FallbackReason", exception.Message);
    }

    [Fact]
    public void SyncResponse_GivenRejectedFallbackOutcome_ExpectedStatusDefaultsToRejected()
    {
        var response = new SyncResponse(
            Success: false,
            Payload: "fallback-not-explicit",
            ServedFromFallback: false,
            CorrelationId: new CorrelationId("corr-77"));

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.False(response.ServedFromFallback);
        Assert.Equal(new CorrelationId("corr-77"), response.CorrelationId);
    }

    [Fact]
    public void SyncResponse_GivenExplicitInvalidSuccessStatusCombination_ExpectedValidationException()
    {
        Assert.Throws<ArgumentException>(() =>
            new SyncResponse(
                Success: true,
                Payload: "ok",
                Status: SyncResponseStatus.Failed));
    }

    [Fact]
    public void SyncRequest_Operation_PropertyType_IsOperationName()
    {
        Assert.Equal(typeof(OperationName), typeof(SyncRequest).GetProperty(nameof(SyncRequest.Operation))!.PropertyType);
    }

    [Fact]
    public void SyncRequest_CorrelationId_PropertyType_IsNullableCorrelationId()
    {
        Assert.Equal(typeof(CorrelationId?), typeof(SyncRequest).GetProperty(nameof(SyncRequest.CorrelationId))!.PropertyType);
    }

    [Fact]
    public void SyncRequest_GivenValidOperationName_ConstructsSuccessfully()
    {
        var request = new SyncRequest(
            Operation: new OperationName("process-order"),
            IsFallbackExplicit: false);

        Assert.Equal(new OperationName("process-order"), request.Operation);
        Assert.Null(request.CorrelationId);
    }

    [Fact]
    public void SyncRequest_ForExplicitFallback_GivenOperationName_ConstructsCorrectly()
    {
        var request = SyncRequest.ForExplicitFallback(
            operation: new OperationName("sync-op"),
            fallbackReason: SyncFallbackReason.ManualOverride,
            fallbackReasonCode: "FORCED",
            correlationId: new CorrelationId("c-1"));

        Assert.Equal(new OperationName("sync-op"), request.Operation);
        Assert.True(request.IsFallbackExplicit);
        Assert.Equal(new CorrelationId("c-1"), request.CorrelationId);
    }

    [Fact]
    public void SyncResponse_CorrelationId_PropertyType_IsNullableCorrelationId()
    {
        Assert.Equal(typeof(CorrelationId?), typeof(SyncResponse).GetProperty(nameof(SyncResponse.CorrelationId))!.PropertyType);
    }

    [Fact]
    public void SyncResponse_GivenNullCorrelationId_ConstructsSuccessfully()
    {
        var response = new SyncResponse(Success: true, Payload: "ok");

        Assert.Null(response.CorrelationId);
    }
}
