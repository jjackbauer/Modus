using Modus.Core.Plugins;
using Modus.Host.Plugins.Validation;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DiscoveryValidationPipelineTests
{
    [Fact]
    public void DiscoveryPipeline_GivenUnorderedPluginSet_ExpectedDeterministicPluginIdOrdering()
    {
        var discovery = new PluginDiscoveryService();
        var input = new[]
        {
            new PluginDescriptor(new PluginId("Plugin.C"), "Plugin.C", new Version(1, 0), [new CapabilityName("Cap.C")], []),
            new PluginDescriptor(new PluginId("Plugin.A"), "Plugin.A", new Version(1, 0), [new CapabilityName("Cap.A")], []),
            new PluginDescriptor(new PluginId("Plugin.B"), "Plugin.B", new Version(1, 0), [new CapabilityName("Cap.B")], []),
        };

        var discovered = discovery.Discover(input);

        Assert.Equal(["Plugin.A", "Plugin.B", "Plugin.C"], discovered.Select(x => x.PluginId.Value).ToArray());
    }

    [Fact]
    public void ValidationPipeline_GivenContractViolation_ExpectedDeterministicValidationFailureReason()
    {
        var validation = new PluginValidationService();
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.ContractsInvalid"),
            "Plugin.ContractsInvalid",
            new Version(1, 0),
            [new CapabilityName("Cap.ContractsInvalid")],
            [],
            IsContractCompliant: false,
            IsValidAssembly: true);

        var result = validation.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Equal("contract violation", result.FailureReason);
    }

    [Fact]
    public void ValidationPipeline_GivenInvalidAssembly_ExpectedDeterministicValidationFailureReason()
    {
        var validation = new PluginValidationService();
        var descriptor = new PluginDescriptor(
            new PluginId("Plugin.AssemblyInvalid"),
            "Plugin.AssemblyInvalid",
            new Version(1, 0),
            [new CapabilityName("Cap.AssemblyInvalid")],
            [],
            IsContractCompliant: true,
            IsValidAssembly: false);

        var result = validation.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Equal("invalid assembly", result.FailureReason);
    }
}
