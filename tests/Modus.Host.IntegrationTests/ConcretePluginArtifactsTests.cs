using Modus.Core.Plugins;
using Modus.Host.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class ConcretePluginArtifactsTests
{
    [Fact]
    public void PluginArtifacts_GivenConcreteClassLibraryPlugin_ExpectedDescriptorContainsStableIdentityVersionAndCapabilities()
    {
        var descriptorFactory = new PluginProjectDescriptorFactory();
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SamplePlugins",
            "Plugin.Payments.Gateway",
            "Plugin.Payments.Gateway.csproj");

        var first = descriptorFactory.Create(projectPath);
        var second = descriptorFactory.Create(projectPath);

        Assert.Equal(new PluginId("Plugin.Payments.Gateway"), first.PluginId);
        Assert.Equal(new PluginId("Plugin.Payments.Gateway"), second.PluginId);
        Assert.Equal(new Version(2, 1, 0), first.Version);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal([new CapabilityName("Cap.Billing"), new CapabilityName("Cap.Payments")], first.Capabilities);
        Assert.Equal(first.Capabilities, second.Capabilities);
    }

    [Fact]
    public void PluginArtifacts_GivenMultipleConcretePlugins_ExpectedDeclaredOperationsAreNonEmptyAndDeterministic()
    {
        var descriptorFactory = new PluginProjectDescriptorFactory();
        var repositoryRoot = FindRepositoryRoot();

        var projectPaths = new[]
        {
            Path.Combine(repositoryRoot, "src", "SamplePlugins", "Plugin.Payments.Gateway", "Plugin.Payments.Gateway.csproj"),
            Path.Combine(repositoryRoot, "src", "SamplePlugins", "Plugin.Orders.Fulfillment", "Plugin.Orders.Fulfillment.csproj"),
        };

        var firstPass = projectPaths
            .Select(descriptorFactory.Create)
            .OrderBy(x => x.PluginId.Value, StringComparer.Ordinal)
            .ToArray();

        var secondPass = projectPaths
            .Select(descriptorFactory.Create)
            .OrderBy(x => x.PluginId.Value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            firstPass.Select(x => x.PluginId).ToArray(),
            secondPass.Select(x => x.PluginId).ToArray());

        Assert.All(firstPass, descriptor =>
        {
            Assert.NotNull(descriptor.DeclaredOperations);
            Assert.NotEmpty(descriptor.DeclaredOperations!);

            var expectedOperations = descriptor.DeclaredOperations!
                .OrderBy(x => x.Value, StringComparer.Ordinal)
                .Distinct()
                .ToArray();

            Assert.Equal(expectedOperations, descriptor.DeclaredOperations);
        });

        Assert.Equal(
            firstPass.Select(x => x.DeclaredOperations!.ToArray()).ToArray(),
            secondPass.Select(x => x.DeclaredOperations!.ToArray()).ToArray());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Modus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}