using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Loader;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

[Collection(PackageSplitSerialCollection.Name)]
public sealed class SamplesTodoAppPackageSplitTests
{
    private const string ChecklistItem = "Add `Samples.TodoApp` minimal solution fixture package and keep `Samples.TodoApp.WipAgents` as external typed plugin proof consumed by shell E2E [depends on sample package split compliance]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task SamplesTodoAppPackage_GivenPackAndLoad_ExpectedMinimalSolutionFixtureTypeAvailableForShellE2EConsumption()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "src", "Samples.TodoApp", "Samples.TodoApp.csproj");
        var packageOutputPath = Path.Combine(Path.GetTempPath(), $"samples-todoapp-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packageOutputPath);

        try
        {
            Assert.True(File.Exists(projectPath), $"Expected dedicated sample fixture package project at '{projectPath}'.");

            var packResult = await RunProcessAsync(
                "dotnet",
                $"pack \"{projectPath}\" -c Debug -o \"{packageOutputPath}\" --nologo",
                repositoryRoot,
                CancellationToken.None);

            Assert.Equal(0, packResult.ExitCode);

            var packagePath = Directory.EnumerateFiles(packageOutputPath, "Samples.TodoApp.*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static path => path, StringComparer.Ordinal)
                .FirstOrDefault();

            Assert.False(string.IsNullOrWhiteSpace(packagePath), $"No Samples.TodoApp package produced. Dotnet output:{Environment.NewLine}{packResult.Output}");

            using var package = ZipFile.OpenRead(packagePath!);
            var fixtureAssemblyEntry = package.Entries.SingleOrDefault(static entry =>
                string.Equals(entry.FullName, "lib/net10.0/Samples.TodoApp.dll", StringComparison.Ordinal));

            Assert.NotNull(fixtureAssemblyEntry);
            Assert.DoesNotContain(package.Entries, static entry =>
                string.Equals(entry.FullName, "lib/net10.0/Samples.TodoApp.WipAgents.dll", StringComparison.Ordinal));

            var assemblyExtractionPath = Path.Combine(packageOutputPath, "Samples.TodoApp.dll");
            fixtureAssemblyEntry!.ExtractToFile(assemblyExtractionPath, overwrite: true);

            var loadContext = new AssemblyLoadContext($"samples-todoapp-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(assemblyExtractionPath);
                Assert.NotNull(assembly.GetType("Samples.TodoApp.TempGitRepositoryFixture", throwOnError: false, ignoreCase: false));
            }
            finally
            {
                loadContext.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
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
            {
                return directory.FullName;
            }

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
