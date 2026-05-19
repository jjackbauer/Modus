using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Hosting;
using Modus.Host.Hosting;
using System.Diagnostics;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class ProgramCompositionTests
{
    [Fact]
    public async Task ProgramComposition_GivenExtensionBasedRegistration_ExpectedNoDirectRuntimeConstructionInEntrypoint()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-prog-composition-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            await using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(pluginsPath, CancellationToken.None);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.True(result.PluginsDirectoryExists);
            Assert.Equal(Path.GetFullPath(pluginsPath), result.PluginsPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task ProgramComposition_GivenExistingRunOnceFlow_ExpectedExitCodeSemanticsUnchanged(
        bool pluginsDirExists, int expectedExitCode)
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-prog-runonceflow-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(root);
        if (pluginsDirExists)
        {
            Directory.CreateDirectory(pluginsPath);
        }

        try
        {
            var result = await RunHostProcessAsync(root, pluginsPath, "--run-once");
            Assert.Equal(expectedExitCode, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartupPipeline_GivenHealthyPluginsDirectory_ExpectedDiagnosticsMatchBaselineStages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-startup-healthy-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            await using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(pluginsPath, CancellationToken.None);

            Assert.True(result.HostHealthy);
            Assert.Equal(
                [
                    "stage=startup pipeline=plugin-loader outcome=initialized",
                    $"stage=startup outcome=success watcher=registered path={Path.GetFullPath(pluginsPath)}",
                ],
                result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartupPipeline_GivenMissingPluginsDirectory_ExpectedHostHealthyFalseWithFailureReason()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-startup-missing-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(root);

        try
        {
            var services = new ServiceCollection();
            services.AddModusPluginHosting(opts => opts.PluginsPath = pluginsPath);

            await using var provider = services.BuildServiceProvider();
            var runner = provider.GetRequiredService<HostRunner>();

            var result = await runner.StartAsync(pluginsPath, CancellationToken.None);

            Assert.False(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.False(result.PluginsDirectoryExists);
            Assert.Contains(
                $"stage=startup outcome=failure reason=plugins directory missing path={Path.GetFullPath(pluginsPath)}",
                result.Diagnostics,
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunHostProcessAsync(
        string workingDirectory, params string[] arguments)
    {
        var hostAssemblyPath = typeof(HostRunner).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        startInfo.ArgumentList.Add(hostAssemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdOut, stdErr);
    }
}
