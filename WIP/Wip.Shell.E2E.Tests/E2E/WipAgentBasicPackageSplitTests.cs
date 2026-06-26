using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[Collection(PackageSplitSerialCollection.Name)]
public sealed class WipAgentBasicPackageSplitTests
{
    private const string ChecklistItem = "Add dedicated package surface for `Wip.Agent.Basic` (including `PlanOnlyAgent` and optional deterministic `PatchAgent` sample path) instead of implicit runtime-only placement [depends on MVP package split compliance]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task WipAgentBasicPackage_GivenPackAndLoad_ExpectedPlanOnlyAgentDiscoverableWithoutRuntimeInternalBinding()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "WIP", "Wip.Agent.Basic", "Wip.Agent.Basic.csproj");
        var packageOutputPath = Path.Combine(Path.GetTempPath(), $"wip-agent-basic-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packageOutputPath);

        try
        {
            Assert.True(File.Exists(projectPath), $"Expected dedicated package project at '{projectPath}'.");

            var packResult = await RunProcessAsync(
                "dotnet",
                $"pack \"{projectPath}\" -c Debug -o \"{packageOutputPath}\" --nologo",
                repositoryRoot,
                CancellationToken.None);

            Assert.Equal(0, packResult.ExitCode);

            var packagePath = Directory.EnumerateFiles(packageOutputPath, "Wip.Agent.Basic.*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static path => path, StringComparer.Ordinal)
                .FirstOrDefault();

            Assert.False(string.IsNullOrWhiteSpace(packagePath), $"No Wip.Agent.Basic package produced. Dotnet output:{Environment.NewLine}{packResult.Output}");

            using var package = ZipFile.OpenRead(packagePath!);
            var packageEntry = package.Entries.SingleOrDefault(static entry =>
                string.Equals(entry.FullName, "lib/net10.0/Wip.Agent.Basic.dll", StringComparison.Ordinal));

            Assert.NotNull(packageEntry);

            var assemblyExtractionPath = Path.Combine(packageOutputPath, "Wip.Agent.Basic.dll");
            packageEntry!.ExtractToFile(assemblyExtractionPath, overwrite: true);

            var assemblyLoadContext = new AssemblyLoadContext($"wip-agent-basic-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var assembly = assemblyLoadContext.LoadFromAssemblyPath(assemblyExtractionPath);
                Assert.NotNull(assembly.GetType("Wip.Agent.Basic.PlanOnlyAgent", throwOnError: false, ignoreCase: false));
                Assert.NotNull(assembly.GetType("Wip.Agent.Basic.PatchAgent", throwOnError: false, ignoreCase: false));
            }
            finally
            {
                assemblyLoadContext.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var runtimeAssemblyPath = Path.Combine(repositoryRoot, "WIP", "Wip.Runtime", "bin", "Debug", "net10.0", "Wip.Runtime.dll");
            if (File.Exists(runtimeAssemblyPath))
            {
                var runtimeAssembly = Assembly.LoadFrom(runtimeAssemblyPath);
                Assert.Null(runtimeAssembly.GetType("Wip.Runtime.Runtime.PlanOnlyAgent", throwOnError: false, ignoreCase: false));
            }
        }
        finally
        {
            if (Directory.Exists(packageOutputPath))
            {
                try
                {
                    Directory.Delete(packageOutputPath, recursive: true);
                }
                catch (IOException)
                {
                    // Cleanup is best-effort because collectible ALC file handles can outlive test scope.
                }
                catch (UnauthorizedAccessException)
                {
                    // Cleanup is best-effort because collectible ALC file handles can outlive test scope.
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Modus.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current test base directory.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.Concat(stdout, Environment.NewLine, stderr).Trim();

        return new ProcessResult(process.ExitCode, output);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
