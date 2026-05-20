using Modus.Core.Plugins;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class DescriptorValidationGateTests
{
    [Theory]
    [InlineData("Plugin.Payments.Gateway", "ModusContractCompliant", "false", "contract violation")]
    [InlineData("Plugin.Orders.Fulfillment", "ModusIsValidAssembly", "false", "invalid assembly")]
    public void ValidationGate_GivenNonCompliantContractMetadataOrInvalidAssembly_ExpectedDeterministicFailureDiagnostics(
        string projectName,
        string metadataProperty,
        string metadataValue,
        string expectedReason)
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-validation-gate-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var watcher = new PluginFolderWatcher();
            var startup = watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, $"{projectName}.csproj");
            File.WriteAllText(projectPath, BuildConcretePluginProject(projectName, metadataProperty, metadataValue));

            var onboarding = watcher.OnProjectCreated(projectPath);

            Assert.True(startup.HostHealthy);
            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.False(onboarding.PluginActivated);
            Assert.Equal(new PluginId(projectName), onboarding.PluginId);
            Assert.Contains(new PluginId(projectName), onboarding.FailedPluginIds);
            Assert.Contains($"stage=validation plugin={projectName} outcome=failure reason={expectedReason}", onboarding.Diagnostics, StringComparer.Ordinal);
            Assert.Contains($"stage=isolation plugin={projectName} failed-stage=validation outcome=isolated", onboarding.Diagnostics, StringComparer.Ordinal);
            Assert.Contains("stage=continuity outcome=preserved", onboarding.Diagnostics, StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string BuildConcretePluginProject(string projectName, string metadataProperty, string metadataValue)
    {
        var assemblyName = projectName;
        var version = projectName == "Plugin.Payments.Gateway" ? "2.1.0" : "1.4.3";
        var capabilities = projectName == "Plugin.Payments.Gateway"
            ? "Cap.Payments;Cap.Billing"
            : "Cap.Orders;Cap.Fulfillment";
        var operations = projectName == "Plugin.Payments.Gateway"
            ? "Payments.SyncLedger;Payments.EmitSettlement"
            : "Orders.AllocateInventory;Orders.CreateShipment";

        return
            "<Project Sdk=\"Microsoft.NET.Sdk\">" +
            "<PropertyGroup>" +
            "<TargetFramework>net10.0</TargetFramework>" +
            $"<AssemblyName>{assemblyName}</AssemblyName>" +
            $"<ModusVersion>{version}</ModusVersion>" +
            $"<ModusCapabilities>{capabilities}</ModusCapabilities>" +
            $"<ModusOperations>{operations}</ModusOperations>" +
            $"<{metadataProperty}>{metadataValue}</{metadataProperty}>" +
            "</PropertyGroup>" +
            "</Project>";
    }
}