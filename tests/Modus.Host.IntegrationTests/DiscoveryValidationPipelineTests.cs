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
            new PluginDescriptor("Plugin.C", "Plugin.C", new Version(1, 0), ["Cap.C"], []),
            new PluginDescriptor("Plugin.A", "Plugin.A", new Version(1, 0), ["Cap.A"], []),
            new PluginDescriptor("Plugin.B", "Plugin.B", new Version(1, 0), ["Cap.B"], []),
        };

        var discovered = discovery.Discover(input);

        Assert.Equal(["Plugin.A", "Plugin.B", "Plugin.C"], discovered.Select(x => x.PluginId).ToArray());
    }

    [Fact]
    public void ValidationPipeline_GivenContractViolation_ExpectedDeterministicValidationFailureReason()
    {
        var validation = new PluginValidationService();
        var descriptor = new PluginDescriptor(
            "Plugin.ContractsInvalid",
            "Plugin.ContractsInvalid",
            new Version(1, 0),
            ["Cap.ContractsInvalid"],
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
            "Plugin.AssemblyInvalid",
            "Plugin.AssemblyInvalid",
            new Version(1, 0),
            ["Cap.AssemblyInvalid"],
            [],
            IsContractCompliant: true,
            IsValidAssembly: false);

        var result = validation.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Equal("invalid assembly", result.FailureReason);
    }
}
