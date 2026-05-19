using Modus.Host.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DescriptorConstructionTests
{
    [Fact]
    public void DescriptorFactory_GivenValidCsprojPath_ExpectedDeterministicPluginDescriptorCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-descriptor-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var projectPath = Path.Combine(pluginsPath, "Plugin Payments - Gateway.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Payments-Gateway Ext</AssemblyName><ModusVersion>2.3.4</ModusVersion><ModusCapabilities>Cap.Payments; Cap.Billing</ModusCapabilities><ModusDependsOn>Plugin.Auth, Plugin.Core</ModusDependsOn></PropertyGroup></Project>");

            var factory = new PluginProjectDescriptorFactory();

            var first = factory.Create(projectPath);
            var second = factory.Create(projectPath);

            Assert.Equal("Plugin.Payments.Gateway", first.PluginId);
            Assert.Equal("Plugin.Payments.Gateway", second.PluginId);
            Assert.Equal("Payments.Gateway.Ext", first.AssemblyName);
            Assert.Equal(first.AssemblyName, second.AssemblyName);
            Assert.Equal(new Version(2, 3, 4), first.Version);
            Assert.Equal(first.Version, second.Version);
            Assert.Equal(["Cap.Billing", "Cap.Payments"], first.Capabilities);
            Assert.Equal(first.Capabilities, second.Capabilities);
            Assert.Equal(["Plugin.Auth", "Plugin.Core"], first.DependsOn);
            Assert.Equal(first.DependsOn, second.DependsOn);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DescriptorFactory_GivenMalformedProjectMetadata_ExpectedDescriptorCreationFailureWithDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-descriptor-malformed-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Malformed.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>");

            var onboarding = watcher.OnProjectCreated(projectPath);

            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.False(onboarding.PluginActivated);
            Assert.Equal("Plugin.Malformed", onboarding.PluginId);
            Assert.Contains("Plugin.Malformed", onboarding.FailedPluginIds);
            Assert.Contains("stage=descriptor outcome=failure reason=Malformed project metadata.", onboarding.Diagnostics, StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
