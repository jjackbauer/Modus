using System.Diagnostics;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Shell.E2E.Tests.E2E;

public sealed class ShellHostE2EHarnessTests
{
    [Fact]
    public async Task ShellReadme_GivenHelpCommand_OutputListsSupportedCommandsIncludingConfigAndDiagnostics()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "help",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Available commands:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("plugins, workflows, config, effective-config, exit", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellReadme_GivenInvalidTransitionSyntax_UsageMessageReturnedWithoutStateMutation()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "usage-path");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "status",
                "transition nope",
                "status",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: transition <Created|Editing|Validating|AwaitingApproval|Approved|Merged>", result.StdOut, StringComparison.Ordinal);
        var createdStatusCount = CountOccurrences(result.StdOut, " is Created for workflow workflow.linear.");
        Assert.True(createdStatusCount >= 2, $"Expected session state to remain Created before and after invalid transition. Output: {result.StdOut}");
    }

    [Fact]
    public async Task ShellHostReadme_GivenConfigFileAndCliPluginsPath_CliOverrideWinsInEffectiveConfigurationOutput()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var configPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-from-config");
        var cliPluginsPath = Path.Combine(fixture.RepositoryPath, "plugins-from-cli");
        var workspaceRoot = Path.Combine(fixture.RepositoryPath, "workspace-root");

        Directory.CreateDirectory(configPluginsPath);
        Directory.CreateDirectory(cliPluginsPath);
        Directory.CreateDirectory(workspaceRoot);

        var configDir = Path.Combine(fixture.RepositoryPath, ".wip");
        Directory.CreateDirectory(configDir);

        var configJson = """
        {
          "pluginsPath": "plugins-from-config",
          "workspaceRoot": "workspace-root",
          "policyId": "policy.from.config",
          "validationCommands": ["dotnet build -c Release", "dotnet test --no-build"]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(configDir, "config.json"), configJson, CancellationToken.None);

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "config",
                "exit"
            ],
            CancellationToken.None,
            args: [cliPluginsPath],
            workingDirectory: fixture.RepositoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Effective configuration:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("policy: policy.from.config", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"pluginsPath: {Path.GetFullPath(cliPluginsPath)}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains($"workspaceRoot: {Path.GetFullPath(workspaceRoot)}", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("validationCommands: dotnet build -c Release | dotnet test --no-build", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain($"pluginsPath: {Path.GetFullPath(configPluginsPath)}", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellReadme_GivenUnknownCommand_DeterministicUnknownCommandMessageIncludesHelpHint()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "bogus-command",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Unknown command 'bogus-command'. Use 'help' to list commands.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellReadme_GivenDiagnosticsCommands_DiagnosticsBridgeOutputIsSurfaced()
    {
        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "plugins",
                "workflows",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Manifest captured at (UTC):", result.StdOut, StringComparison.Ordinal);
        Assert.True(
            result.StdOut.Contains("Loaded plugins:", StringComparison.Ordinal)
            || result.StdOut.Contains("No plugins are currently loaded.", StringComparison.Ordinal),
            $"Expected plugin diagnostics bridge output. StdOut: {result.StdOut}");
        Assert.True(
            result.StdOut.Contains("Registered workflows:", StringComparison.Ordinal)
            || result.StdOut.Contains("No workflows are currently registered.", StringComparison.Ordinal),
            $"Expected workflow diagnostics bridge output. StdOut: {result.StdOut}");
    }

    [Fact]
    public async Task E2E_ShellLifecycle_GivenStartupThroughApprovedMerge_CompletesApprovedChangePathWithSessionEvidence()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "approved-path");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "help",
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "transition Editing",
                "transition Validating",
                "transition AwaitingApproval",
                "transition Approved",
                "transition Merged",
                "status",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Available commands:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session started:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session transitioned to Approved.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Session transitioned to Merged.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("is Merged for workflow workflow.linear.", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("wip[", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task E2E_SafetyGuards_GivenUnapprovedMergeAttempt_RejectsUnsafeTransitionPath()
    {
        await using var fixture = await TempGitRepositoryFixture.CreateAsync();
        var worktreePath = Path.Combine(fixture.RepositoryPath, ".wip", "worktrees", "safety-path");

        var result = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"start workflow.linear {fixture.RepositoryPath} {worktreePath}",
                "transition Editing",
                "transition Validating",
                "transition AwaitingApproval",
                "transition Merged",
                "exit"
            ],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Failed to transition session:", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Invalid transition", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Expected next state is Approved.", result.StdOut, StringComparison.Ordinal);
    }

    private static class ShellHostProcessDriver
    {
        public static async Task<ShellProcessResult> ExecuteScriptAsync(
            IReadOnlyList<string> commands,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? args = null,
            string? workingDirectory = null)
        {
            var shellHostAssemblyPath = typeof(WipShellHost).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(shellHostAssemblyPath)
                    ?? throw new InvalidOperationException("Unable to resolve shell host working directory.")
            };
            startInfo.ArgumentList.Add(shellHostAssemblyPath);
            if (args is not null)
            {
                foreach (var arg in args)
                    startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start shell host process.");

            foreach (var command in commands)
            {
                await process.StandardInput.WriteLineAsync(command);
            }

            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            var stdOutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stdErrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;

            return new ShellProcessResult(process.ExitCode, stdOut, stdErr);
        }
    }

    private static int CountOccurrences(string value, string token)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
            return 0;

        var count = 0;
        var start = 0;
        while (true)
        {
            var index = value.IndexOf(token, start, StringComparison.Ordinal);
            if (index < 0)
                return count;

            count++;
            start = index + token.Length;
        }
    }

    private sealed class TempGitRepositoryFixture : IAsyncDisposable
    {
        private TempGitRepositoryFixture(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static async ValueTask<TempGitRepositoryFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-e2e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var fixture = new TempGitRepositoryFixture(repositoryPath);
            await fixture.RunGitAsync("init", "--initial-branch=main", ".");
            await fixture.RunGitAsync("config", "user.email", "wip-shell-e2e@example.test");
            await fixture.RunGitAsync("config", "user.name", "Wip Shell E2E Tests");

            await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "base\n", CancellationToken.None);
            await fixture.RunGitAsync("add", ".");
            await fixture.RunGitAsync("commit", "-m", "initial commit");

            return fixture;
        }

        public async ValueTask DisposeAsync()
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

            await ValueTask.CompletedTask;
        }

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
                var stdOut = (await stdOutTask).Trim();
                var stdErr = (await stdErrTask).Trim();
                throw new InvalidOperationException(
                    $"Git command failed ({string.Join(" ", args)}). ExitCode={process.ExitCode}. StdOut={stdOut}. StdErr={stdErr}");
            }
        }
    }

    private sealed record ShellProcessResult(int ExitCode, string StdOut, string StdErr);
}
