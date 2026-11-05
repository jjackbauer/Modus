using System.Runtime.CompilerServices;
using Samples.TodoApp.WipAgents;
using Wip.Modus.Hosting;
using Xunit;

namespace Wip.Modus.Tests.Hosting;

public sealed class PluginAssemblyLoadContextTests
{
    [Fact]
    public void PluginAssemblyLoadContext_GivenPluginAssembly_ExpectedCollectibleContextWithSharedContractIdentity()
    {
        var context = new PluginAssemblyLoadContext(typeof(TodoAppWipPlugin).Assembly.Location);
        try
        {
            Assert.True(context.IsCollectible);
            Assert.StartsWith("wip-plugin:", context.Name, StringComparison.Ordinal);

            var assembly = context.LoadPluginAssembly();
            var pluginType = Assert.Single(
                assembly.GetTypes(),
                static type => type.Name == "TodoAppWipPlugin");

            // The host contract assembly must stay in the default context (type identity), while
            // the plugin assembly itself lives in the collectible context.
            var pluginInterface = Assert.Single(pluginType.GetInterfaces(), static candidate => candidate.Name == "IWipHostPluginContract");
            Assert.Same(typeof(Wip.Modus.Hosting.IWipHostPluginContract), pluginInterface);
            Assert.True(typeof(Wip.Modus.Hosting.IWipHostPluginContract).IsAssignableFrom(pluginType));
            Assert.Contains(context.Assemblies, candidate => ReferenceEquals(candidate, assembly));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void PluginAssemblyLoadContext_GivenTwoContexts_ExpectedIsolatedAssemblyIdentities()
    {
        var assemblyPath = typeof(TodoAppWipPlugin).Assembly.Location;
        var first = new PluginAssemblyLoadContext(assemblyPath);
        var second = new PluginAssemblyLoadContext(assemblyPath);
        try
        {
            var firstAssembly = first.LoadPluginAssembly();
            var secondAssembly = second.LoadPluginAssembly();

            Assert.NotSame(firstAssembly, secondAssembly);
            Assert.NotEqual(first.Name, second.Name);
        }
        finally
        {
            first.Unload();
            second.Unload();
        }
    }

    [Fact]
    public void PluginAssemblyLoadContext_GivenUnload_ExpectedContextBecomesUnreferenced()
    {
        var weakReference = LoadAndUnload(typeof(TodoAppWipPlugin).Assembly.Location);

        for (var attempt = 0; attempt < 10 && weakReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weakReference.IsAlive);
    }

    [Fact]
    public async Task LoadPluginsAsync_GivenPluginAssembly_ExpectedCollectibleContextAndUnloadDiagnostics()
    {
        var pluginsPath = CreatePluginsFolderWithAssembly(typeof(TodoAppWipPlugin).Assembly.Location);
        try
        {
            var bridge = new ModusWipBridge(pluginsPath);

            var loaded = await bridge.LoadPluginsAsync(CancellationToken.None);
            Assert.Equal(1, loaded);
            Assert.Contains(
                bridge.GetLoadDiagnostics(),
                static diagnostic => diagnostic.Contains("collectible context", StringComparison.Ordinal));

            await bridge.StopPluginsAsync(CancellationToken.None);
            Assert.Contains(
                bridge.GetLoadDiagnostics(),
                static diagnostic => diagnostic.Contains("[unload]", StringComparison.Ordinal)
                    && diagnostic.Contains("Unloaded collectible context", StringComparison.Ordinal));
        }
        finally
        {
            // The collectible context releases its file handles on finalization; give the GC a
            // few passes and retry the cleanup.
            for (var attempt = 0; attempt < 10 && Directory.Exists(pluginsPath); attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    Directory.Delete(pluginsPath, recursive: true);
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(string assemblyPath)
    {
        var context = new PluginAssemblyLoadContext(assemblyPath);
        var assembly = context.LoadPluginAssembly();
        Assert.NotNull(assembly);
        var weakReference = new WeakReference(context);
        context.Unload();
        return weakReference;
    }

    private static string CreatePluginsFolderWithAssembly(string assemblyPath)
    {
        var pluginsPath = Path.Combine(Path.GetTempPath(), $"modus-wip-plugins-alc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginsPath);
        File.Copy(assemblyPath, Path.Combine(pluginsPath, Path.GetFileName(assemblyPath)), overwrite: true);
        return pluginsPath;
    }
    [Fact]
    public async Task LoadPluginsAsync_GivenTwoPluginsWithConflictingDependencyVersions_ExpectedEachResolvesItsOwnVersion()
    {
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal)
            ? "Release"
            : "Debug";
        var repositoryRoot = FindRepositoryRoot();
        var firstPath = CopyPluginFolder(Path.Combine(repositoryRoot, "tests", "Fixtures", "ConflictPlugin.V1", "bin", configuration, "net10.0"));
        var secondPath = CopyPluginFolder(Path.Combine(repositoryRoot, "tests", "Fixtures", "ConflictPlugin.V2", "bin", configuration, "net10.0"));
        try
        {
            var first = new ModusWipBridge(firstPath);
            var second = new ModusWipBridge(secondPath);

            Assert.Equal(1, await first.LoadPluginsAsync(CancellationToken.None));
            Assert.Equal(1, await second.LoadPluginsAsync(CancellationToken.None));

            var firstPlugin = Assert.Single(first.GetRunManifest().Plugins);
            var secondPlugin = Assert.Single(second.GetRunManifest().Plugins);

            Assert.Equal("conflict.plugin.v1", firstPlugin.PluginId);
            Assert.Equal("conflict.plugin.v2", secondPlugin.PluginId);
            Assert.Contains("conflict.operation.v1", firstPlugin.Capabilities);
            Assert.Contains("conflict.operation.v2", secondPlugin.Capabilities);

            await first.StopPluginsAsync(CancellationToken.None);
            await second.StopPluginsAsync(CancellationToken.None);
        }
        finally
        {
            DeleteWithRetry(firstPath);
            DeleteWithRetry(secondPath);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Modus.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string CopyPluginFolder(string sourceDirectory)
    {
        Assert.True(Directory.Exists(sourceDirectory), $"Missing fixture build output: {sourceDirectory}");
        var destination = Path.Combine(Path.GetTempPath(), $"modus-wip-conflict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        return destination;
    }

    private static void DeleteWithRetry(string path)
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(path); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}