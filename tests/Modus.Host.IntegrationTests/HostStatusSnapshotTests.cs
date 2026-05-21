using Modus.Core.Hosting;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Plugins.Descriptors;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class HostStatusSnapshotTests
{
    [Fact]
    [Trait("ChecklistItem", "Define host status snapshot contract containing host state and loaded plugin metadata [mandatory - status endpoint]")]
    public void BuildHostStatusSnapshot_GivenLoadedPlugins_IncludesPluginIdentityVersionAndState()
    {
        var builder = new HostStatusSnapshotBuilder();
        var activeDescriptor = new PluginDescriptor(
            new PluginId("Plugin.Inventory"),
            "Plugin.Inventory",
            new Version(2, 1, 0),
            [new CapabilityName("Cap.Inventory"), new CapabilityName("Cap.Shared")],
            []);
        var failedDescriptor = new PluginDescriptor(
            new PluginId("Plugin.Legacy"),
            "Plugin.Legacy",
            new Version(1, 0, 0),
            [new CapabilityName("Cap.Legacy")],
            []);

        var snapshot = builder.Build(
            hostHealthy: true,
            descriptors: [activeDescriptor, failedDescriptor],
            activatedPluginIds: ["Plugin.Inventory"],
            failedPluginIds: ["Plugin.Legacy"],
            capabilityOwners: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Cap.Inventory"] = "Plugin.Inventory",
                ["Cap.Shared"] = "Plugin.Inventory",
            });

        Assert.Equal(HostRuntimeState.Degraded, snapshot.State);

        var loaded = Assert.Single(snapshot.LoadedPlugins);
        Assert.Equal(new PluginId("Plugin.Inventory"), loaded.PluginId);
        Assert.Equal("Plugin.Inventory", loaded.AssemblyName);
        Assert.Equal(new Version(2, 1, 0), loaded.Version);
        Assert.Equal(PluginRuntimeState.Active, loaded.LifecycleState);
        Assert.Equal(
            ["Cap.Inventory", "Cap.Shared"],
            loaded.Capabilities.Select(static capability => capability.Value).ToArray());
    }

    [Fact]
    [Trait("ChecklistItem", "Define host status snapshot contract containing host state and loaded plugin metadata [mandatory - status endpoint]")]
    public void BuildHostStatusSnapshot_GivenCapabilityResolution_IncludesCapabilityOwnership()
    {
        var builder = new HostStatusSnapshotBuilder();
        var descriptors = new[]
        {
            new PluginDescriptor(
                new PluginId("Plugin.Inventory"),
                "Plugin.Inventory",
                new Version(2, 1, 0),
                [new CapabilityName("Cap.Inventory")],
                []),
            new PluginDescriptor(
                new PluginId("Plugin.Orders"),
                "Plugin.Orders",
                new Version(1, 5, 0),
                [new CapabilityName("Cap.Orders")],
                [])
        };

        var snapshot = builder.Build(
            hostHealthy: true,
            descriptors: descriptors,
            activatedPluginIds: ["Plugin.Inventory", "Plugin.Orders"],
            failedPluginIds: [],
            capabilityOwners: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Cap.Orders"] = "Plugin.Orders",
                ["Cap.Inventory"] = "Plugin.Inventory",
            });

        Assert.Equal(HostRuntimeState.Running, snapshot.State);
        Assert.Equal(2, snapshot.CapabilityOwnership.Count);
        Assert.Collection(
            snapshot.CapabilityOwnership,
            first =>
            {
                Assert.Equal(new CapabilityName("Cap.Inventory"), first.Capability);
                Assert.Equal(new PluginId("Plugin.Inventory"), first.OwnerPluginId);
            },
            second =>
            {
                Assert.Equal(new CapabilityName("Cap.Orders"), second.Capability);
                Assert.Equal(new PluginId("Plugin.Orders"), second.OwnerPluginId);
            });
    }
}