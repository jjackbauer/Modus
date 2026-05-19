using Modus.Core.Events;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class RuntimePluginLoadingTests
{
    [Fact]
    public void Loader_GivenPluginFolderWithAssemblies_ExpectedDeterministicAssemblyScanAndMetadataExtraction()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-runtime-loader-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(Path.Combine(pluginsPath, "b"));
        Directory.CreateDirectory(Path.Combine(pluginsPath, "a"));

        try
        {
            var hostAssemblySource = typeof(HostRunner).Assembly.Location;
            var coreAssemblySource = typeof(DomainEvent).Assembly.Location;

            var hostAssemblyCopy = Path.Combine(pluginsPath, "b", "host-copy.dll");
            var coreAssemblyCopy = Path.Combine(pluginsPath, "a", "core-copy.dll");
            File.Copy(hostAssemblySource, hostAssemblyCopy);
            File.Copy(coreAssemblySource, coreAssemblyCopy);

            var loader = new PluginLoader();

            var first = loader.ScanRuntimeAssemblies(pluginsPath);
            var second = loader.ScanRuntimeAssemblies(pluginsPath);

            Assert.Equal(first.Descriptors.Select(x => x.PluginId).ToArray(), second.Descriptors.Select(x => x.PluginId).ToArray());
            Assert.Equal(new[] { "Modus.Core", "Modus.Host" }, first.Descriptors.Select(x => x.PluginId).ToArray());
            Assert.All(first.Descriptors, descriptor =>
            {
                Assert.False(string.IsNullOrWhiteSpace(descriptor.AssemblyPath));
                Assert.True(descriptor.AssemblyFileSizeBytes > 0);
                Assert.NotNull(descriptor.Version);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Loader_GivenInvalidAssemblyFile_ExpectedFailureDiagnosticAndDescriptorSkipped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-runtime-loader-invalid-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var invalidAssembly = Path.Combine(pluginsPath, "broken.dll");
            File.WriteAllText(invalidAssembly, "not a real assembly");

            var loader = new PluginLoader();
            var result = loader.ScanRuntimeAssemblies(pluginsPath);

            Assert.Empty(result.Descriptors);
            Assert.Contains(
                result.Diagnostics,
                x => x.Contains("stage=scan", StringComparison.Ordinal)
                    && x.Contains("outcome=failure", StringComparison.Ordinal)
                    && x.Contains("broken.dll", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
