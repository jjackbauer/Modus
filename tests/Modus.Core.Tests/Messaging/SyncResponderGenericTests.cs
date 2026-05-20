using System.Reflection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Messaging;

public class SyncResponderGenericTests
{
    [Fact]
    public void Interface_GivenCoreAssembly_ExpectedGenericResponderInterfaceExists()
    {
        var assembly = typeof(ISyncResponder).Assembly;
        var type = assembly.GetTypes()
            .FirstOrDefault(t =>
                t.IsInterface &&
                t.IsGenericTypeDefinition &&
                t.Namespace == "Modus.Core.Messaging" &&
                t.Name == "ISyncResponder`2");

        Assert.NotNull(type);

        var methods = type.GetMethods();
        Assert.Single(methods);
        Assert.Equal("Handle", methods[0].Name);
    }

    [Fact]
    public void Handle_GivenConcreteImplementation_ExpectedTypedResponseReturnedFromHandler()
    {
        var handler = new StringResponder();
        var request = SyncRequest<string>.ForStandardPath(new OperationName("test"), "payload");

        var response = handler.Handle(request);

        Assert.True(response.Success);
        Assert.Equal("result", response.Payload);
    }

    private sealed class StringResponder : ISyncResponder<SyncRequest<string>, SyncResponse<string>>
    {
        public SyncResponse<string> Handle(SyncRequest<string> request)
            => SyncResponse<string>.Ok("result");
    }
}
