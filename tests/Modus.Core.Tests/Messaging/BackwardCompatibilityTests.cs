using System.Reflection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public sealed class BackwardCompatibilityTests
{
    [Fact]
    public void SyncRequest_GivenExistingNonGenericApi_ExpectedTypeUnchanged()
    {
        var type = typeof(SyncRequest);

        Assert.True(type.IsSealed);
        Assert.Null(type.GetProperty("Payload"));
        Assert.NotNull(type.GetProperty(nameof(SyncRequest.Operation)));
        Assert.NotNull(type.GetProperty(nameof(SyncRequest.IsFallbackExplicit)));
        Assert.NotNull(type.GetProperty(nameof(SyncRequest.FallbackReasonCode)));

        var request = SyncRequest.ForStandardPath(new OperationName("op"));
        Assert.False(request.IsFallbackExplicit);
        Assert.Equal(new OperationName("op"), request.Operation);
    }

    [Fact]
    public void SyncResponse_GivenExistingNonGenericApi_ExpectedStringPayloadPreserved()
    {
        var type = typeof(SyncResponse);

        Assert.True(type.IsSealed);
        var payloadProp = type.GetProperty(nameof(SyncResponse.Payload));
        Assert.NotNull(payloadProp);
        Assert.Equal(typeof(string), payloadProp!.PropertyType);

        var response = new SyncResponse(Success: true, Payload: "hello");
        Assert.Equal("hello", response.Payload);
        Assert.True(response.Success);
    }
}
