using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PublicApiReferenceRefreshTests
{
    [Fact]
    [Trait("ChecklistItem", "Publish refreshed public API reference for Core contracts and Host lifecycle/runtime extension points")]
    public void PublicApiReferenceRefresh_GivenCoreContracts_ExpectedEachPublicContractHasUsageAndConstraints()
    {
        var coreReadme = ReadRepositoryFile(Path.Combine("src", "Modus.Core", "README.md"));

        Assert.Contains("## Public API Reference", coreReadme, StringComparison.Ordinal);
        Assert.Contains("### `IPluginContract`", coreReadme, StringComparison.Ordinal);
        Assert.Contains("### `IPluginLifecycle`", coreReadme, StringComparison.Ordinal);
        Assert.Contains("### `IPluginDependencyRegister`", coreReadme, StringComparison.Ordinal);
        Assert.Contains("### `IPluginOperationCatalog`", coreReadme, StringComparison.Ordinal);
        Assert.Contains("### `IPluginScheduledEvents` and `IPluginScheduler`", coreReadme, StringComparison.Ordinal);

        Assert.Contains("- Intent: define stable identity and contract versioning for every plugin capability.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("- Usage: implement `PluginId`, `ContractName`, and `ContractVersion` on plugin contracts.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("- Constraints: values must be deterministic and safe to compare with ordinal semantics.", coreReadme, StringComparison.Ordinal);

        Assert.Contains("- Intent: provide deterministic runtime hooks used by host activation flow.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("- Usage: implement `Load`, `Start`, `Stop`, and `Unload` for startup and shutdown stages.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("- Constraints: each hook must validate input context and avoid hidden cross-plugin coupling.", coreReadme, StringComparison.Ordinal);

        Assert.Contains("- Intent: register plugin dependencies through DI without leaking host internals.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("- Usage: call `services.AddPluginService<TService, TImplementation>(...)` from `Register`.", coreReadme, StringComparison.Ordinal);
        Assert.Contains("- Constraints: registrations must be idempotent and consistent with declared plugin lifetime.", coreReadme, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", "Publish refreshed public API reference for Core contracts and Host lifecycle/runtime extension points")]
    public void PublicApiReferenceRefresh_GivenHostExtensionPoints_ExpectedLifecycleHooksAndRegistrationFlowAreCovered()
    {
        var hostReadme = ReadRepositoryFile(Path.Combine("src", "Modus.Host", "README.md"));

        Assert.Contains("## Lifecycle and Runtime Extension Points", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`AddModusPluginHosting`", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`AddPluginHostingCore`", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`AddDiscoveredPlugins`", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`AddModusPluginHostingRuntime`", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`HostRunner.StartAsync`", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`TryResolvePluginsByContractInterfaceName`", hostReadme, StringComparison.Ordinal);
        Assert.Contains("`TryResolvePluginByTypeName`", hostReadme, StringComparison.Ordinal);

        var loadIndex = hostReadme.IndexOf("1. `Load(PluginLoadContext)`", StringComparison.Ordinal);
        var startIndex = hostReadme.IndexOf("2. `Start(PluginStartContext)`", StringComparison.Ordinal);
        var stopIndex = hostReadme.IndexOf("3. `Stop(PluginStopContext)`", StringComparison.Ordinal);
        var unloadIndex = hostReadme.IndexOf("4. `Unload(PluginUnloadContext)`", StringComparison.Ordinal);

        Assert.True(loadIndex >= 0, "Expected Load lifecycle hook to be documented.");
        Assert.True(startIndex > loadIndex, "Expected Start hook after Load.");
        Assert.True(stopIndex > startIndex, "Expected Stop hook after Start.");
        Assert.True(unloadIndex > stopIndex, "Expected Unload hook after Stop.");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "Modus.slnx");
            var filePath = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(solutionPath) && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root containing Modus.slnx and {relativePath}.");
    }
}