using System.Diagnostics;
using System.Text.Json;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Artifacts.Local;
using Wip.Validation.DotNet.DotNet;
using Wip.Workspaces.Git;
using Xunit;

namespace Wip.Validation.DotNet.Tests.DotNet;

public sealed class DotNetValidationValidatorTests
{
    [Fact]
    public async Task ExecuteAsync_GivenSuccessfulBuildAndTest_ProducesPassingValidationReportWithCommandEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync();
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-success"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            TargetBranch: "main",
            SessionBranch: "main",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(0, result.Report.Test.ExitCode);
        Assert.Contains("build src/Example/Example.csproj", result.Report.Build.Command, StringComparison.Ordinal);
        Assert.Contains("test tests/Example.Tests/Example.Tests.csproj --no-build", result.Report.Test.Command, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Report.DiffHash));

        var reportPath = Path.Combine(fixture.RepositoryPath, result.ReportArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(reportPath));

        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, CancellationToken.None));
        var root = persisted.RootElement;

        Assert.Equal(context.SessionId.Value, root.GetProperty("SessionId").GetProperty("Value").GetString());
        Assert.Equal(0, root.GetProperty("Build").GetProperty("ExitCode").GetInt32());
        Assert.Equal(0, root.GetProperty("Test").GetProperty("ExitCode").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_GivenFailingTestCommand_ReturnsFailedResultAndPersistsFailureEvidence()
    {
        await using var fixture = await TempGitRepository.CreateAsync(testExitCode: 1, testStdErr: "simulated test failure");
        var artifactStore = new WipArtifactStoreLocal(fixture.RepositoryPath);
        var workspaceProvider = new WipWorkspaceProviderGit();
        var validator = new DotNetValidationValidator(artifactStore, workspaceProvider);

        var context = new CapabilityContext(new SessionId("session-validate-failure"), fixture.RepositoryPath);
        var request = new DotNetValidationRequest(
            BuildProjectPath: "src/Example/Example.csproj",
            TestProjectPath: "tests/Example.Tests/Example.Tests.csproj",
            RepositoryPath: fixture.RepositoryPath,
            TargetBranch: "main",
            SessionBranch: "main",
            DotNetExecutablePath: fixture.DotNetStubPath);

        var result = await validator.ExecuteAsync(request, context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Report.Build.ExitCode);
        Assert.Equal(1, result.Report.Test.ExitCode);
        Assert.Contains("simulated test failure", result.Report.Test.StandardError, StringComparison.Ordinal);

        var artifactDescriptors = await artifactStore.ListAsync(context.SessionId, CancellationToken.None);
        var descriptor = Assert.Single(artifactDescriptors);
        Assert.Equal(result.ReportArtifact.ArtifactId, descriptor.ArtifactId);
    }

    private sealed class TempGitRepository : IAsyncDisposable
    {
        private TempGitRepository(string repositoryPath, string dotNetStubPath)
        {
            RepositoryPath = repositoryPath;
            DotNetStubPath = dotNetStubPath;
        }

        public string RepositoryPath { get; }

        public string DotNetStubPath { get; }

        public static async ValueTask<TempGitRepository> CreateAsync(int buildExitCode = 0, int testExitCode = 0, string testStdErr = "")
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-validate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var fixture = new TempGitRepository(
                repositoryPath,
                dotNetStubPath: Path.Combine(repositoryPath, "dotnet.cmd"));

            await fixture.WriteDotNetStubAsync(buildExitCode, testExitCode, testStdErr);
            await fixture.InitializeGitRepositoryAsync();

            return fixture;
        }

        private async ValueTask InitializeGitRepositoryAsync()
        {
            await RunGitAsync("init", "--initial-branch=main", ".");
            await RunGitAsync("config", "user.email", "wip-validation-tests@example.test");
            await RunGitAsync("config", "user.name", "Wip Validation Tests");

            Directory.CreateDirectory(Path.Combine(RepositoryPath, "src", "Example"));
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "tests", "Example.Tests"));
            await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "Example", "Example.csproj"), "<Project />", CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "tests", "Example.Tests", "Example.Tests.csproj"), "<Project />", CancellationToken.None);

            await RunGitAsync("add", ".");
            await RunGitAsync("commit", "-m", "initial");
        }

        private async ValueTask WriteDotNetStubAsync(int buildExitCode, int testExitCode, string testStdErr)
        {
            var content =
                "@echo off\r\n" +
                "set cmd=%1\r\n" +
                "if /I \"%cmd%\"==\"build\" (\r\n" +
                $"  echo Build succeeded for %2\r\n  exit /b {buildExitCode}\r\n" +
                ")\r\n" +
                "if /I \"%cmd%\"==\"test\" (\r\n" +
                "  echo Test command invoked for %2\r\n" +
                (string.IsNullOrWhiteSpace(testStdErr)
                    ? string.Empty
                    : $"  1>&2 echo {EscapeBatchLiteral(testStdErr)}\r\n") +
                $"  exit /b {testExitCode}\r\n" +
                ")\r\n" +
                "echo Unsupported command 1>&2\r\n" +
                "exit /b 99\r\n";

            await File.WriteAllTextAsync(DotNetStubPath, content, CancellationToken.None);
        }

        private static string EscapeBatchLiteral(string value)
            => value.Replace("^", "^^", StringComparison.Ordinal)
                .Replace("&", "^&", StringComparison.Ordinal)
                .Replace("|", "^|", StringComparison.Ordinal)
                .Replace("<", "^<", StringComparison.Ordinal)
                .Replace(">", "^>", StringComparison.Ordinal);

        private async ValueTask RunGitAsync(params string[] args)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git command failed ({string.Join(" ", args)}). ExitCode={process.ExitCode}. StdErr={await stdErrTask}. StdOut={await stdOutTask}");
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(RepositoryPath))
                    Directory.Delete(RepositoryPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return ValueTask.CompletedTask;
        }
    }
}
