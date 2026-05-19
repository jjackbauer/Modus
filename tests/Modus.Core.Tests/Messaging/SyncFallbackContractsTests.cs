using Modus.Core.Messaging;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public sealed class SyncFallbackContractsTests
{
    [Fact]
    public void SyncRequest_GivenExplicitFallback_ExpectedReasonAndCodeCaptured()
    {
        var request = SyncRequest.ForExplicitFallback(
            operation: "reconcile-order",
            fallbackReason: SyncFallbackReason.ConsistencyRequirement,
            fallbackReasonCode: "EVENTUAL_CONSISTENCY_GAP",
            correlationId: "corr-42");

        Assert.Equal("reconcile-order", request.Operation);
        Assert.True(request.IsFallbackExplicit);
        Assert.Equal(SyncFallbackReason.ConsistencyRequirement, request.FallbackReason);
        Assert.Equal("EVENTUAL_CONSISTENCY_GAP", request.FallbackReasonCode);
        Assert.Equal("corr-42", request.CorrelationId);
    }

    [Fact]
    public void SyncRequest_GivenNonFallbackWithFallbackReason_ExpectedValidationException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SyncRequest(
                Operation: "reconcile-order",
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
            CorrelationId: "corr-77");

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.False(response.ServedFromFallback);
        Assert.Equal("corr-77", response.CorrelationId);
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
}
