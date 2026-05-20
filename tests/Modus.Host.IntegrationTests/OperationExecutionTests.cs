using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class OperationExecutionTests
{
    [Fact]
    public void ExecuteDeclaredOperations_GivenActivatedPlugin_ExpectedAtLeastOneOperationExecutedAndLogged()
    {
        var runtime = new InMemoryHostRuntime();
        var inventory = new PluginDescriptor(
            new PluginId("Plugin.Inventory"),
            "Plugin.Inventory",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Inventory")],
            [],
            DeclaredOperations: [new OperationName("Inventory.RebuildSnapshot"), new OperationName("Inventory.EmitDelta")]);

        var result = runtime.Start([inventory]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Inventory", result.ActivatedPluginIds);
        Assert.Contains(
            "stage=operation plugin=Plugin.Inventory operation=Inventory.EmitDelta outcome=success",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Start_GivenNoDeclaredOperations_ExpectedHealthCheckFallbackOperationIsExecuted()
    {
        var runtime = new InMemoryHostRuntime();
        var plugin = new PluginDescriptor(
            new PluginId("Plugin.Health"),
            "Plugin.Health",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Health")],
            []);

        var result = runtime.Start([plugin]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Health", result.ActivatedPluginIds);
        Assert.Contains(
            "stage=operation plugin=Plugin.Health operation=Op.Plugin.Health.HealthCheck outcome=success",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void ExecuteDeclaredOperations_GivenOperationFailure_ExpectedPluginFailureIsolatedAndHostContinues()
    {
        var runtime = new InMemoryHostRuntime();
        var failing = new PluginDescriptor(
            new PluginId("Plugin.Failing"),
            "Plugin.Failing",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Failing")],
            [],
            DeclaredOperations: [new OperationName("Failing.Run")],
            FailingOperations: [new OperationName("Failing.Run")]);

        var healthy = new PluginDescriptor(
            new PluginId("Plugin.Healthy"),
            "Plugin.Healthy",
            new Version(3, 0, 0),
            [new CapabilityName("Cap.Healthy"), new CapabilityName("Cap.Shared")],
            []);

        var fallback = new PluginDescriptor(
            new PluginId("Plugin.Fallback"),
            "Plugin.Fallback",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Shared")],
            []);

        var result = runtime.Start([failing, healthy, fallback]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Failing", result.FailedPluginIds);
        Assert.Contains("Plugin.Healthy", result.ActivatedPluginIds);
        Assert.DoesNotContain("Plugin.Failing", result.ActivatedPluginIds);
        Assert.Equal("Plugin.Healthy", result.CapabilityOwners["Cap.Shared"]);
        Assert.Contains(
            "stage=operation plugin=Plugin.Failing operation=Failing.Run outcome=failure reason=operation exception",
            result.Diagnostics,
            StringComparer.Ordinal);
        Assert.Contains(
            "stage=rollback plugin=Plugin.Failing outcome=success reverted=activation,registration",
            result.Diagnostics,
            StringComparer.Ordinal);
        Assert.Contains(
            "stage=operation plugin=Plugin.Healthy operation=Op.Plugin.Healthy.HealthCheck outcome=success",
            result.Diagnostics,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Start_GivenSharedCapabilityAcrossVersions_ExpectedHighestVersionOwnsCapability()
    {
        var runtime = new InMemoryHostRuntime();
        var lowerVersionOwner = new PluginDescriptor(
            new PluginId("Plugin.CapabilityOwner.V1"),
            "Plugin.CapabilityOwner.V1",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Shared")],
            []);

        var higherVersionOwner = new PluginDescriptor(
            new PluginId("Plugin.CapabilityOwner.V2"),
            "Plugin.CapabilityOwner.V2",
            new Version(2, 0, 0),
            [new CapabilityName("Cap.Shared")],
            []);

        var result = runtime.Start([lowerVersionOwner, higherVersionOwner]);

        Assert.True(result.Started);
        Assert.Equal("Plugin.CapabilityOwner.V2", result.CapabilityOwners["Cap.Shared"]);
    }

    [Fact]
    public void Start_GivenSharedCapabilityWithSameVersion_ExpectedLexicographicallySmallerPluginIdWins()
    {
        var runtime = new InMemoryHostRuntime();
        var lexicalLater = new PluginDescriptor(
            new PluginId("Plugin.Shared.Zebra"),
            "Plugin.Shared.Zebra",
            new Version(3, 1, 0),
            [new CapabilityName("Cap.Shared")],
            []);

        var lexicalEarlier = new PluginDescriptor(
            new PluginId("Plugin.Shared.Alpha"),
            "Plugin.Shared.Alpha",
            new Version(3, 1, 0),
            [new CapabilityName("Cap.Shared")],
            []);

        var result = runtime.Start([lexicalLater, lexicalEarlier]);

        Assert.True(result.Started);
        Assert.Equal("Plugin.Shared.Alpha", result.CapabilityOwners["Cap.Shared"]);
    }

    [Fact]
    public void Start_GivenHigherVersionCapabilityOwnerFailsActivation_ExpectedHealthyLowerVersionOwnerSelectedDeterministically()
    {
        var runtime = new InMemoryHostRuntime();
        var lowerVersionHealthy = new PluginDescriptor(
            new PluginId("Plugin.Shared.Lower"),
            "Plugin.Shared.Lower",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Shared")],
            []);

        var higherVersionFailingActivation = new PluginDescriptor(
            new PluginId("Plugin.Shared.Higher"),
            "Plugin.Shared.Higher",
            new Version(2, 0, 0),
            [new CapabilityName("Cap.Shared")],
            [],
            FailOnActivation: true);

        var result = runtime.Start([lowerVersionHealthy, higherVersionFailingActivation]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Shared.Higher", result.FailedPluginIds);
        Assert.Equal("Plugin.Shared.Lower", result.CapabilityOwners["Cap.Shared"]);
    }

    [Fact]
    public void Start_GivenHigherVersionCapabilityOwnerFailsOperation_ExpectedHealthyLowerVersionOwnerSelectedDeterministically()
    {
        var runtime = new InMemoryHostRuntime();
        var lowerVersionHealthy = new PluginDescriptor(
            new PluginId("Plugin.Shared.Operation.Lower"),
            "Plugin.Shared.Operation.Lower",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Shared")],
            [],
            DeclaredOperations: [new OperationName("Shared.Ping")]);

        var higherVersionFailingOperation = new PluginDescriptor(
            new PluginId("Plugin.Shared.Operation.Higher"),
            "Plugin.Shared.Operation.Higher",
            new Version(3, 0, 0),
            [new CapabilityName("Cap.Shared")],
            [],
            DeclaredOperations: [new OperationName("Shared.Ping")],
            FailingOperations: [new OperationName("Shared.Ping")]);

        var result = runtime.Start([lowerVersionHealthy, higherVersionFailingOperation]);

        Assert.True(result.Started);
        Assert.Contains("Plugin.Shared.Operation.Higher", result.FailedPluginIds);
        Assert.Equal("Plugin.Shared.Operation.Lower", result.CapabilityOwners["Cap.Shared"]);
    }
}