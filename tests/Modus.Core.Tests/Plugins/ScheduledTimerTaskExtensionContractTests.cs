using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class ScheduledTimerTaskExtensionContractTests
{
    [Fact]
    public void IScheduledTimerTaskExtension_GivenContractDefinition_ExpectedDeclaresOperationsSchedulesAndHandlerMembers()
    {
        var extensionType = typeof(IPluginContract).Assembly.GetType("Modus.Core.Plugins.IScheduledTimerTaskExtension");

        Assert.NotNull(extensionType);
        Assert.True(extensionType!.IsInterface);
        Assert.Equal(
            typeof(IReadOnlyCollection<OperationName>),
            extensionType.GetProperty(nameof(IPluginOperationCatalog.SupportedOperations))?.PropertyType);

        var registerSchedules = extensionType.GetMethod(nameof(IPluginScheduledEvents.RegisterSchedules), [typeof(IPluginScheduler)]);
        Assert.NotNull(registerSchedules);

        var handle = extensionType.GetMethod(nameof(ISyncResponder.Handle), [typeof(SyncRequest)]);
        Assert.NotNull(handle);
        Assert.Equal(typeof(SyncResponse), handle!.ReturnType);
    }

    [Fact]
    public void IScheduledTimerTaskExtension_GivenContractDefinition_ExpectedComposesOperationCatalogScheduleAndResponderContracts()
    {
        var extensionType = typeof(IPluginContract).Assembly.GetType("Modus.Core.Plugins.IScheduledTimerTaskExtension");

        Assert.NotNull(extensionType);
        Assert.True(typeof(IPluginOperationCatalog).IsAssignableFrom(extensionType));
        Assert.True(typeof(IPluginScheduledEvents).IsAssignableFrom(extensionType));
        Assert.True(typeof(ISyncResponder).IsAssignableFrom(extensionType));
    }
}