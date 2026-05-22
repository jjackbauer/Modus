using System.Diagnostics;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class HostRunnerEntrypointTests
{
    [Fact]
    [Trait("ChecklistItem", "Modus.Host.Plugins-Folder-Refactoring.VerifyHostRunnerCompilation")]
    public async Task HostRunner_GivenValidPluginsPath_ExpectedWatcherRegisteredAndHostHealthy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-host-runner-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var runner = new HostRunner();

            var result = await runner.StartAsync(pluginsPath, CancellationToken.None);

            Assert.True(result.HostHealthy);
            Assert.True(result.WatcherRegistered);
            Assert.True(result.PluginsDirectoryExists);
            Assert.Equal(Path.GetFullPath(pluginsPath), result.PluginsPath);
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
    public async Task HostRunnerStartAsync_GivenMissingPluginsPath_ExpectedFailureDiagnosticWithoutProcessCrash()
    {
        var runner = new HostRunner();

        var result = await runner.StartAsync(string.Empty, CancellationToken.None);

        Assert.False(result.HostHealthy);
        Assert.False(result.WatcherRegistered);
        Assert.False(result.PluginsDirectoryExists);
        Assert.Equal(string.Empty, result.PluginsPath);
        Assert.Equal(
            [
                "stage=startup outcome=failure reason=plugins path missing",
            ],
            result.Diagnostics);
    }

    [Fact]
    public async Task Program_GivenUnhealthyStartup_ExpectedExitCodeOne()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-program-unhealthy-{Guid.NewGuid():N}");
        var missingPluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(root);

        try
        {
            var result = await RunHostProcessAsync(root, missingPluginsPath, "--run-once");
            var exitCode = result.ExitCode;
            Assert.Equal(1, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Modus.Host.Plugins-Folder-Refactoring.VerifyHostRunnerCompilation")]
    public async Task Program_GivenHealthyStartup_ExpectedExitCodeZero()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-program-healthy-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var result = await RunHostProcessAsync(root, pluginsPath, "--run-once");
            var exitCode = result.ExitCode;
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Program_GivenRuntimePluginsPath_ExpectedResolutionDiagnosticsIncludeSelectedLifetime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-program-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var pluginsPath = ResolvePluginOutputDirectory(FindRepositoryRoot());

        try
        {
            var result = await RunHostProcessAsync(root, pluginsPath, "--run-once");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                "stage=di outcome=success contract=IHostTelemetryPluginContract resolvedCount=1 selectedLifetime=Singleton",
                result.StdOut,
                StringComparison.Ordinal);
            Assert.Contains(
                "stage=di outcome=success contract=IMachineTelemetryPluginContract resolvedCount=1 selectedLifetime=Singleton",
                result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Program_GivenNoPluginsPathArgumentAndExistingDefaultPluginsDirectory_ExpectedDefaultPathResolvedFromCurrentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-program-default-healthy-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var result = await RunHostProcessAsync(root, "--run-once");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(
                $"stage=startup outcome=success watcher=registered path={Path.GetFullPath(pluginsPath)}",
                result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Program_GivenNoPluginsPathArgumentAndMissingDefaultPluginsDirectory_ExpectedUnhealthyStartupAndFailureDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-program-default-unhealthy-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(root);

        try
        {
            var result = await RunHostProcessAsync(root, "--run-once");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                $"stage=startup outcome=failure reason=plugins directory missing path={Path.GetFullPath(pluginsPath)}",
                result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunHostProcessAsync(string workingDirectory, params string[] arguments)
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

    private static string FindRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Modus.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string ResolvePluginOutputDirectory(string repoRoot)
    {
        var debugPath = Path.Combine(repoRoot, "plugins", "bin", "Debug", "net10.0");
        if (Directory.Exists(debugPath))
        {
            return debugPath;
        }

        var releasePath = Path.Combine(repoRoot, "plugins", "bin", "Release", "net10.0");
        if (Directory.Exists(releasePath))
        {
            return releasePath;
        }

        throw new DirectoryNotFoundException(
            $"Could not find plugin binaries. Checked '{debugPath}' and '{releasePath}'.");
    }
}