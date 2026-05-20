using Modus.Core.Messaging;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public sealed class SyncResponseGenericTests
{
    [Fact]
    public void Constructor_GivenTypedReferencePayload_ExpectedPayloadPropertyAccessible()
    {
        var response = new SyncResponse<string>(
            Success: true,
            Payload: "order-result");

        Assert.Equal("order-result", response.Payload);
    }

    [Fact]
    public void Constructor_GivenNullReferenceTypePayload_ExpectedArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SyncResponse<string>(
                Success: true,
                Payload: null!));
    }

    [Fact]
    public void Constructor_GivenSuccessTrue_ExpectedStatusDefaultsToSuccess()
    {
        var response = new SyncResponse<string>(
            Success: true,
            Payload: "ok");

        Assert.Equal(SyncResponseStatus.Success, response.Status);
    }

    [Fact]
    public void Constructor_GivenSuccessFalse_ExpectedStatusDefaultsToRejected()
    {
        var response = new SyncResponse<string>(
            Success: false,
            Payload: "nope");

        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
    }

    [Fact]
    public void Constructor_GivenSuccessTrueWithNonSuccessStatus_ExpectedArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new SyncResponse<string>(
                Success: true,
                Payload: "ok",
                Status: SyncResponseStatus.Failed));
    }

    [Fact]
    public void Ok_GivenTypedPayload_ExpectedSuccessResponseWithCorrectFields()
    {
        var response = SyncResponse<string>.Ok("result-value");

        Assert.True(response.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.False(response.ServedFromFallback);
        Assert.Equal("result-value", response.Payload);
    }

    [Fact]
    public void Ok_GivenNullReferencePayload_ExpectedArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SyncResponse<string>.Ok(null!));
    }

    [Fact]
    public void Reject_GivenRejectedStatus_ExpectedNonSuccessResponseWithPayload()
    {
        var response = SyncResponse<string>.Reject("error-detail", SyncResponseStatus.Rejected);

        Assert.False(response.Success);
        Assert.Equal(SyncResponseStatus.Rejected, response.Status);
        Assert.Equal("error-detail", response.Payload);
    }

    [Fact]
    public void Reject_GivenSuccessStatus_ExpectedArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SyncResponse<string>.Reject("error-detail", SyncResponseStatus.Success));
    }
}
