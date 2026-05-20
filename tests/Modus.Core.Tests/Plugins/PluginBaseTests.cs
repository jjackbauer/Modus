using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginBaseTests
{
    [Fact]
    public void PluginBase_GivenNullLoadContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Load(null!));
    }

    [Fact]
    public void PluginBase_GivenNullStartContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Start(null!));
    }

    [Fact]
    public void PluginBase_GivenNullStopContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Stop(null!));
    }

    [Fact]
    public void PluginBase_GivenNullUnloadContext_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.Unload(null!));
    }

    [Fact]
    public void PluginBase_GivenNullSchedulerInRegisterSchedules_ExpectedArgumentNullException()
    {
        var plugin = new TestPlugin();

        Assert.Throws<ArgumentNullException>(() => plugin.RegisterSchedules(null!));
    }

    [Fact]
    public void PluginBase_GivenDerivedType_ExpectedDefaultsAreSafeAndOverridable()
    {
        var plugin = new TestPlugin();

        Assert.IsAssignableFrom<IPluginContract>(plugin);
        Assert.IsAssignableFrom<IPluginLifecycle>(plugin);
        Assert.IsAssignableFrom<IPluginOperationCatalog>(plugin);
        Assert.IsAssignableFrom<IPluginScheduledEvents>(plugin);

        Assert.Equal(new PluginId("Test.Plugin"), plugin.PluginId);
        Assert.Equal(new ContractName("Modus.PluginContract"), plugin.ContractName);
        Assert.Equal(new Version(1, 0, 0), plugin.ContractVersion);
        Assert.Equal([new OperationName("Test.Operation")], plugin.SupportedOperations);
    }

    private sealed class TestPlugin : PluginBase
    {
        public override PluginId PluginId => new PluginId("Test.Plugin");

        public override IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("Test.Operation")];
    }
}